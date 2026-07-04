#if UNITY_EDITOR
// One-shot scene-wiring repair for the paint-stream feature.
// Root cause: the Task 1 MCP `update_component` call targeted `_SimulationManager`
// (objectPath), but that GameObject never hosted an SPHFluidSolver — the real one lives on
// the `Bucket`. MCP's documented "adds it if not present" behavior created an orphan solver
// (orificeDiameter 0.05, unwired) on _SimulationManager, while the Bucket's solver stayed at
// the stale orificeDiameter 2. Result: stream/droplets/splats 40× too big, splat-size
// regression, and a duplicate-MonoBehaviour Instance-singleton race.
// This tool sets the Bucket solver's orificeDiameter to 0.05 and destroys the orphan.
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class PaintStreamSceneFix
{
    private const string MENU = "Tools/Paint Stream/Fix Scene Wiring (Bucket orificeDiameter + orphan solver)";

    [MenuItem(MENU)]
    public static void Run()
    {
        var solvers = Object.FindObjectsByType<SPHFluidSolver>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Debug.Log($"[PaintStreamFix] Found {solvers.Length} SPHFluidSolver component(s) in active scene.");

        int bucketFixed = 0;
        int orphanDestroyed = 0;
        int unexpected = 0;

        foreach (var s in solvers)
        {
            if (s == null) continue;
            string goName = s.gameObject.name;
            float before = s.orificeDiameter;
            bool pendWired = s.pendulum != null;
            bool bucketWired = s.bucketBuilder != null;

            if (goName == "Bucket")
            {
                Undo.RecordObject(s, "Set Bucket SPHFluidSolver.orificeDiameter = 0.05");
                s.orificeDiameter = 0.05f;
                EditorUtility.SetDirty(s);
                Debug.Log($"[PaintStreamFix] Bucket solver: orificeDiameter {before:F4} -> {s.orificeDiameter:F4}  (useFull3D={s.useFull3D}, pendulumWired={pendWired}, bucketWired={bucketWired})");
                bucketFixed++;
            }
            else if (!pendWired && !bucketWired)
            {
                Debug.LogWarning($"[PaintStreamFix] Destroying orphan SPHFluidSolver on '{goName}' (orificeDiameter={before:F4}, not wired).");
                Undo.DestroyObjectImmediate(s);
                orphanDestroyed++;
            }
            else
            {
                // A wired solver NOT on the Bucket is unexpected — leave it and shout.
                Debug.LogError($"[PaintStreamFix] UNEXPECTED wired SPHFluidSolver on '{goName}' (orificeDiameter={before:F4}, pendulumWired={pendWired}, bucketWired={bucketWired}) — left untouched. REVIEW MANUALLY.");
                unexpected++;
            }
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[PaintStreamFix] Done. Bucket fixed: {bucketFixed}. Orphans destroyed: {orphanDestroyed}. Unexpected: {unexpected}. Save the scene now (File > Save or the MCP save_scene tool).");
    }
}
#endif

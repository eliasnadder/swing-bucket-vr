using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// ponytail: one-shot idempotent editor menu that completes the Phase 2/3/4/5
/// scene wiring the MCP server cannot do (it can't assign UnityEngine.Object
/// reference fields via componentData JSON — every such attempt silently no-ops).
/// Uses SerializedObject.objectReferenceValue, exactly how Unity wires refs.
/// Safe to re-run: duplicates only if the target GO is missing, adds components
/// only if absent, and clears template-copied persistent calls so runtime
/// BindListeners() is the sole listener source.
/// </summary>
public static class PhaseRecovery
{
    [MenuItem("Tools/Phase Recovery (P2/P4/P5)")]
    public static void Run()
    {
        var sb = new StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();

        var content = FindDeepRoot("Content");
        var buttons = FindDeepRoot("Buttons");
        sb.AppendLine($"Content={(content != null)}, Buttons={(buttons != null)}");

        var massSliderGO     = EnsureDuplicate("Rope length",  content != null ? content.transform : null, "Mass");
        var dirSliderGO      = EnsureDuplicate("Wind",         content != null ? content.transform : null, "Direction");
        var flowRateSliderGO = EnsureDuplicate("Paint amount", content != null ? content.transform : null, "Flow Rate");
        var runButtonGO      = EnsureDuplicate("Restart",      buttons != null ? buttons.transform : null,  "Run Simulation");
        sb.AppendLine($"sliders: Mass={massSliderGO != null} Dir={dirSliderGO != null} Flow={flowRateSliderGO != null} RunBtn={runButtonGO != null}");

        var mgr = FindDeepRoot("_SimulationManager");
        if (mgr == null) { Debug.LogError("[PhaseRecovery] _SimulationManager not found"); return; }
        var ui = mgr.GetComponent<SimulationUIManager>();
        if (ui == null) { Debug.LogError("[PhaseRecovery] SimulationUIManager missing"); return; }

        var so = new SerializedObject(ui);
        var bucket = FindDeepRoot("Bucket");
        SetRef(so, sb, "paintEmitter", bucket != null ? bucket.GetComponent<PaintEmitter>() : null);

        SetRef(so, sb, "massSlider",          massSliderGO     != null ? massSliderGO.GetComponent<Slider>()          : null);
        SetRef(so, sb, "massValueText",       massSliderGO     != null ? GetValueTMP(massSliderGO.transform)           : null);
        SetRef(so, sb, "directionSlider",     dirSliderGO      != null ? dirSliderGO.GetComponent<Slider>()           : null);
        SetRef(so, sb, "directionValueText",  dirSliderGO      != null ? GetValueTMP(dirSliderGO.transform)           : null);
        SetRef(so, sb, "flowRateSlider",      flowRateSliderGO != null ? flowRateSliderGO.GetComponent<Slider>()      : null);
        SetRef(so, sb, "flowRateValueText",   flowRateSliderGO != null ? GetValueTMP(flowRateSliderGO.transform)      : null);
        SetRef(so, sb, "runSimulationButton", runButtonGO      != null ? runButtonGO.GetComponent<Button>()           : null);
        so.ApplyModifiedProperties();

        ClearSliderPersistentCalls(massSliderGO?.GetComponent<Slider>());
        ClearSliderPersistentCalls(dirSliderGO?.GetComponent<Slider>());
        ClearSliderPersistentCalls(flowRateSliderGO?.GetComponent<Slider>());
        ClearButtonPersistentCalls(runButtonGO?.GetComponent<Button>());

        EnsureComponent<ExperimentSaver>(mgr, sb);
        EnsureComponent<ExperimentComparer>(mgr, sb);
        EnsureComponent<ReportGenerator>(mgr, sb);

        if (content != null) LayoutRebuilder.ForceRebuildLayoutImmediate(content.GetComponent<RectTransform>());
        if (buttons != null) LayoutRebuilder.ForceRebuildLayoutImmediate(buttons.GetComponent<RectTransform>());

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[PhaseRecovery] done.\n" + sb);
        Selection.activeGameObject = mgr;
    }

    static GameObject EnsureDuplicate(string templateName, Transform parent, string newName)
    {
        if (parent == null) return null;
        var existing = FindIn(parent, newName);
        if (existing != null) return existing.gameObject;
        var tmpl = FindDeepRoot(templateName);
        if (tmpl == null) return null;
        var clone = Object.Instantiate(tmpl, parent, false);
        clone.name = newName;
        return clone;
    }

    static void EnsureComponent<T>(GameObject go, StringBuilder sb) where T : Component
    {
        if (go == null) return;
        if (go.GetComponent<T>() != null) { sb.AppendLine($"  {typeof(T).Name}: already present"); return; }
        go.AddComponent<T>();
        sb.AppendLine($"  +{typeof(T).Name} added");
    }

    static void SetRef(SerializedObject so, StringBuilder sb, string field, Object value)
    {
        var p = so.FindProperty(field);
        if (p == null) { sb.AppendLine($"  {field}: PROPERTY NOT FOUND"); return; }
        p.objectReferenceValue = value;
        sb.AppendLine($"  {field} -> {(value == null ? "NULL" : value.name)}");
    }

    static void ClearSliderPersistentCalls(Slider s)
    {
        if (s == null) return;
        var so = new SerializedObject(s);
        var p = so.FindProperty("m_OnValueChanged.m_PersistentCalls.m_Calls");
        if (p != null) p.arraySize = 0;
        so.ApplyModifiedProperties();
    }

    static void ClearButtonPersistentCalls(Button b)
    {
        if (b == null) return;
        var so = new SerializedObject(b);
        var p = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
        if (p != null) p.arraySize = 0;
        so.ApplyModifiedProperties();
    }

    static GameObject FindDeepRoot(string name)
    {
        foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (go.name == name) return go;
            var f = FindIn(go.transform, name);
            if (f != null) return f.gameObject;
        }
        return null;
    }

    static Transform FindIn(Transform t, string name)
    {
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            var f = FindIn(t.GetChild(i), name);
            if (f != null) return f;
        }
        return null;
    }

    // The Value label is the first direct child named "Value" with a TMP component.
    static TextMeshProUGUI GetValueTMP(Transform sliderRoot)
        => FindIn(sliderRoot, "Value")?.GetComponent<TextMeshProUGUI>();
}

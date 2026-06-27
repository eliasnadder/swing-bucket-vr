using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Snapshot of slider values, environment, and geometry at the moment an
/// experiment is captured. JsonUtility-friendly: only Serializable fields,
/// no dictionaries, no generic arrays of non-primitive types. Paint color is
/// stored as a hex string (Color is also natively supported by JsonUtility
/// but the hex keeps the JSON human-readable).
///</summary>
[System.Serializable]
public class InputsSnapshot
{
    // ── Pendulum ──
    public float ropeL0;
    public float ropeK;
    public float ropeC;
    public float dampingFactor;
    public float gravity;
    public float mass;
    public float windSpeed;
    public float initialTheta;
    public float initialOmega;

    // ── SPH / Fluid ──
    public float initialVolume;
    public float Cd;
    public float orificeDiameter;
    public float paintDensity;
    public float humidity;
    public float temperature;
    public string paintColorHex;

    // ── Bucket ──
    public float bucketBottomRadius;
    public float bucketTopRadius;
    public float bucketHeight;

    // ── Canvas ──
    public float canvasWidth;
    public float canvasHeight;
    public float paintOpacity;
    public float tiltAngle;
    public int textureWidth;
    public int textureHeight;
}

/// <summary>
/// Top-level experiment record — one file per save.
/// Fields are intentionally flat so JsonUtility round-trips cleanly without
/// custom converters. JsonUtility handles Color but not Color[], so color is
/// stored as a separate hex string above (Color struct omitted on purpose).
///</summary>
[System.Serializable]
public class ExperimentData
{
    public InputsSnapshot inputs;
    public float runtimeSeconds;
    public int particlesEmitted;
    public int particlesReachedCanvas;
    public float spreadAreaPx;
    public string outputPngPath;
    public string note;
    public string timestampUtc;
    public string experimentId;

    public string ToJSON() => JsonUtility.ToJson(this, true);

    public static ExperimentData FromJSON(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonUtility.FromJson<ExperimentData>(json); }
        catch (Exception e)
        {
            Debug.LogWarning("[ExperimentSaver] FromJSON failed: " + e.Message);
            return null;
        }
    }

    public override string ToString()
        => $"Experiment {experimentId} @ {timestampUtc} — note='{note}'";
}

[DisallowMultipleComponent]
public class ExperimentSaver : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button exportButton;

    /// <summary>
    /// Folder under Application.persistentDataPath where all *.json files
    /// and side-reports are written. Resolved lazily because
    /// Application.persistentDataPath is only valid at runtime.
    ///</summary>
    public static string ExperimentsFolder
        => Path.Combine(Application.persistentDataPath, "Experiments");

    private void Awake()
    {
        if (exportButton != null)
            exportButton.onClick.AddListener(() => SaveExperimentStatic("button"));
    }

    // ─────────────────────────────────────────
    //  Snapshot — read public API, never modify.
    // ─────────────────────────────────────────
    public static ExperimentData CaptureSnapshot(string note)
    {
        var data = new ExperimentData
        {
            timestampUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm-ssZ"),
            note = string.IsNullOrEmpty(note) ? "" : note,
            experimentId = Guid.NewGuid().ToString("N").Substring(0, 8),
            runtimeSeconds = Time.timeSinceLevelLoad,
            inputs = CaptureInputs(),
        };

        // Metrics — each wrapped in try/catch because they touch the live
        // SPH solver and canvas texture and must not poison the snapshot.
        try { data.particlesEmitted = CaptureParticlesEmitted(); }
        catch (Exception e) { Debug.LogWarning("[ExperimentSaver] particlesEmitted failed: " + e.Message); }

        try { data.particlesReachedCanvas = CaptureParticlesOnCanvas(); }
        catch (Exception e) { Debug.LogWarning("[ExperimentSaver] particlesReachedCanvas failed: " + e.Message); }

        try { data.spreadAreaPx = CaptureSpreadAreaPx(); }
        catch (Exception e) { Debug.LogWarning("[ExperimentSaver] spreadAreaPx failed: " + e.Message); }

        data.outputPngPath = "";
        return data;
    }

    private static InputsSnapshot CaptureInputs()
    {
        var inputs = new InputsSnapshot();

        try
        {
            var pendulum = FindAnyObjectByType<SwingingCoupledSpringPendulum>();
            if (pendulum != null)
            {
                inputs.ropeL0         = SafeFloat(pendulum.L0);
                inputs.ropeK          = SafeFloat(pendulum.k_rope);
                inputs.ropeC          = SafeFloat(pendulum.c_rope);
                inputs.dampingFactor  = SafeFloat(pendulum.b);
                inputs.gravity        = SafeFloat(pendulum.g);
                inputs.mass           = SafeFloat(pendulum.m0);
                inputs.windSpeed      = SafeFloat(pendulum.windSpeed);
                inputs.initialTheta   = SafeFloat(pendulum.initialTheta);
                inputs.initialOmega   = SafeFloat(pendulum.initialOmega);
            }
        }
        catch (Exception e) { Debug.LogWarning("[ExperimentSaver] Pendulum snapshot failed: " + e.Message); }

        try
        {
            var fluid = SPHFluidSolver.Instance;
            if (fluid != null)
            {
                inputs.initialVolume     = SafeFloat(fluid.initialVolume);
                inputs.Cd                = SafeFloat(fluid.Cd);
                inputs.orificeDiameter   = SafeFloat(fluid.orificeDiameter);
                inputs.paintDensity      = SafeFloat(fluid.paintDensity);
                inputs.humidity          = SafeFloat(fluid.humidity);
                inputs.temperature       = SafeFloat(fluid.temperature);
                inputs.paintColorHex     = "#" + ColorUtility.ToHtmlStringRGB(fluid.currentPaintColor);
            }
        }
        catch (Exception e) { Debug.LogWarning("[ExperimentSaver] Fluid snapshot failed: " + e.Message); }

        try
        {
            var bucket = FindAnyObjectByType<BucketBuilder>();
            if (bucket != null)
            {
                inputs.bucketBottomRadius = SafeFloat(bucket.bottomRadius);
                inputs.bucketTopRadius    = SafeFloat(bucket.topRadius);
                inputs.bucketHeight       = SafeFloat(bucket.bucketHeight);
            }
        }
        catch (Exception e) { Debug.LogWarning("[ExperimentSaver] Bucket snapshot failed: " + e.Message); }

        try
        {
            var canvas = FindAnyObjectByType<PaintCanvas>();
            if (canvas != null)
            {
                inputs.canvasWidth   = SafeFloat(canvas.worldSize.x);
                inputs.canvasHeight  = SafeFloat(canvas.worldSize.y);
                inputs.paintOpacity  = SafeFloat(canvas.paintOpacity);
                inputs.tiltAngle     = SafeFloat(canvas.tiltAngle);
                inputs.textureWidth  = canvas.textureWidth;
                inputs.textureHeight = canvas.textureHeight;
            }
        }
        catch (Exception e) { Debug.LogWarning("[ExperimentSaver] Canvas snapshot failed: " + e.Message); }

        return inputs;
    }

    // ─────────────────────────────────────────
    //  Metric helpers — best-effort reads of live state.
    // ─────────────────────────────────────────
    private static int CaptureParticlesEmitted()
    {
        var fluid = SPHFluidSolver.Instance;
        return fluid != null ? fluid.ActiveParticleCount : 0;
    }

    private static int CaptureParticlesOnCanvas()
    {
        var fluid = SPHFluidSolver.Instance;
        var canvas = FindAnyObjectByType<PaintCanvas>();
        if (fluid == null || canvas == null) return 0;

        var particles = fluid.Particles;
        int count = 0;
        for (int i = 0; i < particles.Count; i++)
        {
            if (canvas.IsWorldPointOnCanvas(particles[i].position))
                count++;
        }
        return count;
    }

    private static float CaptureSpreadAreaPx()
    {
        var canvas = FindAnyObjectByType<PaintCanvas>();
        if (canvas == null) return 0f;

        Texture2D tex;
        try
        {
            canvas.FlushPending();      // bake any queued splats to the texture
            tex = canvas.GetPaintTexture();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ExperimentSaver] FlushPending/GetPaintTexture failed: " + e.Message);
            return 0f;
        }
        if (tex == null) return 0f;

        Color[] pixels;
        try { pixels = tex.GetPixels(); }
        catch (Exception e)
        {
            Debug.LogWarning("[ExperimentSaver] GetPixels failed: " + e.Message);
            return 0f;
        }

        int dirty = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (pixels[i].a > 0.01f) dirty++;
        }
        return dirty;
    }

    // ─────────────────────────────────────────
    //  Persistence — never throw.
    // ─────────────────────────────────────────
    public static string SaveExperimentStatic(string note)
    {
        ExperimentData data;
        try { data = CaptureSnapshot(note); }
        catch (Exception e)
        {
            Debug.LogWarning("[ExperimentSaver] Capture failed: " + e.Message);
            return null;
        }

        string path;
        try
        {
            string folder = ExperimentsFolder;
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            string filename = "exp_" + data.experimentId + "_" + data.timestampUtc + ".json";
            path = Path.Combine(folder, filename);
            File.WriteAllText(path, data.ToJSON());
            Debug.Log("[ExperimentSaver] Saved experiment JSON to: " + path);
            return path;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ExperimentSaver] Write failed: " + e.Message);
            return null;
        }
    }

    public static ExperimentData[] LoadAllExperiments()
    {
        var list = new List<ExperimentData>();
        string folder;
        try { folder = ExperimentsFolder; }
        catch (Exception e)
        {
            Debug.LogWarning("[ExperimentSaver] ExperimentsFolder failed: " + e.Message);
            return list.ToArray();
        }
        if (!Directory.Exists(folder)) return list.ToArray();

        string[] files;
        try { files = Directory.GetFiles(folder, "*.json"); }
        catch (Exception e)
        {
            Debug.LogWarning("[ExperimentSaver] GetFiles failed: " + e.Message);
            return list.ToArray();
        }

        foreach (string f in files)
        {
            try
            {
                string txt = File.ReadAllText(f);
                ExperimentData d = ExperimentData.FromJSON(txt);
                if (d != null) list.Add(d);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ExperimentSaver] Skipping unreadable file " + f + ": " + e.Message);
            }
        }
        return list.ToArray();
    }

    // ─────────────────────────────────────────
    //  Numeric sanitation — replace NaN/Inf with 0 so JsonUtility output
    //  stays valid: NaN/Infinity aren't valid JSON tokens.
    // ─────────────────────────────────────────
    private static float SafeFloat(float v)
        => (float.IsNaN(v) || float.IsInfinity(v)) ? 0f : v;
}

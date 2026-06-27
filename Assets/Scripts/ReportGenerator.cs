using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Produces a human-readable Markdown summary of an ExperimentData record.
/// Self-contained — no editor-only dependencies, ships in standalone builds.
///</summary>
[DisallowMultipleComponent]
public class ReportGenerator : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button reportButton;

    private void Awake()
    {
        if (reportButton != null)
            reportButton.onClick.AddListener(SaveLatestReport);
    }

    // ─────────────────────────────────────────
    //  Public API (also referenced by ExperimentComparer).
    // ─────────────────────────────────────────
    public static string BuildMarkdownReport(ExperimentData data)
    {
        if (data == null)
            return "# Experiment Report\n\n_No data provided._\n";

        var sb = new StringBuilder();
        sb.Append("# Experiment Report — ").Append(SafeStr(data.experimentId)).Append('\n');
        sb.Append("Generated UTC: ").Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")).Append("\n\n");

        sb.Append("## Timestamp\n\n");
        sb.Append("- Captured: `").Append(SafeStr(data.timestampUtc)).Append("`\n");
        sb.Append("- Note: `").Append(EscapeBackticks(SafeStr(data.note))).Append("`\n\n");

        sb.Append("## Inputs\n\n");
        var i = data.inputs;
        if (i != null)
        {
            sb.Append("- Pendulum L0 / k_rope / c_rope: ")
              .Append(Fmt(i.ropeL0)).Append(" / ").Append(Fmt(i.ropeK)).Append(" / ").Append(Fmt(i.ropeC)).Append('\n');
            sb.Append("- Pendulum b / g / m0: ")
              .Append(Fmt(i.dampingFactor)).Append(" / ").Append(Fmt(i.gravity)).Append(" / ").Append(Fmt(i.mass)).Append('\n');
            sb.Append("- Initial θ / ω: ")
              .Append(Fmt(i.initialTheta)).Append("° / ").Append(Fmt(i.initialOmega)).Append('\n');
            sb.Append("- Wind speed: ").Append(Fmt(i.windSpeed)).Append('\n');
            sb.Append("- SPH initial volume: ").Append(Fmt(i.initialVolume)).Append('\n');
            sb.Append("- SPH Cd × orificeØ: ")
              .Append(Fmt(i.Cd)).Append(" × ").Append(Fmt(i.orificeDiameter)).Append('\n');
            sb.Append("- Paint density / humidity / temperature: ")
              .Append(Fmt(i.paintDensity)).Append(" / ")
              .Append(Fmt(i.humidity)).Append(" / ")
              .Append(Fmt(i.temperature)).Append('\n');
            sb.Append("- Paint color: ").Append(SafeStr(i.paintColorHex)).Append('\n');
            sb.Append("- Bucket (r × R × h): ")
              .Append(Fmt(i.bucketBottomRadius)).Append(" × ")
              .Append(Fmt(i.bucketTopRadius)).Append(" × ")
              .Append(Fmt(i.bucketHeight)).Append('\n');
            sb.Append("- Canvas (w × h, opacity, tilt): ")
              .Append(Fmt(i.canvasWidth)).Append(" × ").Append(Fmt(i.canvasHeight))
              .Append(", ").Append(Fmt(i.paintOpacity))
              .Append(", ").Append(Fmt(i.tiltAngle)).Append("°\n");
            sb.Append("- Canvas texture: ").Append(i.textureWidth).Append(" × ").Append(i.textureHeight).Append('\n');
        }
        else
        {
            sb.Append("_No inputs snapshot available._\n");
        }
        sb.Append('\n');

        sb.Append("## Runtime\n\n");
        sb.Append("- Wall-clock seconds: ").Append(Fmt(data.runtimeSeconds)).Append("\n\n");

        sb.Append("## Particles\n\n");
        sb.Append("- Emitted: ").Append(data.particlesEmitted).Append('\n');
        sb.Append("- Reached canvas: ").Append(data.particlesReachedCanvas).Append('\n');
        float lossRatio = data.particlesEmitted > 0
            ? (float)(data.particlesEmitted - data.particlesReachedCanvas) / data.particlesEmitted
            : 0f;
        sb.Append("- Loss ratio: ").Append((lossRatio * 100f).ToString("F2")).Append("%\n\n");

        sb.Append("## Spread\n\n");
        sb.Append("- Area in pixels: ").Append(Fmt(data.spreadAreaPx)).Append('\n');
        int texArea = (i?.textureWidth ?? 0) * (i?.textureHeight ?? 0);
        float coverage = texArea > 0f ? Mathf.Clamp01(data.spreadAreaPx / texArea) : 0f;
        sb.Append("- Coverage: ").Append((coverage * 100f).ToString("F2")).Append("% of texture area\n\n");

        sb.Append("## Result\n\n");
        sb.Append(Summarize(data)).Append("\n");

        if (!string.IsNullOrEmpty(data.outputPngPath))
        {
            sb.Append("\n## Output\n\n");
            sb.Append("- PNG: `").Append(SafeStr(data.outputPngPath)).Append("`\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Writes the markdown report to <paramref name="outPath"/>. When null,
    /// falls back to the standard experiments folder with the timestamp
    /// convention "report_<id>_<utc>.md".
    ///</summary>
    public static void SaveReport(ExperimentData data, string outPath = null)
    {
        if (data == null)
        {
            Debug.LogWarning("[ReportGenerator] No data to report on.");
            return;
        }

        try
        {
            string path = outPath;
            if (string.IsNullOrEmpty(path))
            {
                string folder = ExperimentSaver.ExperimentsFolder;
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                path = Path.Combine(folder,
                    "report_" + SafeStr(data.experimentId) + "_" + SafeStr(data.timestampUtc) + ".md");
            }
            File.WriteAllText(path, BuildMarkdownReport(data));
            Debug.Log("[ReportGenerator] Saved report to: " + path);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ReportGenerator] Write failed: " + e.Message);
        }
    }

    // ─────────────────────────────────────────
    //  UI hook — find latest experiment, save its report.
    // ─────────────────────────────────────────
    public void SaveLatestReport()
    {
        ExperimentData[] all;
        try { all = ExperimentSaver.LoadAllExperiments(); }
        catch (Exception e)
        {
            Debug.LogWarning("[ReportGenerator] LoadAllExperiments failed: " + e.Message);
            return;
        }
        if (all == null || all.Length == 0)
        {
            Debug.LogWarning("[ReportGenerator] No experiments found to report.");
            return;
        }
        SaveReport(all[all.Length - 1]);
    }

    // ─────────────────────────────────────────
    //  Internals
    // ─────────────────────────────────────────
    private static string Summarize(ExperimentData data)
    {
        // Heuristic: wider spread + larger loss = more chaotic. Coverage and
        // reach give the "story" — kept independent so the summary line reads
        // cleanly without forcing every experiment into the same bucket.
        var i = data.inputs;
        int texArea = (i?.textureWidth ?? 1) * (i?.textureHeight ?? 1);
        float coverage = texArea > 0f ? Mathf.Clamp01(data.spreadAreaPx / texArea) : 0f;

        float reach = data.particlesEmitted > 0
            ? Mathf.Clamp01((float)data.particlesReachedCanvas / data.particlesEmitted)
            : 0f;
        float loss = data.particlesEmitted > 0
            ? Mathf.Clamp01(1f - reach)
            : 0f;

        float chaos = coverage * 0.5f + loss * 0.5f;

        string character = chaos > 0.65f ? "highly chaotic"
            : chaos > 0.30f    ? "moderately chaotic"
            : "calm";

        string efficiency = reach > 0.65f
            ? "efficient (most emitted particles reached the canvas)"
            : reach > 0.30f
                ? "average efficiency"
                : "lossy (most particles missed the canvas)";

        return $"This run is **{character}** — " +
               $"coverage ~{coverage * 100f:F2}%, " +
               $"reach ~{reach * 100f:F2}%, " +
               $"pointing to **{efficiency}**.";
    }

    private static string Fmt(float? v)
    {
        if (v == null) return "n/a";
        float f = v.Value;
        if (float.IsNaN(f) || float.IsInfinity(f)) return "n/a";
        return f.ToString("F3");
    }

    private static string SafeStr(string s) => s ?? "?";

    private static string EscapeBackticks(string s)
        => string.IsNullOrEmpty(s) ? "" : s.Replace("`", "'");
}

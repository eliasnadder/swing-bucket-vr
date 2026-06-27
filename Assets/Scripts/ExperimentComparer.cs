using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Per-field difference between two experiments. We compare numeric metrics
/// only — comparing slider inputs separately is left to a future tool.
///</summary>
[System.Serializable]
public class FieldDiff
{
    public string field;
    public float  left;
    public float  right;
    public float  percentDifference;
    public string largerSide; // "left" | "right" | "equal"
}

/// <summary>
/// ComparisonResult — output of ExperimentComparer.Compare.
/// aggregateWinnerLeft is true if the LEFT side dominated the metric tally,
/// false if the RIGHT side dominated (or tied).
///</summary>
[System.Serializable]
public class ComparisonResult
{
    public string       pathLeft;
    public string       pathRight;
    public string       leftIdentifier;
    public string       rightIdentifier;
    public FieldDiff[]  perFieldDiffs;
    public string       winner;
    public bool         aggregateWinnerLeft;
}

[DisallowMultipleComponent]
public class ExperimentComparer : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button compareButton;

    [Header("Output")]
    [Tooltip("Optional camera whose targetTexture gets the latest side-by-side render.")]
    public Camera outputCamera;

    [Tooltip("Most recently produced side-by-side texture. UI scripts can read this.")]
    public Texture2D lastSideBySide;

    private void Awake()
    {
        if (compareButton != null)
            compareButton.onClick.AddListener(CompareLatestTwo);
    }

    /// <summary>
    /// Reads the two most recent *.json files in the experiments folder and
    /// produces a ComparisonResult. UI button target.
    ///</summary>
    public void CompareLatestTwo()
    {
        string folder;
        try { folder = ExperimentSaver.ExperimentsFolder; }
        catch (Exception e)
        {
            Debug.LogWarning("[ExperimentComparer] ExperimentsFolder failed: " + e.Message);
            return;
        }
        if (!Directory.Exists(folder))
        {
            Debug.LogWarning("[ExperimentComparer] No experiments folder yet.");
            return;
        }

        string[] files;
        try { files = Directory.GetFiles(folder, "*.json"); }
        catch (Exception e)
        {
            Debug.LogWarning("[ExperimentComparer] GetFiles failed: " + e.Message);
            return;
        }
        if (files.Length < 2)
        {
            Debug.LogWarning("[ExperimentComparer] Need at least 2 experiments to compare (found " + files.Length + ").");
            return;
        }
        Array.Sort(files); // lexical timestamp ordering matches filename format

        string pathA = files[files.Length - 2];
        string pathB = files[files.Length - 1];

        ComparisonResult result = Compare(pathA, pathB);
        if (result == null) return;

        LastPair = new ExperimentData[] { LoadSafe(pathA), LoadSafe(pathB) };
        if (LastPair[0] != null && LastPair[1] != null)
        {
            lastSideBySide = RenderSideBySide(LastPair[0], LastPair[1]);

            if (outputCamera != null && lastSideBySide != null)
            {
                // Camera.targetTexture wants a RenderTexture; convert the Texture2D
                // by blitting into one (creating it lazily if the camera has none).
                if (outputCamera.targetTexture == null)
                {
                    outputCamera.targetTexture = new RenderTexture(
                        lastSideBySide.width, lastSideBySide.height, 0, RenderTextureFormat.ARGB32);
                }
                Graphics.Blit(lastSideBySide, outputCamera.targetTexture);
            }
        }

        Debug.Log("[ExperimentComparer] " + result.winner);
    }

    /// <summary>
    /// Last successfully loaded pair. Useful for ReportGenerator to feed a
    /// side-by-side summary without re-reading from disk.
    ///</summary>
    public ExperimentData[] LastPair { get; private set; }

    private static ExperimentData LoadSafe(string path)
    {
        try { return ExperimentData.FromJSON(File.ReadAllText(path)); }
        catch (Exception e)
        {
            Debug.LogWarning("[ExperimentComparer] Load failed for " + path + ": " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// Reads both JSON files, computes per-field differences, and emits a
    /// tallied winner. Returns a non-null ComparisonResult even on partial
    /// failure; the caller can inspect perFieldDiffs/Length to decide.
    ///</summary>
    public static ComparisonResult Compare(string pathA, string pathB)
    {
        var result = new ComparisonResult
        {
            pathLeft         = pathA,
            pathRight        = pathB,
            perFieldDiffs    = Array.Empty<FieldDiff>(),
            winner           = "Not enough data",
            aggregateWinnerLeft = false,
        };

        if (string.IsNullOrEmpty(pathA) || string.IsNullOrEmpty(pathB))
        {
            Debug.LogWarning("[ExperimentComparer] Compare called with empty path.");
            return result;
        }
        if (!File.Exists(pathA))
        {
            Debug.LogWarning("[ExperimentComparer] pathA missing: " + pathA);
            return result;
        }
        if (!File.Exists(pathB))
        {
            Debug.LogWarning("[ExperimentComparer] pathB missing: " + pathB);
            return result;
        }

        ExperimentData left  = LoadSafe(pathA);
        ExperimentData right = LoadSafe(pathB);
        if (left == null || right == null)
        {
            result.winner = "Unreadable JSON";
            return result;
        }

        result.leftIdentifier  = left.experimentId;
        result.rightIdentifier = right.experimentId;

        var diffs = new List<FieldDiff>
        {
            Diff("spreadAreaPx",            left.spreadAreaPx,         right.spreadAreaPx),
            Diff("particlesEmitted",        left.particlesEmitted,     right.particlesEmitted),
            Diff("particlesReachedCanvas",  left.particlesReachedCanvas, right.particlesReachedCanvas),
            Diff("runtimeSeconds",          left.runtimeSeconds,       right.runtimeSeconds),
        };
        result.perFieldDiffs = diffs.ToArray();

        int leftWins = 0, rightWins = 0;
        for (int i = 0; i < diffs.Count; i++)
        {
            if (diffs[i].left > diffs[i].right) leftWins++;
            else if (diffs[i].right > diffs[i].left) rightWins++;
        }

        bool leftBetter = leftWins > rightWins;
        if (leftWins == rightWins)
            result.winner = $"Tie — {leftWins}/{diffs.Count} metrics equal on each side";
        else
            result.winner = leftBetter
                ? $"Left dominated — {leftWins}/{diffs.Count} metrics higher"
                : $"Right dominated — {rightWins}/{diffs.Count} metrics higher";

        result.aggregateWinnerLeft = leftBetter;
        return result;
    }

    private static FieldDiff Diff(string name, float left, float right)
    {
        // Sanitize inputs — NaN/Infinity make Math.Abs unreliable.
        left  = Sanitize(left);
        right = Sanitize(right);

        float denom = Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(left), Mathf.Abs(right)));
        float pct   = Mathf.Abs(left - right) / denom * 100f;

        string larger = "equal";
        if (left > right)      larger = "left";
        else if (right > left) larger = "right";

        return new FieldDiff
        {
            field             = name,
            left              = left,
            right             = right,
            percentDifference = pct,
            largerSide        = larger,
        };
    }

    /// <summary>
    /// Side-by-side Texture2D: split horizontally into two halves; each half
    /// is tinted by its particlesReachedCanvas ratio against the larger of the
    /// two. Cheap, deterministic, no texture upload needed at runtime.
    ///</summary>
    public static Texture2D RenderSideBySide(ExperimentData left, ExperimentData right, int width = 512, int height = 512)
    {
        width  = Mathf.Max(8, width);
        height = Mathf.Max(8, height);

        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode   = TextureWrapMode.Clamp;

        Color32[] pixels = new Color32[width * height];
        Color32 bg = new Color32(20, 20, 22, 255);
        for (int i = 0; i < pixels.Length; i++) pixels[i] = bg;

        float leftCount  = SafeCount(left);
        float rightCount = SafeCount(right);
        float maxCount   = Mathf.Max(1f, Mathf.Max(leftCount, rightCount));

        Color32 leftTint  = ScoreToColor(leftCount  / maxCount, true);
        Color32 rightTint = ScoreToColor(rightCount / maxCount, false);

        int half = width / 2;
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < half; x++)       pixels[row + x] = leftTint;
            for (int x = half; x < width; x++)   pixels[row + x] = rightTint;
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        return tex;
    }

    private static float SafeCount(ExperimentData d)
        => d != null ? Mathf.Max(0f, d.particlesReachedCanvas) : 0f;

    private static Color32 ScoreToColor(float t, bool warm)
    {
        t = Mathf.Clamp01(t);
        // Warm side (left) → orange-red; cool side (right) → blue-cyan.
        // The base ≈40 keeps both halves legible when t≈0.
        if (warm)
            return new Color32(
                (byte)(140 + 110 * t),
                (byte)( 50 +  80 * t),
                (byte)( 30 +  30 * t),
                255);
        return new Color32(
            (byte)( 30 +  30 * t),
            (byte)( 60 +  90 * t),
            (byte)(120 + 110 * t),
            255);
    }

    private static float Sanitize(float v)
        => (float.IsNaN(v) || float.IsInfinity(v)) ? 0f : v;
}

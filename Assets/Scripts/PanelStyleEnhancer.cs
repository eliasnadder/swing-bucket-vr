using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Runtime restyler for the right panel — dark professional theme.
/// Runs once in Start(), reorganizes slider groups, recolors everything,
/// fixes typos, and injects section headers.
/// Attach to the same GameObject as SimulationUIManager.
/// </summary>
public class PanelStyleEnhancer : MonoBehaviour
{
    // ── Color palette ──────────────────────────────────────────
    static readonly Color PanelBg        = FromHex(0x1a1a2e, 0.92f);
    static readonly Color HeaderBg      = FromHex(0x16213e);
    static readonly Color AccentRed      = FromHex(0xe94560);
    static readonly Color SliderBg      = FromHex(0x0f3460, 0.80f);
    static readonly Color LabelColor     = FromHex(0xe0e0e0);
    static readonly Color ValueColor     = Color.white;
    static readonly Color DropdownBg    = FromHex(0x16213e);
    static readonly Color ButtonBg      = FromHex(0x16213e);

    // ── Section definition ─────────────────────────────────────
    struct Section
    {
        public string title;
        public string[] sliderNames;   // GameObject names in Content
        public Section(string t, string[] s) { title = t; sliderNames = s; }
    }

    static readonly Section[] Sections = new Section[]
    {
        new("PENDULUM", new[]
        {
            "Rope length", "Rope Elasticity", "Rope Damping",
            "Air Damping", "Gravity", "Init angel",
            "Init omega", "NO. Swings"
        }),
        new("FLUID", new[]
        {
            "Orifice Diameter", "Vicosity", "Paint amount"
        }),
        new("ENVIRONMENT", new[]
        {
            "Wind", "Temperature", "Humidity"
        }),
        new("BUCKET", new[]
        {
            "Bucket Radius", "XPivot", "YPivot"
        }),
        new("CANVAS", new[]
        {
            "Tilt", "Canva width", "Canva height"
        }),
    };

    // ── Label typo map (GameObject name → corrected display text) ──
    static readonly (string goName, string fix)[] LabelFixes = new[]
    {
        ("Init angel",  "Initial Angle"),
        ("Vicosity",    "Viscosity"),
        ("NO. Swings",  "Max Swings"),
        ("Canva width", "Canvas Width"),
        ("Canva height","Canvas Height"),
    };

    // ── Refs ───────────────────────────────────────────────────
    SimulationUIManager ui;

    void Start()
    {
        ui = GetComponent<SimulationUIManager>();
        if (ui == null) { Debug.LogError("[PanelStyleEnhancer] No SimulationUIManager found"); return; }

        // Find the Content transform (parent of all sliders)
        Transform content = FindContentTransform();
        if (content == null) { Debug.LogError("[PanelStyleEnhancer] Could not find Content transform"); return; }

        RestylePanelBackground(content);
        AdjustLayout(content);
        ReorderAndGroup(content);
        RestyleSliders(content);
        RestyleDropdownsAndButtons(content);
        FixLabelTypos(content);

        // Remove the old "Pendulum Sliders" text-only header
        // (sections will be re-created by ReorderAndGroup)
    }

    // ────────────────────────────────────────────────────────────
    //  Helpers
    // ────────────────────────────────────────────────────────────

    Transform FindContentTransform()
    {
        // Walk from any slider upward to find the Content parent
        if (ui.lengthSlider != null)
            return ui.lengthSlider.transform.parent;
        // Fallback: search by name
        var all = FindObjectsOfType<RectTransform>();
        foreach (var t in all)
            if (t.name == "Content" && t.parent != null && t.parent.name == "Viewport")
                return t;
        return null;
    }

    void RestylePanelBackground(Transform content)
    {
        // Walk up to the Scroll View
        Transform scroll = content.parent?.parent;
        if (scroll == null) return;

        var img = scroll.GetComponent<Image>();
        if (img != null)
        {
            img.color = PanelBg;
        }
    }

    void AdjustLayout(Transform content)
    {
        var vlg = content.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) return;

        vlg.padding = new RectOffset(15, 15, 15, 15);
        vlg.spacing = 8f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = false;
        vlg.childControlHeight = false;
    }

    void ReorderAndGroup(Transform content)
    {
        // Remove old "Pendulum Sliders" section header if present
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            var child = content.GetChild(i);
            if (child.name == "Pendulum Sliders")
            {
                Destroy(child.gameObject);
            }
        }

        // Collect references to children by GameObject name
        var byName = new System.Collections.Generic.Dictionary<string, Transform>();
        for (int i = 0; i < content.childCount; i++)
        {
            var c = content.GetChild(i);
            byName[c.name] = c;
        }

        // Detach all children (they stay alive, just off the layout)
        var detached = new System.Collections.Generic.List<Transform>();
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            var c = content.GetChild(i);
            c.SetParent(null, false);
            detached.Add(c);
        }

        // Re-attach in grouped order with section headers
        foreach (var section in Sections)
        {
            // Section header
            CreateSectionHeader(content, section.title);

            // Slider rows
            foreach (var sliderName in section.sliderNames)
            {
                if (byName.TryGetValue(sliderName, out var t))
                {
                    t.SetParent(content, false);
                }
            }
        }

        // OPTIONS section — dropdowns
        CreateSectionHeader(content, "OPTIONS");
        foreach (var kv in byName)
        {
            if (kv.Key == "Dropdown" || kv.Key.Contains("dropdown") || kv.Key.Contains("Dropdown"))
            {
                kv.Value.SetParent(content, false);
            }
        }

        // ACTIONS section — buttons
        CreateSectionHeader(content, "ACTIONS");
        foreach (var kv in byName)
        {
            if (kv.Key == "Buttons" || kv.Key.Contains("Button"))
            {
                kv.Value.SetParent(content, false);
            }
        }
    }

    void CreateSectionHeader(Transform parent, string title)
    {
        // Background bar
        var go = new GameObject($"Section_{title}");
        go.SetActive(false); // prevent layout flash

        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 36f;
        le.minHeight = 36f;

        // Background image
        var bg = go.AddComponent<Image>();
        bg.color = HeaderBg;
        bg.raycastTarget = false;

        // Label
        var labelGo = new GameObject("HeaderLabel");
        var labelRt = labelGo.AddComponent<RectTransform>();
        labelRt.SetParent(rt, false);
        labelRt.anchorMin = new Vector2(0, 0);
        labelRt.anchorMax = new Vector2(1, 1);
        labelRt.offsetMin = new Vector2(12, 0);
        labelRt.offsetMax = new Vector2(0, 0);

        var tmp = labelGo.AddComponent<TextMeshProUGUI>();
        tmp.text = $"── {title} ──";
        tmp.fontSize = 18f;
        tmp.color = AccentRed;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Left | TextAlignmentOptions.Middle;
        tmp.raycastTarget = false;

        go.SetActive(true);
    }

    void RestyleSliders(Transform content)
    {
        for (int i = 0; i < content.childCount; i++)
        {
            var child = content.GetChild(i);
            var slider = child.GetComponent<Slider>();
            if (slider == null) continue;

            // Background
            var bg = FindChildImage(child, "Background");
            if (bg != null) bg.color = SliderBg;

            // Fill
            var fill = FindChildImage(child, "Fill");
            if (fill != null) fill.color = AccentRed;

            // Handle
            var handle = FindChildImage(child, "Handle");
            if (handle != null) handle.color = AccentRed;

            // Label text
            var label = FindChildTMP(child, "Label");
            if (label != null)
            {
                label.color = LabelColor;
                label.fontSize = 22f;
            }

            // Value text — often siblings with the slider, find by having a TMP
            // that isn't the label
            var allTMP = child.GetComponentsInChildren<TextMeshProUGUI>();
            foreach (var tmp in allTMP)
            {
                if (tmp.name != "Label")
                {
                    tmp.color = ValueColor;
                    tmp.fontSize = 22f;
                }
            }

            // Add a small layout element for consistent row height
            var le = child.GetComponent<LayoutElement>();
            if (le == null) le = child.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 40f;
            le.minHeight = 40f;
        }
    }

    void RestyleDropdownsAndButtons(Transform content)
    {
        // Dropdowns
        for (int i = 0; i < content.childCount; i++)
        {
            var child = content.GetChild(i);
            var dropdown = child.GetComponent<TMP_Dropdown>();
            if (dropdown != null)
            {
                var bg = child.GetComponent<Image>();
                if (bg != null) bg.color = DropdownBg;

                // Label text
                var label = FindChildTMP(child, "Label");
                if (label != null)
                {
                    label.color = LabelColor;
                    label.fontSize = 22f;
                }

                var le = child.GetComponent<LayoutElement>();
                if (le == null) le = child.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 36f;
                le.minHeight = 36f;
            }
        }

        // Buttons container — style each button child
        for (int i = 0; i < content.childCount; i++)
        {
            var child = content.GetChild(i);
            if (child.name != "Buttons") continue;

            for (int j = 0; j < child.childCount; j++)
            {
                var btn = child.GetChild(j);
                var img = btn.GetComponent<Image>();
                if (img != null) img.color = ButtonBg;

                var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.color = AccentRed;
                    tmp.fontSize = 22f;
                    tmp.fontStyle = FontStyles.Bold;
                }
            }
        }
    }

    void FixLabelTypos(Transform content)
    {
        // Fix slider row GameObject names (also fixes the Label child text)
        for (int i = 0; i < content.childCount; i++)
        {
            var child = content.GetChild(i);
            foreach (var fix in LabelFixes)
            {
                if (child.name == fix.goName)
                {
                    child.name = fix.fix;
                    var label = FindChildTMP(child, "Label");
                    if (label != null) label.text = fix.fix;
                }
            }
        }
    }

    // ── Utility: find child Image by name ──
    static Image FindChildImage(Transform parent, string childName)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            var c = parent.GetChild(i);
            if (c.name == childName)
                return c.GetComponent<Image>();

            // Search one level deeper (e.g. Fill Area > Fill)
            for (int j = 0; j < c.childCount; j++)
            {
                var gc = c.GetChild(j);
                if (gc.name == childName)
                    return gc.GetComponent<Image>();
            }
        }
        return null;
    }

    // ── Utility: find child TMP by name ──
    static TextMeshProUGUI FindChildTMP(Transform parent, string childName)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            var c = parent.GetChild(i);
            if (c.name == childName)
                return c.GetComponent<TextMeshProUGUI>();
        }
        return null;
    }

    // ── Utility: hex color with alpha ──
    static Color FromHex(int rgb, float alpha = 1f)
    {
        float r = ((rgb >> 16) & 0xFF) / 255f;
        float g = ((rgb >> 8) & 0xFF) / 255f;
        float b = (rgb & 0xFF) / 255f;
        return new Color(r, g, b, alpha);
    }
}

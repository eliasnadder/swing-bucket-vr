using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SimulationUIManager : MonoBehaviour
{
    [Header("Engine References")]
    public SwingingPendulum pendulumEngine;
    public FluidSPHSystem fluidEngine;
    public PaintSurfaceCanvas canvasEngine;

    // ============== Pendulum ==============
    [Header("Pendulum Sliders")]
    public Slider lengthSlider;
    public TextMeshProUGUI lengthValueText;

    public Slider elasticitySlider;
    public TextMeshProUGUI elasticityValueText;

    public Slider airResistanceSlider;
    public TextMeshProUGUI airResistanceValueText;

    public Slider gravitySlider;
    public TextMeshProUGUI gravityValueText;

    // ============== Fluid =============
    [Header("Fluid Sliders")]
    public Slider orificeSlider;
    public TextMeshProUGUI orificeValueText;

    // ======= Environment (Section 2.7) =======
    [Header("Environment Sliders")]
    [Tooltip("سرعة الرياح m/s — تؤثر على البندول والجسيمات")]
    public Slider windSpeedSlider;
    public TextMeshProUGUI windSpeedValueText;

    [Tooltip("درجة الحرارة °C — تُعدّل اللزوجة عبر أرينيوس")]
    public Slider temperatureSlider;
    public TextMeshProUGUI temperatureValueText;

    [Tooltip("الرطوبة النسبية 0–100 % — تُعدّل انتشار الطلاء")]
    public Slider humiditySlider;
    public TextMeshProUGUI humidityValueText;

    // ============== Canvas ==============
    [Header("Canvas Sliders")]
    [Tooltip("زاوية ميل اللوحة 0–90° — انزلاق الطلاء")]
    public Slider tiltSlider;
    public TextMeshProUGUI tiltValueText;

    // ============== Dropdowns ==============
    [Header("Dropdowns")]
    public TMP_Dropdown colorDropdown;
    [Tooltip("نوع سطح اللوحة: Canvas / Wood / Metal / Paper")]
    public TMP_Dropdown surfaceTypeDropdown;

    // ============== Buttons ==============
    [Header("Buttons")]
    public Button restartButton;

    // ============== Color palette ==============
    private static readonly Color[] PaintColors =
    {
        Color.red,
        Color.blue,
        Color.green,
        Color.yellow,
        Color.magenta,
        Color.cyan,
        new Color(1f, 0.5f, 0f),   // Orange
        Color.white,
    };

    // ============== Function ==============
    void Start()
    {
        InitSliderValues();
        BindListeners();
        UpdateLabels();
    }

    // ======= Initialise slider values from engine defaults =======
    void InitSliderValues()
    {
        if (pendulumEngine != null)
        {
            SetSlider(lengthSlider, pendulumEngine.L0);
            SetSlider(elasticitySlider, pendulumEngine.k_rope);
            SetSlider(airResistanceSlider, pendulumEngine.b);
            SetSlider(gravitySlider, pendulumEngine.g);
        }
        if (fluidEngine != null)
        {
            SetSlider(orificeSlider, fluidEngine.orificeDiameter);
            SetSlider(windSpeedSlider, fluidEngine.windSpeed);
            SetSlider(temperatureSlider, fluidEngine.temperature);
            SetSlider(humiditySlider, fluidEngine.humidity * 100f);
        }
        if (canvasEngine != null)
        {
            SetSlider(tiltSlider, canvasEngine.tiltAngle);
        }
    }

    static void SetSlider(Slider s, float val)
    { if (s != null) s.value = val; }

    // ============== Bind all listeners ==============
    void BindListeners()
    {
        // Pendulum
        Bind(lengthSlider, v => { if (pendulumEngine) pendulumEngine.L0 = v; });
        Bind(elasticitySlider, v => { if (pendulumEngine) pendulumEngine.k_rope = v; });
        Bind(airResistanceSlider, v => { if (pendulumEngine) pendulumEngine.b = v; });
        Bind(gravitySlider, v => { if (pendulumEngine) pendulumEngine.g = v; });

        // Fluid
        Bind(orificeSlider, v => { if (fluidEngine) fluidEngine.orificeDiameter = v; });

        // Environment — synced to BOTH pendulum wind AND fluid wind
        Bind(windSpeedSlider, v =>
        {
            if (fluidEngine) fluidEngine.windSpeed = v;
            if (pendulumEngine) pendulumEngine.windSpeed = v;
        });
        Bind(temperatureSlider, v => { if (fluidEngine) fluidEngine.temperature = v; });
        Bind(humiditySlider, v => { if (fluidEngine) fluidEngine.humidity = v / 100f; });

        // Canvas
        Bind(tiltSlider, v => { if (canvasEngine) canvasEngine.tiltAngle = v; });

        // Dropdowns
        if (colorDropdown != null) colorDropdown.onValueChanged.AddListener(ApplyColor);
        if (surfaceTypeDropdown != null) surfaceTypeDropdown.onValueChanged.AddListener(ApplySurface);

        // Buttons
        if (restartButton != null) restartButton.onClick.AddListener(RestartScene);
    }

    // Helper — bind slider + label refresh
    void Bind(Slider s, System.Action<float> action)
    {
        if (s == null) return;
        s.onValueChanged.AddListener(v => { action(v); UpdateLabels(); });
    }

    // ============== Colour dropdown ==============
    void ApplyColor(int index)
    {
        if (fluidEngine == null) return;
        fluidEngine.ChangePaintColor(
            index < PaintColors.Length ? PaintColors[index] : Color.red);
    }

    // ============== Surface type dropdown ==============
    void ApplySurface(int index)
    {
        if (canvasEngine == null) return;
        canvasEngine.surfaceType = (PaintSurfaceCanvas.SurfaceType)
            Mathf.Clamp(index, 0, 3);
    }

    // ============== Label update ==============
    void UpdateLabels()
    {
        SetLabel(lengthValueText, lengthSlider, v => $"{v:F2} m");
        SetLabel(elasticityValueText, elasticitySlider, v => $"{v:F0} N/m");
        SetLabel(airResistanceValueText, airResistanceSlider, v => $"{v:F3}");
        SetLabel(gravityValueText, gravitySlider, v => $"{v:F2} m/s²");
        SetLabel(orificeValueText, orificeSlider, v => $"{v * 1000f:F1} mm");
        SetLabel(windSpeedValueText, windSpeedSlider, v => $"{v:F1} m/s");
        SetLabel(temperatureValueText, temperatureSlider, v => $"{v:F0} °C");
        SetLabel(humidityValueText, humiditySlider, v => $"{v:F0} %");
        SetLabel(tiltValueText, tiltSlider, v => $"{v:F0}°");
    }

    static void SetLabel(TextMeshProUGUI label, Slider slider,
                         System.Func<float, string> format)
    {
        if (label != null && slider != null)
            label.text = format(slider.value);
    }

    // ============== Restart ==============
    void RestartScene()
    {
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
    }
}
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SimulationUIManager : MonoBehaviour
{
    private const float UNITS_TO_METERS = 100f;

    [Header("Engine References")]
    public SwingingCoupledSpringPendulum pendulumEngine;
    public FluidSPHSystem fluidEngine;
    public PaintSurfaceCanvas canvasEngine;
    public BucketBuilder bucketBuilder;

    // ============== Pendulum ==============
    [Header("Pendulum Sliders")]
    public Slider lengthSlider;
    public TextMeshProUGUI lengthValueText;

    public Slider elasticitySlider;
    public TextMeshProUGUI elasticityValueText;

    public Slider ropeDampingSlider;        // c_rope
    public TextMeshProUGUI ropeDampingValueText;

    public Slider airResistanceSlider;
    public TextMeshProUGUI airResistanceValueText;

    public Slider gravitySlider;
    public TextMeshProUGUI gravityValueText;

    public Slider initialAngleSlider;       // θ₀
    public TextMeshProUGUI initialAngleValueText;

    public Slider initialOmegaSlider;       // ω₀
    public TextMeshProUGUI initialOmegaValueText;

    // ============== Fluid ==============
    [Header("Fluid Sliders")]
    public Slider orificeSlider;
    public TextMeshProUGUI orificeValueText;

    public Slider viscositySlider;          // لزوجة
    public TextMeshProUGUI viscosityValueText;

    public Slider paintAmountSlider;        // كمية الطلاء 
    public TextMeshProUGUI paintAmountValueText;

    // ============== Environment (Section 2.7) ==============
    [Header("Environment Sliders")]
    public Slider windSpeedSlider;
    public TextMeshProUGUI windSpeedValueText;

    public Slider temperatureSlider;
    public TextMeshProUGUI temperatureValueText;

    public Slider humiditySlider;
    public TextMeshProUGUI humidityValueText;

    // ============== Canvas ==============
    [Header("Canvas Sliders")]
    public Slider tiltSlider;
    public TextMeshProUGUI tiltValueText;

    // ============== Dropdowns ==============
    [Header("Dropdowns")]
    public TMP_Dropdown colorDropdown;
    public TMP_Dropdown surfaceTypeDropdown;

    // ============== Buttons ==============
    [Header("Buttons")]
    public Button restartButton;

    // ============== Color Palette ==============
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

    // ============================================================
    void Start()
    {
        BindListeners();
        InitSliderValues();
        UpdateLabels();
    }

    // ============== Init Values ==============
    void InitSliderValues()
    {
        if (pendulumEngine != null)
        {
            SetSlider(lengthSlider, pendulumEngine.L0 / UNITS_TO_METERS);
            SetSlider(elasticitySlider, pendulumEngine.k_rope);
            SetSlider(ropeDampingSlider, pendulumEngine.c_rope);
            SetSlider(airResistanceSlider, pendulumEngine.b);
            SetSlider(gravitySlider, pendulumEngine.g);
            SetSlider(initialAngleSlider, pendulumEngine.initialTheta);
            SetSlider(initialOmegaSlider, pendulumEngine.initialOmega);
        }
        if (fluidEngine != null)
        {
            if (bucketBuilder != null && orificeSlider != null)
            {
                orificeSlider.minValue = 0.01f;
                orificeSlider.maxValue = bucketBuilder.OrificeMaxDiameter;
            }
            SetSlider(orificeSlider, fluidEngine.orificeDiameter);
            SetSlider(viscositySlider, fluidEngine.viscosity);
            SetSlider(paintAmountSlider, fluidEngine.initialVolume * 1000f);
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

    // ============== Bind Listeners ==============
    void BindListeners()
    {
        // Pendulum
        Bind(lengthSlider, v => { if (pendulumEngine) pendulumEngine.ResetLength(v * UNITS_TO_METERS); });
        Bind(elasticitySlider, v => { if (pendulumEngine) pendulumEngine.k_rope = v; });
        Bind(ropeDampingSlider, v => { if (pendulumEngine) pendulumEngine.c_rope = v; });
        Bind(airResistanceSlider, v => { if (pendulumEngine) pendulumEngine.b = v; });
        Bind(gravitySlider, v => { if (pendulumEngine) pendulumEngine.g = v; });
        Bind(initialAngleSlider, v => { if (pendulumEngine) pendulumEngine.initialTheta = v; });
        Bind(initialOmegaSlider, v => { if (pendulumEngine) pendulumEngine.initialOmega = v; });

        // Fluid
        Bind(orificeSlider, v => { if (fluidEngine) fluidEngine.orificeDiameter = v; });
        Bind(viscositySlider, v => { if (fluidEngine) fluidEngine.viscosity = v; });
        Bind(paintAmountSlider, v => { if (fluidEngine) fluidEngine.initialVolume = v / 1000f; });

        // Environment — synced to pendulum AND fluid
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

    void Bind(Slider s, System.Action<float> action)
    {
        if (s == null) return;
        s.onValueChanged.AddListener(v => { action(v); UpdateLabels(); });
    }

    // ============== Dropdowns ==============
    void ApplyColor(int index)
    {
        if (fluidEngine == null) return;
        fluidEngine.ChangePaintColor(
            index < PaintColors.Length ? PaintColors[index] : Color.red);
    }

    void ApplySurface(int index)
    {
        if (canvasEngine == null) return;
        canvasEngine.surfaceType = (PaintSurfaceCanvas.SurfaceType)Mathf.Clamp(index, 0, 3);
    }

    // ============== Labels ==============
    void UpdateLabels()
    {
        SetLabel(lengthValueText, lengthSlider, v => $"{v:F2} m");
        SetLabel(elasticityValueText, elasticitySlider, v => $"{v:F0} N/m");
        SetLabel(ropeDampingValueText, ropeDampingSlider, v => $"{v:F1}");
        SetLabel(airResistanceValueText, airResistanceSlider, v => $"{v:F3}");
        SetLabel(gravityValueText, gravitySlider, v => $"{v:F2} m/s²");
        SetLabel(initialAngleValueText, initialAngleSlider, v => $"{v:F0}°");
        SetLabel(initialOmegaValueText, initialOmegaSlider, v => $"{v:F2} rad/s");
        SetLabel(orificeValueText, orificeSlider, v => $"{v * 1000f:F1} mm");
        SetLabel(viscosityValueText, viscositySlider, v => $"{v:F3}");
        SetLabel(paintAmountValueText, paintAmountSlider, v => $"{v:F1} L");
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
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }
}
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SimulationUIManager : MonoBehaviour
{
    private const float UNITS_TO_METERS = 100f;

    [Header("Engine References")]
    public SwingingCoupledSpringPendulum pendulumEngine;
    public SPHFluidSolver fluidEngine;
    public PaintCanvas canvasEngine;
    public BucketBuilder bucketBuilder;

    // ============== Pendulum ==============
    [Header("Pendulum Sliders")]
    public Slider lengthSlider;
    public TextMeshProUGUI lengthValueText;

    public Slider elasticitySlider;
    public TextMeshProUGUI elasticityValueText;

    public Slider ropeDampingSlider;
    public TextMeshProUGUI ropeDampingValueText;

    public Slider airResistanceSlider;
    public TextMeshProUGUI airResistanceValueText;

    public Slider gravitySlider;
    public TextMeshProUGUI gravityValueText;

    public Slider initialAngleSlider;
    public TextMeshProUGUI initialAngleValueText;

    public Slider initialOmegaSlider;
    public TextMeshProUGUI initialOmegaValueText;

    public Slider numberOfSwingsSlider;
    public TextMeshProUGUI numberOfSwingsValueText;

    public Slider xpivotSlider;
    public TextMeshProUGUI xpivotValueText;

    public Slider ypivotSlider;
    public TextMeshProUGUI ypivotValueText;

    // ============== Fluid ==============
    [Header("Fluid Sliders")]
    public Slider orificeSlider;
    public TextMeshProUGUI orificeValueText;

    public Slider viscositySlider;
    public TextMeshProUGUI viscosityValueText;

    public Slider paintAmountSlider;
    public TextMeshProUGUI paintAmountValueText;

    // ============== Bucket ==============
    [Header("Bucket Sliders")]
    public Slider bucketRadiusSlider;
    public TextMeshProUGUI bucketRadiusValueText;

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

    public Slider canvasWidthSlider;
    public TextMeshProUGUI canvasWidthValueText;

    public Slider canvasHeightSlider;
    public TextMeshProUGUI canvasHeightValueText;

    // ============== Dropdowns ==============
    [Header("Dropdowns")]
    public TMP_Dropdown colorDropdown;
    public TMP_Dropdown surfaceTypeDropdown;

    // ============== Buttons ==============
    [Header("Buttons")]
    public Button restartButton;

    // ============== Slider Ranges (Section: bug-fix) ==============
    // ─────────────────────────────────────────────────────────────
    // ⚠️ السبب الجذري للمشكلة: كانت كل الـ Sliders في المشهد بنطاقها
    // الافتراضي (Min=0 / Max=1). عند استدعاء InitSliderValues() نحاول
    // وضع قيم حقيقية مثل g=981 أو L0=8.5(م تحويل) داخل Slider بنطاق
    // [0,1]. Unity يكتم (clamp) القيمة تلقائياً، وبسبب أن onValueChanged
    // مربوط مسبقاً بـ BindListeners()، تتم إعادة كتابة المتغيّر الحقيقي
    // بالقيمة المكتومة الخاطئة (مثلاً g يصبح 1 بدل 981!).
    //
    // الحل: نضبط Min/Max لكل Slider بالكود قبل تعيين القيمة، بنفس
    // الفكرة المطبّقة سابقاً على orificeSlider فقط.
    // ─────────────────────────────────────────────────────────────
    [Header("Slider Range Overrides")]
    public Vector2 lengthRange = new Vector2(0.1f, 10f);     // متر
    public Vector2 elasticityRange = new Vector2(0f, 2000f);     // k_rope
    public Vector2 ropeDampingRange = new Vector2(0f, 50f);       // c_rope
    public Vector2 airResistanceRange = new Vector2(0f, 2f);        // b
    public Vector2 gravityRange = new Vector2(0f, 2000f);     // cm/s²
    public Vector2 initialAngleRange = new Vector2(-90f, 90f);     // درجة
    public Vector2 initialOmegaRange = new Vector2(-10f, 10f);     // rad/s
    public Vector2 viscosityRange = new Vector2(0f, 5f);
    public Vector2 paintAmountRange = new Vector2(0f, 2000f);     // (L0×1000)
    public Vector2 windSpeedRange = new Vector2(-20f, 20f);
    public Vector2 temperatureRange = new Vector2(-10f, 60f);
    public Vector2 humidityRange = new Vector2(0f, 100f);      // %
    public Vector2 tiltRange = new Vector2(0f, 90f);       // درجة
    public Vector2 pivotXRange = new Vector2(-75f, 75f);     // cm (PivotX)
    public Vector2 pivotYRange = new Vector2(0f, 200f);      // cm (PivotY)
    public Vector2 bucketRadiusRange = new Vector2(5f, 50f); // cm (bottomRadius × 100)
    public Vector2 numberOfSwingsRange = new Vector2(0f, 50f); // عدّاد (maxSwings)
    public Vector2 canvasWidthRange = new Vector2(50f, 500f);  // cm (worldSize.x × 100)
    public Vector2 canvasHeightRange = new Vector2(50f, 500f); // cm (worldSize.y × 100)

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
        ApplySliderRanges();   // ← يجب أن يحدث أولاً، قبل أي SetSlider
        BindListeners();
        InitSliderValues();
        UpdateLabels();
    }

    // ── يضبط Min/Max لكل Slider بحيث لا تُكتم القيم الحقيقية ──
    void ApplySliderRanges()
    {
        SetSliderRange(lengthSlider, lengthRange);
        SetSliderRange(elasticitySlider, elasticityRange);
        SetSliderRange(ropeDampingSlider, ropeDampingRange);
        SetSliderRange(airResistanceSlider, airResistanceRange);
        SetSliderRange(gravitySlider, gravityRange);
        SetSliderRange(initialAngleSlider, initialAngleRange);
        SetSliderRange(initialOmegaSlider, initialOmegaRange);
        SetSliderRange(viscositySlider, viscosityRange);
        SetSliderRange(paintAmountSlider, paintAmountRange);
        SetSliderRange(windSpeedSlider, windSpeedRange);
        SetSliderRange(temperatureSlider, temperatureRange);
        SetSliderRange(humiditySlider, humidityRange);
        SetSliderRange(tiltSlider, tiltRange);
        SetSliderRange(xpivotSlider, pivotXRange);
        SetSliderRange(ypivotSlider, pivotYRange);
        SetSliderRange(bucketRadiusSlider, bucketRadiusRange);
        SetSliderRange(numberOfSwingsSlider, numberOfSwingsRange);
        SetSliderRange(canvasWidthSlider, canvasWidthRange);
        SetSliderRange(canvasHeightSlider, canvasHeightRange);

        // orificeSlider يبقى خاصاً لأن نطاقه يعتمد على BucketBuilder
        if (bucketBuilder != null && orificeSlider != null)
        {
            orificeSlider.minValue = 0.01f;
            orificeSlider.maxValue = bucketBuilder.OrificeMaxDiameter;
        }
    }

    static void SetSliderRange(Slider s, Vector2 range)
    {
        if (s == null) return;
        s.minValue = range.x;
        s.maxValue = range.y;
    }

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
            SetSlider(numberOfSwingsSlider, pendulumEngine.maxSwings);
            SetSlider(xpivotSlider, pendulumEngine.PivotX);
            SetSlider(ypivotSlider, pendulumEngine.PivotY);
        }
        if (fluidEngine != null)
        {
            SetSlider(orificeSlider, fluidEngine.orificeDiameter);
            SetSlider(viscositySlider, fluidEngine.viscosity);
            SetSlider(paintAmountSlider, fluidEngine.initialVolume * 1000f);
            SetSlider(windSpeedSlider, fluidEngine.windSpeed);
            SetSlider(temperatureSlider, fluidEngine.temperature);
            SetSlider(humiditySlider, fluidEngine.humidity * 100f);
        }
        if (bucketBuilder != null)
        {
            // bottomRadius is already stored in cm (post-cm-migration) → slider displays cm directly.
            SetSlider(bucketRadiusSlider, bucketBuilder.bottomRadius);
        }
        if (canvasEngine != null)
        {
            SetSlider(tiltSlider, canvasEngine.tiltAngle);
            // worldSize is already stored in cm (post-cm-migration) → slider displays cm directly.
            SetSlider(canvasWidthSlider, canvasEngine.worldSize.x);
            SetSlider(canvasHeightSlider, canvasEngine.worldSize.y);
        }
    }

    static void SetSlider(Slider s, float val)
    { if (s != null) s.value = val; }

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
        Bind(numberOfSwingsSlider, v => { if (pendulumEngine) pendulumEngine.maxSwings = Mathf.RoundToInt(v); });
        Bind(xpivotSlider, v => { if (pendulumEngine) pendulumEngine.PivotX = v; });
        Bind(ypivotSlider, v => { if (pendulumEngine) pendulumEngine.PivotY = v; });

        // Fluid
        Bind(orificeSlider, v => { if (fluidEngine) fluidEngine.orificeDiameter = v; });
        Bind(viscositySlider, v => { if (fluidEngine) fluidEngine.viscosity = v; });
        Bind(paintAmountSlider, v => { if (fluidEngine) fluidEngine.initialVolume = v / 1000f; });

        // Environment
        Bind(windSpeedSlider, v =>
        {
            if (fluidEngine) fluidEngine.windSpeed = v;
            if (pendulumEngine) pendulumEngine.windSpeed = v;
        });
        Bind(temperatureSlider, v => { if (fluidEngine) fluidEngine.temperature = v; });
        Bind(humiditySlider, v => { if (fluidEngine) fluidEngine.humidity = v / 100f; });

        // Bucket
        Bind(bucketRadiusSlider, v => { if (bucketBuilder) bucketBuilder.bottomRadius = v; });

        // Canvas
        Bind(tiltSlider, v => { if (canvasEngine) canvasEngine.tiltAngle = v; });
        Bind(canvasWidthSlider, v =>
        {
            if (!canvasEngine) return;
            Vector2 s = canvasEngine.worldSize;
            s.x = v;
            canvasEngine.worldSize = s;
        });
        Bind(canvasHeightSlider, v =>
        {
            if (!canvasEngine) return;
            Vector2 s = canvasEngine.worldSize;
            s.y = v;
            canvasEngine.worldSize = s;
        });

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

    void ApplyColor(int index)
    {
        if (fluidEngine == null) return;
        fluidEngine.ChangePaintColor(index < PaintColors.Length ? PaintColors[index] : Color.red);
    }

    void ApplySurface(int index)
    {
        if (canvasEngine == null) return;
        canvasEngine.surfaceType = (PaintCanvas.SurfaceType)Mathf.Clamp(index, 0, 3);
    }

    void UpdateLabels()
    {
        SetLabel(lengthValueText, lengthSlider, v => $"{v:F2} m");
        SetLabel(elasticityValueText, elasticitySlider, v => $"{v:F0} N/m");
        SetLabel(ropeDampingValueText, ropeDampingSlider, v => $"{v:F1}");
        SetLabel(airResistanceValueText, airResistanceSlider, v => $"{v:F3}");
        SetLabel(gravityValueText, gravitySlider, v => $"{v:F2} m/s²");
        SetLabel(initialAngleValueText, initialAngleSlider, v => $"{v:F0}°");
        SetLabel(initialOmegaValueText, initialOmegaSlider, v => $"{v:F2} rad/s");
        SetLabel(orificeValueText, orificeSlider, v => $"{v * 10f:F1} mm");
        SetLabel(viscosityValueText, viscositySlider, v => $"{v:F3}");
        SetLabel(paintAmountValueText, paintAmountSlider, v => $"{v:F1} L");
        SetLabel(windSpeedValueText, windSpeedSlider, v => $"{v:F1} m/s");
        SetLabel(temperatureValueText, temperatureSlider, v => $"{v:F0} °C");
        SetLabel(humidityValueText, humiditySlider, v => $"{v:F0} %");
        SetLabel(tiltValueText, tiltSlider, v => $"{v:F0}°");
        SetLabel(numberOfSwingsValueText, numberOfSwingsSlider, v => $"{v:F0}");
        SetLabel(xpivotValueText, xpivotSlider, v => $"{v:F1} cm");
        SetLabel(ypivotValueText, ypivotSlider, v => $"{v:F1} cm");
        SetLabel(bucketRadiusValueText, bucketRadiusSlider, v => $"{v:F1} cm");
        SetLabel(canvasWidthValueText, canvasWidthSlider, v => $"{v:F0} cm");
        SetLabel(canvasHeightValueText, canvasHeightSlider, v => $"{v:F0} cm");
    }

    static void SetLabel(TextMeshProUGUI label, Slider slider, System.Func<float, string> format)
    {
        if (label != null && slider != null)
            label.text = format(slider.value);
    }

    void RestartScene()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }
}
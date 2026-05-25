using UnityEngine;

public class PaintSurfaceCanvas : MonoBehaviour
{
    public static PaintSurfaceCanvas Instance { get; private set; }

    // ── أنواع الأسطح (Section 2.6.3.1) ──
    public enum SurfaceType { Canvas, Wood, Metal, Paper }

    // جدول معاملات الأسطح: [Absorption α, Friction μ, Roughness]
    //                         Canvas   Wood    Metal   Paper
    private static readonly float[,] SurfaceTable =
    {
        /* α — Absorption */ { 0.75f,  0.48f,  0.90f,  0.15f },
        /* μ — Friction    */ { 0.85f,  0.68f,  0.15f,  0.75f },
        /* Roughness       */ { 0.07f,  0.20f,  0.70f,  0.05f },
    };

    [Header("Canvas Texturing")]
    public Renderer canvasRenderer;
    public int textureSize = 512;

    [Header("Surface Properties (Section 2.6.3)")]
    public SurfaceType surfaceType = SurfaceType.Canvas;

    [Tooltip("زاوية ميل اللوحة بالدرجات — تُسبب انزلاق الطلاء (Section 2.6.3.3)")]
    [Range(0f, 90f)] public float tiltAngle = 0f;

    [Header("Gaussian Model Parameters (Section 2.6.1)")]
    [Tooltip("معامل الانتشار الغاوسي σ")]
    public float sigma = 0.05f;           // معامل الانتشار الغاوسي القياسي
    public float absorptionCoeff = 0.8f;  // معامل الامتصاص للسطح

    [Header("Weber Number Model (Section 2.6.2)")]
    [Tooltip("معامل التوتر السطحي σ_s (N/m)")]
    public float surfaceTension = 0.07f;
    [Tooltip("قطر جسيم الطلاء التقديري (m)")]
    public float particleDiameter = 0.01f;
    [Tooltip("معامل تأثير We على نصف قطر البقعة")]
    public float weberRadiusScale = 0.002f;

    // =============== Private state ===============
    private Texture2D paintTexture;
    private Color[] bufferColors;
    private Bounds aabbBounds;       // AABB للاصطدام المبدئي (Section 2.5.2)

    // اختصارات معاملات السطح الحالي
    int SurfaceIdx => (int)surfaceType;
    float Absorption => SurfaceTable[0, SurfaceIdx]; // α
    float Friction => SurfaceTable[1, SurfaceIdx]; // μ

    // =============== Function =============== 
    void Awake() { Instance = this; }

    void Start()
    {
        // إنشاء خامة مخصصة للرسم الفوري دون كولايدرات
        paintTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
        bufferColors = new Color[textureSize * textureSize];

        // تلوين اللوحة بالخلفية البيضاء الأساسية
        for (int i = 0; i < bufferColors.Length; i++) bufferColors[i] = Color.white;

        paintTexture.SetPixels(bufferColors);
        paintTexture.Apply();
        canvasRenderer.material.mainTexture = paintTexture;
        RefreshAABB();
    }

    // تحديث AABB إذا تحرك الـ Canvas في المشهد
    void LateUpdate() => RefreshAABB();
    void RefreshAABB() => aabbBounds = canvasRenderer.bounds;

    public Texture2D GetPaintTexture() => paintTexture;

    // كشف الاصطدام: AABB Broad-Phase + Canvas Plane Narrow-Phase
    // (Section 2.5.2)
    public bool CheckCollision(Vector3 worldPos)
    {
        // ── المرحلة الواسعة AABB ──
        Bounds expanded = aabbBounds;
        expanded.Expand(0.15f);              // هامش بسيط لمنع التخطي
        if (!expanded.Contains(worldPos)) return false;

        // ── المرحلة الدقيقة: المسافة الموقعة إلى مستوى اللوحة ──
        // نقطة التقاطع تحدث حين ينتقل الجسيم إلى الجهة الأخرى من المستوى
        Vector3 toPoint = worldPos - transform.position;
        float signedDist = Vector3.Dot(toPoint, transform.up);
        return signedDist <= 0.05f;          // عتبة 5 سم
    }

    public Vector3 GetSurfaceNormal() => transform.up;

    public void PaintAt(Vector3 worldPos, Vector3 impactVelocity, Color paintColor)
    {
        // تحويل المسقط العالمي ثلاثي الأبعاد إلى إحداثيات ثنائية الأبعاد على الـ Texture UV
        Vector3 localPos = transform.InverseTransformPoint(worldPos);
        float u = localPos.x + 0.5f;
        float v = localPos.z + 0.5f;

        int centerX = Mathf.RoundToInt(u * textureSize);
        int centerY = Mathf.RoundToInt(v * textureSize);

        float speed = impactVelocity.magnitude;

        // ── نموذج عدد ويبر (Section 2.6.2) ──
        // We = ρ · v² · D / σ_s
        float We = (1200f * speed * speed * particleDiameter) / surfaceTension;

        // نصف قطر البقعة = Gaussian baseline × (1 + We·scale) × α_surface
        float effectiveSigma = sigma * (1f + weberRadiusScale * We) * Absorption;

        // حساب قوة البقعة وحجمها بناءً على الزخم ومعامل الامتصاص
        // float impactEnergy = impactVelocity.magnitude * absorptionCoeff;
        float impactEnergy = speed * Absorption;
        // int radius = Mathf.RoundToInt(sigma * textureSize * impactEnergy);
        int radius = Mathf.Clamp(
            Mathf.RoundToInt(effectiveSigma * textureSize * impactEnergy),
            1, textureSize / 4);

        // ── تأثير ميل اللوحة (Section 2.6.3.3) ──
        // F_parallel = m·g·sin(α)  →  offset لوني في اتجاه الميل
        float tiltRad = tiltAngle * Mathf.Deg2Rad;
        int tiltShift = Mathf.RoundToInt(Mathf.Sin(tiltRad) * radius * Friction);

        // ── تأثير الرطوبة (Section 2.7.2) ──
        float humidityMult = FluidSPHSystem.Instance != null
            ? FluidSPHSystem.Instance.HumiditySpreadFactor : 1f;

        // مسح المنطقة المحيطة بمركز البقعة وتطبيق معادلة غاوس الرياضية
        // ── رسم التوزيع الغاوسي (Section 2.6.1): I(r) = I₀·exp(-r²/(2σ²)) ──
        for (int x = centerX - radius; x <= centerX + radius; x++)
        {
            for (int y = centerY - radius + tiltShift; y <= centerY + radius; y++)
            {
                if (x >= 0 && x < textureSize && y >= 0 && y < textureSize)
                {
                    // float distSq = Mathf.Pow(x - centerX, 2) + Mathf.Pow(y - centerY, 2);
                    float distSq = Mathf.Pow(x - centerX, 2)
                                 + Mathf.Pow(y - (centerY + tiltShift), 2);

                    float maxDistSq = Mathf.Pow(radius, 2);

                    if (distSq <= maxDistSq)
                    {
                        // I(r) = I0 * exp(-r^2 / (2 * sigma^2))
                        float r_normalized = distSq / maxDistSq;
                        // float intensity = Mathf.Exp(-r_normalized * 2f) * impactEnergy;
                        float intensity = Mathf.Exp(-r_normalized * 2f) * impactEnergy * humidityMult;

                        int pixelIndex = y * textureSize + x;
                        // دمج لون الطلاء (مثلاً الأحمر) مع اللون الحالي للوحة تبعا للكثافة الغاوسية
                        // bufferColors[pixelIndex] = Color.Lerp(bufferColors[pixelIndex], Color.red, intensity);
                        bufferColors[pixelIndex] = Color.Lerp(bufferColors[pixelIndex], paintColor, Mathf.Clamp01(intensity));
                    }
                }
            }
        }

        // تطبيق التعديلات البصرية بشكل فوري وعالي الأداء
        paintTexture.SetPixels(bufferColors);
        paintTexture.Apply();
    }

}
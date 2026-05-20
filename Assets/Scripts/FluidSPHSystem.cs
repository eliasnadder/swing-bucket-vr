using UnityEngine;
using System.Collections.Generic;

public class FluidSPHSystem : MonoBehaviour
{
    public static FluidSPHSystem Instance { get; private set; }

    public struct SPHParticle
    {
        public Vector3 position;
        public Vector3 velocity;
        public float density;
        public float pressure;
        public Color color;
    }

    [Header("Relying References")]
    public SwingingPendulum pendulum;

    [Header("Fluid Properties (Table 2.4.2)")]
    public float orificeDiameter = 0.05f; // قطر فتحة الخروج d
    public float h_paint = 0.3f;          // ارتفاع عمود الطلاء داخل الدلو h
    public float Cd = 0.6f;               // معامل التدفق
    public float paintDensity0 = 1200f;   // الكثافة الطبيعية للطلاء
    public float viscosity = 0.1f;        // معامل اللزوجة
    public float h_kernel = 0.2f;         // نصف قطر دالة التنعيم SPH

    [Header("Environment (Section 2.7)")]
    [Tooltip("درجة الحرارة بالسيليزيوس — تؤثر على اللزوجة عبر نموذج أرينيوس")]
    public float temperature = 25f;

    [Tooltip("الرطوبة النسبية (0–1) — تؤثر على معامل انتشار الطلاء")]
    [Range(0f, 1f)] public float humidity = 0.5f;


    [Tooltip("سرعة الرياح (m/s)")]
    public float windSpeed = 0f;
    public Vector3 windDirection = Vector3.right;
    public float windCoeff = 0.5f;          // cw (Section 2.7.3)

    [Header("Color")]
    public Color currentPaintColor = Color.red;


    // =============== Private state ===============
    private List<SPHParticle> particles = new List<SPHParticle>();
    private float initialVolume = 0.01f; // حجم الطلاء الابتدائي م3
    private float currentVolume;
    private float initialPaintHeight;


    // Poly6 kernel normalization constant 3D: 315 / (64π·h⁹)
    private float poly6Norm;

    // =============== constants ===============
    private const float T_REF = 25f;     // درجة حرارة مرجعية (°C)
    private const float K_TEMP = 0.03f;   // حساسية اللزوجة للحرارة
    private const float BETA_H = 0.5f;    // حساسية الانتشار للرطوبة


    // =============== Function =============== 
    void Awake() { Instance = this; }

    void Start()
    {
        currentVolume = initialVolume;
        initialPaintHeight = h_paint;
        poly6Norm = 315f / (64f * Mathf.PI * Mathf.Pow(h_kernel, 9f));
        if (pendulum == null) pendulum = GetComponent<SwingingPendulum>();
    }

    // ===================================================
    // خصائص بيئية مشتقة

    /// <summary>
    /// نموذج أرينيوس المبسط (Section 2.7.1):
    /// μ(T) = μ₀ · exp( k_T · (T_ref − T) )
    /// الحرارة العالية → لزوجة أقل → تدفق أسرع
    /// </summary>
    public float EffectiveViscosity =>
        viscosity * Mathf.Exp(K_TEMP * (T_REF - temperature));

    /// <summary>
    /// معامل انتشار الطلاء بتأثير الرطوبة (Section 2.7.2):
    /// k_spread = k₀ · (1 + β · H)
    /// </summary>
    public float HumiditySpreadFactor => 1f + BETA_H * humidity;
    // ===================================================

    // Public method for UI interaction to refill or change fluid settings dynamically
    public void ChangePaintColor(Color newColor) => currentPaintColor = newColor;

    void FixedUpdate()
    {
        if (currentVolume <= 0) return;

        float dt = Time.fixedDeltaTime;

        //  =============== قانون تورتشيلي (Section 2.4.4.1)  ===============
        // 1. تطبيق قانون تورتشيلي لحساب سرعة ومعدل التدفق (Q = Cd * A * Sqrt(2 * geff * h))
        float area = Mathf.PI * Mathf.Pow(orificeDiameter / 2f, 2);
        float geff = pendulum.EffectiveGravity;
        float v_out = Mathf.Sqrt(2f * geff * h_paint);
        float Q = Cd * area * v_out;

        // حساب الحجم والكتلة الخارجة لتحديث البندول
        float volumeToEmit = Q * dt;
        currentVolume -= volumeToEmit;
        float massToEmit = volumeToEmit * paintDensity0;
        pendulum.UpdateBucketMass(massToEmit);

        // تحديث ارتفاع عمود السائل المتناقص تدريجياً داخل الدلو
        // استخدم الارتفاع الابتدائي المخزن بدل 0.3f الثابتة
        // h_paint = (currentVolume / initialVolume) * 0.3f;
        h_paint = (currentVolume / initialVolume) * initialPaintHeight;

        // 2. توليد الجسيمات بناءً على معدل التدفق اللحظي
        int particlesToSpawn = Mathf.RoundToInt(Q * 10000f * dt); // عامل تحجيم بصري للجسيمات
        for (int i = 0; i < particlesToSpawn; i++)
        {
            SPHParticle p = new SPHParticle();
            p.position = transform.position; // تبدأ من أسفل الدلو
            // سرعة المقذوف = سرعة الدلو + سرعة الاندفاع لأسفل
            p.velocity = pendulum.BucketVelocity + Vector3.down * v_out;
            p.color = currentPaintColor; // Assign the active color at emission moment
            p.density = paintDensity0;
            p.pressure = 0f;
            particles.Add(p);
        }

        // 3. تحديث جزيئات SPH (حساب الكثافة والضغط واللزوجة) والتكامل الحركي الجسيمي
        UpdateSPH();
    }

    void UpdateSPH()
    {
        float dt = Time.fixedDeltaTime;
        float mu = EffectiveViscosity;

        // قوة الرياح على الجسيمات (Section 2.7.3): F_wind = cw · vw²
        Vector3 F_wind = windDirection.normalized * windCoeff * windSpeed * windSpeed;

        // ── المرور الأول: الكثافة والضغط (SPH Section 2.4.3) ──
        // حساب الكثافة والضغط لكل جسيم تبعاً للمحيطين به (SPH Kernel)
        for (int i = 0; i < particles.Count; i++)
        {
            float densitySum = 0f;
            for (int j = 0; j < particles.Count; j++)
            {
                float dist = Vector3.Distance(particles[i].position, particles[j].position);
                if (dist < h_kernel)
                {
                    // Poly6 Kernel Approximation
                    // densitySum += paintDensity0 * Mathf.Pow(h_kernel * h_kernel - dist * dist, 3);
                    // Add a normalization factor (2D Poly6 constant is 4/(π·h⁸), 3D is 315/(64π·h⁹))
                    float q = Mathf.Pow(Mathf.Pow(h_kernel, 2) - Mathf.Pow(dist, 2), 3);
                    densitySum += paintDensity0 * poly6Norm * q;
                }
            }
            SPHParticle p = particles[i];
            p.density = Mathf.Max(paintDensity0, densitySum);
            p.pressure = 2000f * (p.density - paintDensity0); // معادلة الحالة للضغط
            particles[i] = p;
        }

        // ── المرور الثاني: التكامل الحركي + الاصطدام (AABB) ──
        float particleArea = Mathf.PI * 0.01f * 0.01f;

        // ── المرور الثاني: التكامل الحركي + الاصطدام (AABB) ──
        // تطبيق القوى وتحديث المواقع (محاكاة مقذوفات حرّة خاضعة للجاذبية ومقاومة الهواء)
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            SPHParticle p = particles[i];

            // القوى الخارجية المؤثرة (الجاذبية + مقاومة الهواء الفيزيائية Fdrag = 0.5 * Cd * rho * A * v^2) (Section 1.1.2)
            Vector3 F_gravity = Vector3.down * 9.81f;
            // Vector3 F_drag = -0.5f * 0.47f * 1.2f * (Mathf.PI * 0.01f * 0.01f) * p.velocity.magnitude * p.velocity;
            Vector3 F_drag = -0.5f * 0.47f * 1.2f * particleArea
                                * p.velocity.magnitude * p.velocity;

            // اللزوجة الحرارية (Section 2.7.1) — تخميد سرعة الجسيم
            Vector3 F_viscous = -mu * p.velocity;

            Vector3 accel = F_gravity + F_wind
                          + (F_drag + F_viscous) / (paintDensity0 * 0.001f);
            // Vector3 totalAcceleration = F_gravity + (F_drag / (paintDensity0 * 0.001f));
            // p.velocity += totalAcceleration * dt;
            // p.position += p.velocity * dt;

            // particles[i] = p;

            p.velocity += accel * dt;
            p.position += p.velocity * dt;
            particles[i] = p;

            // إرسال البيانات لنظام الرسم عند اصطدام الجسيم بسطح اللوحة (Y = 0 فرضا) بدون كولايدرز
            // if (p.position.y <= 0f)
            // {
            //     PaintSurfaceCanvas.Instance.PaintAt(p.position, p.velocity, p.color);
            //     particles.RemoveAt(i); // إزالة الجسيم بعد الاصطدام والامتصاص
            // }

            // ── الاصطدام: AABB broad-phase ثم المستوى الدقيق (Section 2.5.2) ──
            if (PaintSurfaceCanvas.Instance != null &&
                PaintSurfaceCanvas.Instance.CheckCollision(p.position))
            {
                PaintSurfaceCanvas.Instance.PaintAt(p.position, p.velocity, p.color);
                particles.RemoveAt(i);
            }
        }
    }
}
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
    public SwingingCoupledSpringPendulum pendulum;

    [Header("Fluid Properties (Table 2.4.2)")]
    public float orificeDiameter = 0.05f;
    public float h_paint = 0.3f;
    public float Cd = 0.6f;
    public float paintDensity0 = 1200f;
    public float viscosity = 0.1f;
    public float h_kernel = 0.2f;

    [Header("Environment (Section 2.7)")]
    [Tooltip("درجة الحرارة بالسيليزيوس")]
    public float temperature = 25f;
    [Tooltip("الرطوبة النسبية (0–1)")]
    [Range(0f, 1f)] public float humidity = 0.5f;
    [Tooltip("سرعة الرياح (m/s)")]
    public float windSpeed = 0f;
    public Vector3 windDirection = Vector3.right;
    public float windCoeff = 0.5f;

    [Header("Color")]
    public Color currentPaintColor = Color.red;

    private List<SPHParticle> particles = new List<SPHParticle>();
    public float initialVolume = 0.01f;
    private float currentVolume;
    private float initialPaintHeight;
    private float poly6Norm;

    private const float T_REF = 25f;
    private const float K_TEMP = 0.03f;
    private const float BETA_H = 0.5f;

    public float EffectiveViscosity => viscosity * Mathf.Exp(K_TEMP * (T_REF - temperature));
    public float HumiditySpreadFactor => 1f + BETA_H * humidity;
    public int ActiveParticleCount => particles.Count;
    public float CurrentFlowRate { get; private set; }

    void Awake() { Instance = this; }

    void Start()
    {
        currentVolume = initialVolume;
        initialPaintHeight = h_paint;
        poly6Norm = 315f / (64f * Mathf.PI * Mathf.Pow(h_kernel, 9f));

        // ── FIX: بحث تلقائي عن البندول إذا لم يُعيَّن ──
        if (pendulum == null)
            pendulum = GetComponent<SwingingCoupledSpringPendulum>();
        if (pendulum == null)
            pendulum = FindObjectOfType<SwingingCoupledSpringPendulum>();

        if (pendulum == null)
            Debug.LogError("[FluidSPHSystem] لم يُعثر على SwingingCoupledSpringPendulum! تأكد من تعيينه في Inspector.");
    }

    public void ChangePaintColor(Color c) => currentPaintColor = c;

    void FixedUpdate()
    {
        // ── FIX: حماية من NullReference ──
        if (pendulum == null) { CurrentFlowRate = 0f; return; }
        if (currentVolume <= 0f) { CurrentFlowRate = 0f; return; }

        float dt = Time.fixedDeltaTime;
        float area = Mathf.PI * Mathf.Pow(orificeDiameter * 0.5f, 2f);

        // ── FIX: قيمة دنيا أعلى لـ geff لضمان تدفق مرئي ──
        float geff = Mathf.Max(1f, pendulum.EffectiveGravity);

        float safeH = Mathf.Max(0f, h_paint);
        float v_out = Mathf.Sqrt(2f * geff * safeH);
        float Q = Cd * area * v_out;
        CurrentFlowRate = Q;

        float volumeToEmit = Mathf.Min(Q * dt, currentVolume);
        currentVolume -= volumeToEmit;
        pendulum.UpdateBucketMass(volumeToEmit * paintDensity0);

        h_paint = Mathf.Max(0f, (currentVolume / initialVolume) * initialPaintHeight);

        // ── FIX: معدل توليد أكثر استقراراً ──
        int spawnCount = Mathf.Max(0, Mathf.RoundToInt(Q * 10000f * dt));
        // تحديد سقف للجسيمات لمنع تدهور الأداء
        spawnCount = Mathf.Min(spawnCount, 20);

        for (int i = 0; i < spawnCount; i++)
        {
            SPHParticle p = new SPHParticle();
            p.position = pendulum.transform.position;
            // ── FIX: إضافة تشتت صغير لتجنب تراكب الجسيمات ──
            p.position += new Vector3(
                Random.Range(-0.02f, 0.02f),
                0f,
                Random.Range(-0.02f, 0.02f));
            p.velocity = pendulum.BucketVelocity + Vector3.down * v_out;
            p.color = currentPaintColor;
            p.density = paintDensity0;
            p.pressure = 0f;
            particles.Add(p);
        }

        UpdateSPH();
    }

    void UpdateSPH()
    {
        float dt = Time.fixedDeltaTime;
        float mu = EffectiveViscosity;
        Vector3 F_wind = windDirection.normalized * windCoeff * windSpeed * windSpeed;
        float particleArea = Mathf.PI * 0.01f * 0.01f;

        // مرور الكثافة والضغط
        for (int i = 0; i < particles.Count; i++)
        {
            float densitySum = 0f;
            for (int j = 0; j < particles.Count; j++)
            {
                float dist = Vector3.Distance(particles[i].position, particles[j].position);
                if (dist < h_kernel)
                {
                    float q = h_kernel * h_kernel - dist * dist;
                    densitySum += paintDensity0 * poly6Norm * q * q * q;
                }
            }
            SPHParticle p = particles[i];
            p.density = Mathf.Max(paintDensity0, densitySum);
            p.pressure = 2000f * (p.density - paintDensity0);
            particles[i] = p;
        }

        // مرور القوى + الاصطدام
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            SPHParticle p = particles[i];

            Vector3 F_gravity = Vector3.down * 9.81f;
            Vector3 F_drag = -0.5f * 0.47f * 1.2f * particleArea
                                * p.velocity.magnitude * p.velocity;
            Vector3 F_viscous = -mu * p.velocity;

            Vector3 accel = F_gravity + F_wind
                          + (F_drag + F_viscous) / (paintDensity0 * 0.001f);

            p.velocity += accel * dt;
            p.position += p.velocity * dt;
            particles[i] = p;

            // ── FIX: حذف الجسيمات التي تسقط كثيراً لمنع التراكم اللانهائي ──
            if (p.position.y < -50f)
            {
                particles.RemoveAt(i);
                continue;
            }

            if (PaintSurfaceCanvas.Instance != null &&
                PaintSurfaceCanvas.Instance.CheckCollision(p.position))
            {
                PaintSurfaceCanvas.Instance.PaintAt(p.position, p.velocity, p.color);
                particles.RemoveAt(i);
            }
        }
    }
}
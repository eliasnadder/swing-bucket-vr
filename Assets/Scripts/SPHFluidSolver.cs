using System.Collections.Generic;
using UnityEngine;

public class SPHFluidSolver : MonoBehaviour
{
    // ── Singleton ── (يُستخدم من BucketBuilder وLiveDataOverlay)
    public static SPHFluidSolver Instance { get; private set; }

    [Header("SPH Parameters")]
    public float smoothingRadius = 8f;
    public float particleMass = 0.02f;
    public float restDensity = 1000f;
    public float gasConstant = 2000f;
    public float viscosity = 0.25f;
    public Vector3 gravity = new Vector3(0f, -981f, 0f);
    public float damping = 0.02f;
    public float timeStep = 0.0166667f;
    public bool useFull3D = false;

    [Header("2.5D Depth Control")]
    public float depthPlane = 0f;
    public float depthDamping = 0.65f;

    // ── Paint State (Section 2.4 — Torricelli flow) ──
    [Header("Paint State")]
    [Tooltip("حجم الطلاء الابتدائي (L)")]
    public float initialVolume = 0.5f;
    [Tooltip("أقصى ارتفاع طلاء في السطل")]
    public float maxPaintHeight = 0.30f;
    [Tooltip("معامل التفريغ Cd")]
    public float Cd = 0.6f;
    [Tooltip("قطر فتحة السطل")]
    public float orificeDiameter = 2f;
    [Tooltip("كثافة الطلاء (kg/m³)")]
    public float paintDensity = 1200f;
    [Tooltip("لون الطلاء الحالي")]
    public Color currentPaintColor = Color.red;

    // ── Environment (Section 2.7) ──
    [Header("Environment")]
    [Tooltip("درجة الحرارة (°C) — تؤثر على اللزوجة الفعّالة")]
    public float temperature = 25f;
    [Tooltip("الرطوبة النسبية 0–1 — تؤثر على انتشار البقعة")]
    [Range(0f, 1f)] public float humidity = 0.5f;
    [Tooltip("سرعة الرياح (m/s)")]
    public float windSpeed = 0f;
    public Vector3 windDirection = Vector3.right;
    public float windCoeff = 0.5f;

    [Header("Debug")]
    public bool drawGizmos = false;

    [Header("Emission Tuning")]
    [Tooltip("عدد الجسيمات لكل وحدة حجم — كلما زاد كلما كان التدفق أكثف مرئياً")]
    public float particlesPerVolumeUnit = 50000f;
    [Tooltip("أقصى عدد جسيمات تُصدر في فريم واحد")]
    public int   maxSpawnPerFrame = 20;

    // ── Private state ──
    private float currentVolume;
    private float h_paint;                // ارتفاع الطلاء الحالي داخل السطل
    private float initialPaintHeight;
    private float emissionAccumulator;

    // ── Constants for temperature model ──
    private const float T_REF  = 25f;
    private const float K_TEMP = 0.03f;
    private const float BETA_H = 0.5f;

    // ── Public read-only properties for UI/BucketBuilder ──
    public float   CurrentVolume        => currentVolume;
    public float   CurrentFlowRate      { get; private set; }
    public float   PaintHeight          => h_paint;
    public float   EffectiveViscosity   => viscosity * Mathf.Exp(K_TEMP * (T_REF - temperature));
    public float   HumiditySpreadFactor => 1f + BETA_H * humidity;
    public int     ActiveParticleCount  => particles.Count;

    private readonly List<SPHParticle> particles = new List<SPHParticle>(1024);
    private readonly List<int> neighborIndices   = new List<int>(128);
    private SpatialHashGrid spatialHash;

    // ── Reference to pendulum (for Torricelli effective gravity) ──
    [Header("References")]
    public SwingingCoupledSpringPendulum pendulum;
    public BucketBuilder bucketBuilder;

    public IReadOnlyList<SPHParticle> Particles => particles;
    public int ParticleCount => particles.Count;

    // ─────────────────────────────────────────
    private void Awake()
    {
        Instance = this;
        spatialHash = new SpatialHashGrid(smoothingRadius, useFull3D);
    }

    private void Start()
    {
        currentVolume = initialVolume;
        h_paint = maxPaintHeight;
        initialPaintHeight = maxPaintHeight;

        // Auto-find if not wired in Inspector
        if (pendulum == null)     pendulum     = FindAnyObjectByType<SwingingCoupledSpringPendulum>();
        if (bucketBuilder == null) bucketBuilder = FindAnyObjectByType<BucketBuilder>();

        if (pendulum == null)
            Debug.LogWarning("[SPHFluidSolver] No SwingingCoupledSpringPendulum found — Torricelli flow disabled.");
    }

    private void OnValidate()
    {
        smoothingRadius    = Mathf.Max(0.001f, smoothingRadius);
        particleMass       = Mathf.Max(0.000001f, particleMass);
        restDensity        = Mathf.Max(0.0001f, restDensity);
        gasConstant        = Mathf.Max(0f, gasConstant);
        viscosity          = Mathf.Max(0f, viscosity);
        damping            = Mathf.Max(0f, damping);
        depthDamping       = Mathf.Clamp01(depthDamping);
        orificeDiameter    = Mathf.Max(0.001f, orificeDiameter);
        if (spatialHash != null)
            spatialHash = new SpatialHashGrid(smoothingRadius, useFull3D);
    }

    // ── Called by SimulationController every FixedUpdate ──
    public void StepSimulation(float dt)
    {
        if (dt <= 0f) return;

        EmitParticles(dt);

        if (particles.Count == 0) return;

        if (spatialHash == null)
            spatialHash = new SpatialHashGrid(smoothingRadius, useFull3D);

        spatialHash.Rebuild(particles);
        ComputeDensities();
        ComputeForces();
        Integrate(dt);
    }

    // ── Torricelli emission (Section 2.4) ──
    private void EmitParticles(float dt)
    {
        if (currentVolume <= 0f) { CurrentFlowRate = 0f; return; }
        if (pendulum == null)    { CurrentFlowRate = 0f; return; }

        float geff  = Mathf.Max(1f, pendulum.EffectiveGravity);
        float area  = Mathf.PI * Mathf.Pow(orificeDiameter * 0.5f, 2f);
        float safeH = Mathf.Max(0f, h_paint);
        float v_out = Mathf.Sqrt(2f * geff * safeH);
        float Q     = Cd * area * v_out;
        CurrentFlowRate = Q;

        float volumeToEmit = Mathf.Min(Q * dt, currentVolume);
        currentVolume -= volumeToEmit;
        pendulum.UpdateBucketMass(volumeToEmit * paintDensity);
        h_paint = Mathf.Max(0f, (currentVolume / Mathf.Max(0.0001f, initialVolume)) * initialPaintHeight);

        // عدد الجسيمات المطلوب إصدارها — يتناسب مع Q (المتحكم به بـ orificeDiameter)
        emissionAccumulator += Q * particlesPerVolumeUnit * dt;
        int spawnCount = Mathf.Min(Mathf.FloorToInt(emissionAccumulator), maxSpawnPerFrame);
        emissionAccumulator -= spawnCount;

        Vector3 spawnBase = bucketBuilder != null
            ? bucketBuilder.GetPaintSpawnPosition()
            : (pendulum != null ? pendulum.transform.position : transform.position);

        for (int i = 0; i < spawnCount; i++)
        {
            Vector3 spawnPos = spawnBase + new Vector3(
                Random.Range(-0.02f, 0.02f), 0f,
                Random.Range(-0.02f, 0.02f));

            Vector3 bucketVel = pendulum != null ? pendulum.BucketVelocity : Vector3.zero;
            Vector3 windForce = windDirection.normalized * windCoeff * windSpeed;

            AddParticle(spawnPos, bucketVel + Vector3.down * v_out + windForce * dt, currentPaintColor);
        }
    }

    public void ChangePaintColor(Color c) => currentPaintColor = c;

    // ─────────────────────────────────────────
    public void ClearParticles() => particles.Clear();

    public void AddParticle(Vector3 position, Vector3 velocity, Color color)
    {
        SPHParticle particle = new SPHParticle
        {
            position = position,
            velocity = velocity,
            force    = Vector3.zero,
            density  = restDensity,
            pressure = 0f,
            color    = color,
            alive    = true
        };

        if (!useFull3D)
            particle.position.z = depthPlane;

        particles.Add(particle);
    }

    public SPHParticle GetParticle(int index)           => particles[index];
    public void        SetParticle(int index, SPHParticle p) => particles[index] = p;
    public void        RemoveParticleAt(int index)       => particles.RemoveAt(index);

    // ─────────────────────────────────────────
    public void ComputeDensities()
    {
        for (int i = 0; i < particles.Count; i++)
        {
            SPHParticle particle = particles[i];
            float density = 0f;

            spatialHash.GetNeighbors(particle.position, neighborIndices);
            for (int n = 0; n < neighborIndices.Count; n++)
            {
                float dist = Vector3.Distance(particle.position, particles[neighborIndices[n]].position);
                density += particleMass * SPHKernel.Poly6(dist, smoothingRadius);
            }

            particle.density  = Mathf.Max(restDensity * 0.1f, density);
            particle.pressure = gasConstant * (particle.density - restDensity);
            particles[i] = particle;
        }
    }

    public void ComputeForces()
    {
        Vector3 windForce = windDirection.normalized * windCoeff * windSpeed * windSpeed;
        float   mu        = EffectiveViscosity;

        for (int i = 0; i < particles.Count; i++)
        {
            SPHParticle particle = particles[i];
            Vector3 force = Vector3.zero;

            spatialHash.GetNeighbors(particle.position, neighborIndices);
            for (int n = 0; n < neighborIndices.Count; n++)
            {
                int j = neighborIndices[n];
                if (j == i) continue;

                SPHParticle nb  = particles[j];
                Vector3     r   = particle.position - nb.position;
                float       dist = r.magnitude;
                if (dist <= 0f || dist >= smoothingRadius) continue;

                float nbDensity   = Mathf.Max(0.0001f, nb.density);
                float pressureTerm = (particle.pressure + nb.pressure) / (2f * nbDensity);
                force += -particleMass * pressureTerm * SPHKernel.SpikyGradient(r, smoothingRadius);

                float viscTerm = SPHKernel.ViscosityLaplacian(dist, smoothingRadius);
                force += mu * particleMass * (nb.velocity - particle.velocity) / nbDensity * viscTerm;
            }

            force += particle.density * gravity;
            force += -damping * particle.velocity * particle.density;
            force += windForce * particle.density;

            particle.force = force;
            particles[i] = particle;
        }
    }

    public void Integrate() => Integrate(timeStep);

    public void Integrate(float dt)
    {
        for (int i = 0; i < particles.Count; i++)
        {
            SPHParticle particle = particles[i];
            float density = Mathf.Max(restDensity * 0.1f, particle.density);

            particle.velocity += (particle.force / density) * dt;
            particle.position += particle.velocity * dt;

            if (!useFull3D)
            {
                particle.position.z = Mathf.Lerp(particle.position.z, depthPlane, 0.35f);
                particle.velocity.z *= depthDamping;
            }

            // حذف الجسيمات التي سقطت بعيداً جداً
            if (particle.position.y < -100f)
            {
                particles.RemoveAt(i);
                i--;
                continue;
            }

            particles[i] = particle;
        }
    }
}

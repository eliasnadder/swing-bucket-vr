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

    // ── Bernoulli extension (Section 2.4.4.2) ──
    //   Q = Cd · A · sqrt( 2·g_eff·h_paint  +  ½·|v_tang|² )
    // When ON the bucket's horizontal swing adds a dynamic pressure head, so the
    // drip rate spikes as the user slings the bucket sideways. OFF returns to the
    // pure-Torricelli sqrt(2·g_eff·h) form.
    [Header("Bernoulli Torricelli Extension (Section 2.4.4.2)")]
    [Tooltip("يضيف رأس حركي ½·|v|² من حركة السطل الأفقية إلى سرعة التدفق")]
    public bool useBernoulliApproximation = true;

    // ── Surface tension / cohesion (Section 2.4.2.3) ──
    //   F_surface_i = surfaceTensionCoeff · CohesionForceGradient(p_i, neighbour)
    // where CohesionForceGradient = Poly6'(|r|, h) · r̂  (Poly6's analytical 3D
    // gradient) and is implemented inline in ComputeForces. Default coefficient
    // is zero so existing scenes are unchanged; raise it to make nearby droplets
    // cling to each other.
    [Header("Surface Tension / Cohesion (Section 2.4.2.3)")]
    [Range(0f, 100f)]
    [Tooltip("معامل التماسك — قوة الجذب بين الجسيمات القريبة (ملاحظة: التوازن الافتراضي 0 لا يُغيّر السلوك)")]
    public float surfaceTensionCoeff = 0f;
    [Tooltip("مسافة قص (cm) — لا توجد قوة تماسك عند مسافاتٍ أقل من هذه القيمة لتجنّب الانفرادية عند التماس القريب جداً")]
    public float surfaceTensionMinDist = 0f;

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

    // ── Coalescence (Section 2.5.1) ──
    //   When ON, particles within `coalescenceRadius` whose relative speed is below
    //   `maxCoalesceRelSpeed` are merged into one — drop a denser, longer-lived
    //   droplet cluster. Default OFF preserves existing scenes.
    [Header("Coalescence (Section 2.5.1)")]
    [Tooltip("دمج الجسيمات القريبة ذات السرعات المتشابهة (يقاف افتراضي لتجنّب تغيير المشهد)")]
    public bool enableCoalescence = false;
    [Tooltip("نصف قطر الدمج (cm)")]
    public float coalescenceRadius = 1f;
    [Tooltip("أقصى فرق سرعة للسماح بالدمج (cm/s)")]
    public float maxCoalesceRelSpeed = 5f;

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

        // ── Coalescence pass (Section 2.5.1) ──
        // Run AFTER Integrate so neighbour queries see the post-step positions.
        // Spatial hash is rebuilt with the latest particle positions.
        if (enableCoalescence && particles.Count >= 2)
        {
            spatialHash.Rebuild(particles);
            CoalesceParticles();
        }
    }

    // ── Torricelli emission with optional Bernoulli extension (Section 2.4) ──
    private void EmitParticles(float dt)
    {
        if (currentVolume <= 0f) { CurrentFlowRate = 0f; return; }
        if (pendulum == null)    { CurrentFlowRate = 0f; return; }

        float geff  = Mathf.Max(1f, pendulum.EffectiveGravity);
        float area  = Mathf.PI * Mathf.Pow(orificeDiameter * 0.5f, 2f);
        float safeH = Mathf.Max(0f, h_paint);

        // ── Dynamic-head (swing) velocity term — Section 2.4.4.2 ──
        // |v_tang| is taken as the bucket's full planar speed (component perpendicular
        // to gravity). The radial (along-rope) component contributes only to
        // hydrostatic pressure which is already captured by g_eff·h, so we project
        // it out before taking the magnitude.
        Vector3 bucketVel = pendulum.BucketVelocity;
        Vector3 tangentialVel = Vector3.ProjectOnPlane(bucketVel, Vector3.up);
        float vtMag = tangentialVel.magnitude;

        float v_out;
        if (useBernoulliApproximation)
        {
            // Q = Cd · A · sqrt(2·g_eff·h + ½·|v_tang|²)
            // The ½·v² term is the kinetic-energy-per-unit-mass that the swinging
            // bucket transmits to the fluid — physically, the fluid "ahead" of the
            // bucket's swing direction gets pushed out faster.
            v_out = Mathf.Sqrt(Mathf.Max(0f, 2f * geff * safeH + 0.5f * vtMag * vtMag));
        }
        else
        {
            // Pure Torricelli — original section 2.4 form.
            v_out = Mathf.Sqrt(2f * geff * safeH);
        }

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

        // Pre-compute Poly6 gradient normalisation constants for surface tension.
        // Poly6(r,h) = (315 / 64π h⁹) · (h² − r²)³
        // dPoly6/dr  = −6·(315/(64π h⁹)) · r · (h² − r²)²
        // ∇_i Poly6|{r = p_i − p_j} = dPoly6/dr · r̂  =  −6·(315/(64π h⁹)) · (h²−r²)² · rVec
        // The leading coefficient is negative; thus ∇_i Poly6 points OPPOSITE to rVec,
        // i.e. toward particle j. That is the attractive direction. The cohesion
        // coefficient `surfaceTensionCoeff` is positive in the slider, and we apply
        // it directly — F_surface_i = surfaceTensionCoeff · ∇_i Poly6 — to make the
        // force attractive. (Equivalent up to one sign-flip to Müller-style
        // "−σ · Poly6Kernel_gradient(r,h)" since the analytical gradient already
        // points toward the neighbour.)
        bool   wantCohesion = surfaceTensionCoeff > 0f;
        float  invH9        = wantCohesion ? 1f / Mathf.Pow(smoothingRadius, 9f) : 0f;
        // (315 − 64·π·k_grad)/64·π  where k_grad coalesces two constants we'll absorb
        // implicitly; we keep −6·315/(64π) = −945/32 as the closed-form coefficient.
        const float POLY6GRAD_COEFF = -945f / 32f;
        float poly6Base = wantCohesion ? POLY6GRAD_COEFF * invH9 : 0f;
        float minDistSq = surfaceTensionMinDist * surfaceTensionMinDist;

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

                // ── Surface tension / cohesion (Section 2.4.2.3) ──
                // Inlined Poly6 gradient to avoid touching SPHKernel.cs.
                // Skip when the pair is closer than surfaceTensionMinDist
                // (a configurable "no-tension at very close range" cutoff
                // that keeps the force finite when two droplets touch).
                if (wantCohesion && dist > surfaceTensionMinDist && dist * dist > minDistSq)
                {
                    float ratio2   = smoothingRadius * smoothingRadius - dist * dist;
                    // ∇_i Poly6 = poly6Base · (h²−r²)² · rVec
                    Vector3 gradPoly6 = poly6Base * (ratio2 * ratio2) * r;
                    force += surfaceTensionCoeff * gradPoly6 * particleMass;
                }
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

    // ─────────────────────────────────────────
    // Coalescence pass (Section 2.5.1)
    //
    // Iteration order (reverse):
    //   • Outer i descends from particles.Count−1 → 0, so when RemoveAt(i)
    //     removes particle i the remaining particles at lower indices are NOT
    //     re-indexed under our cursor.
    //   • Inner loop scans spatial-hash neighbours of particle i, picks the
    //     first valid candidate j with j < i (lower index survives; higher
    //     index is removed). This deduplicates unordered (i,j) and (j,i) pairs.
    //
    // Merge rule (chosen & documented — equal-mass case, easily reversible):
    //   • Position: p_merged = (p_i + p_j) · 0.5               (arithmetic mid-point)
    //     — equivalent to mass-weighted average when m_i == m_j.
    //   • Velocity: v_merged = (v_i + v_j) · 0.5               (arithmetic mid-point)
    //     — equivalent to momentum-weighted average when m_i == m_j (since
    //       (m·v_i + m·v_j)/(2m) = (v_i + v_j)/2).
    //   • Color:    c_merged_rgba = ((c_i.r + c_j.r)/2, ..., (c_i.a + c_j.a)/2)
    //   • Density / pressure: arithmetic mean (recomputed next frame anyway).
    //   • force: zeroed.
    //   • alive: true.
    //
    // The reverse-iteration + lower-index survivor rule guarantees no particle
    // is processed twice and no pair is merged twice.
    private void CoalesceParticles()
    {
        if (particles.Count < 2) return;

        float r2 = coalescenceRadius * coalescenceRadius;

        // Iterate from end to start so RemoveAt(idx) doesn't disturb unprocessed
        // (lower-indexed) entries.
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            SPHParticle pa = particles[i];
            if (!pa.alive) continue;          // belt-and-braces; alive is currently always true.

            spatialHash.GetNeighbors(pa.position, neighborIndices);

            int mergeWith = -1;
            for (int k = 0; k < neighborIndices.Count; k++)
            {
                int j = neighborIndices[k];
                if (j == i) continue;          // skip self
                if (j >= i) continue;          // dedup: only merge with lower-index partners

                SPHParticle candidate = particles[j];
                Vector3 delta = pa.position - candidate.position;
                float distSq = delta.sqrMagnitude;
                if (distSq > r2) continue;

                float relSpeed = (pa.velocity - candidate.velocity).magnitude;
                if (relSpeed > maxCoalesceRelSpeed) continue;

                mergeWith = j;
                break;                         // first valid candidate wins (one merge per outer i)
            }

            if (mergeWith < 0) continue;

            SPHParticle pb = particles[mergeWith];
            SPHParticle merged = new SPHParticle
            {
                position = (pa.position + pb.position) * 0.5f,
                velocity = (pa.velocity + pb.velocity) * 0.5f,
                force    = Vector3.zero,
                density  = (pa.density + pb.density) * 0.5f,
                pressure = (pa.pressure + pb.pressure) * 0.5f,
                color    = new Color(
                    (pa.color.r + pb.color.r) * 0.5f,
                    (pa.color.g + pb.color.g) * 0.5f,
                    (pa.color.b + pb.color.b) * 0.5f,
                    (pa.color.a + pb.color.a) * 0.5f),
                alive    = true
            };

            particles[mergeWith] = merged;
            particles.RemoveAt(i);
        }
    }
}

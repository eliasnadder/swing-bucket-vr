using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
[ExecuteAlways]
public class SwingingCoupledSpringPendulum : MonoBehaviour
{
    [Header("Visual Components")]
    public Transform pivotPoint;
    private LineRenderer ropeRenderer;

    [Header("Rope Visuals")]
    [Range(4, 40)] public int ropeSegments = 20;
    [Range(0f, 0.15f)] public float sagFactor = 0.04f;
    public float ropeWidthTop = 0.06f;
    public float ropeWidthBottom = 0.02f;

    [Header("Pendulum Parameters")]
    public float L0 = 850f;
    public float m0 = 2f;
    public float g = 981f;
    public float b = 0.1f;

    [Header("Spring-Damper Rope (Three.js style)")]
    public float k_rope = 500f;
    public float c_rope = 3f;

    [Header("Wind Parameters")]
    public float windSpeed = 0f;
    public Vector3 windDirection = Vector3.right;
    public float windCoeff = 0.5f;

    [Header("Initial State")]
    public float initialTheta = 45f;
    public float initialOmega = 0f;
    public float initialPhi = 0f;

    [Header("Editor Helper")]
    [Tooltip("في وضع التحرير فقط (Edit Mode): يحسب θ₀ وL0 وφ₀ تلقائياً من موضع السطل الحالي نسبة لنقطة التعليق الثابتة، بدلاً من تحريك نقطة التعليق نفسها")]
    public bool autoSyncFromScenePosition = true;

    // حالة النظام - four angular variables only, radial handled by constraint
    private float theta, omega_theta, phi, omega_phi;
    private float currentMass;
    private float currentLength;
    private Vector3 previousPosition;
    private bool firstFrame = true;

    // خصائص عامة
    public Vector3 BucketVelocity { get; private set; }
    public float EffectiveGravity { get; private set; }
    public float AngularAccelerationTheta { get; private set; }
    public Vector3 DailVelocity => BucketVelocity;
    public float CurrentLength => currentLength;
    public float CurrentTheta => theta;

    private struct State { public float t, o_t, p, o_p; }
    private struct Derivs { public float d_t, d_ot, d_p, d_op; }

    // ─────────────────────────────────────────
    void Start()
    {
        if (!Application.isPlaying) return;

        currentMass = m0;
        theta = initialTheta * Mathf.Deg2Rad;
        omega_theta = initialOmega;
        phi = initialPhi * Mathf.Deg2Rad;
        omega_phi = 0f;

        if (pivotPoint == null)
        {
            var goPivot = new GameObject("Auto_Pivot");
            Vector3 offset = SphericalToCartesian(L0, theta, phi);
            goPivot.transform.position = transform.position - offset;
            pivotPoint = goPivot.transform;
        }

        transform.position = pivotPoint.position + SphericalToCartesian(L0, theta, phi);
        previousPosition = transform.position;
        BucketVelocity = Vector3.zero;
        EffectiveGravity = g;
        currentLength = L0;

        SetupLineRenderer();
    }

    // إحداثيات كروية → ديكارتية (y يشير للأسفل)
    static Vector3 SphericalToCartesian(float L, float t, float p)
    {
        return new Vector3(
             L * Mathf.Sin(t) * Mathf.Cos(p),
            -L * Mathf.Cos(t),
             L * Mathf.Sin(t) * Mathf.Sin(p));
    }

    static Vector3 ComputeAngularVelocity(float L, float t, float p, float ot, float op)
    {
        float sinT = Mathf.Sin(t);
        float cosT = Mathf.Cos(t);
        float sinP = Mathf.Sin(p);
        float cosP = Mathf.Cos(p);
        return new Vector3(
            L * (cosT * cosP * ot - sinT * sinP * op),
            L * sinT * ot,
            L * (cosT * sinP * ot + sinT * cosP * op)
        );
    }

    void Update()
    {
        if (Application.isPlaying)
            return;

        if (!autoSyncFromScenePosition || pivotPoint == null)
            return;

        SyncAnglesFromScenePosition();
    }

    void SyncAnglesFromScenePosition()
    {
        Vector3 v = transform.position - pivotPoint.position;
        float len = v.magnitude;
        if (len < 0.01f) return;

        float t = Mathf.Acos(Mathf.Clamp(-v.y / len, -1f, 1f));
        float p = Mathf.Atan2(v.z, v.x);

        L0 = len;
        initialTheta = t * Mathf.Rad2Deg;
        initialPhi = p * Mathf.Rad2Deg;
    }

    void SetupLineRenderer()
    {
        ropeRenderer = GetComponent<LineRenderer>();
        ropeRenderer.positionCount = ropeSegments + 1;
        ropeRenderer.startWidth = ropeWidthTop;
        ropeRenderer.endWidth = ropeWidthBottom;
        ropeRenderer.useWorldSpace = true;
        ropeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ropeRenderer.receiveShadows = false;

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(new Color(0.7f, 0.7f, 0.7f), 0f),
                new GradientColorKey(new Color(0.25f, 0.25f, 0.25f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
        );
        ropeRenderer.colorGradient = gradient;

        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Standard");

        var mat = new Material(sh);
        mat.color = new Color(0.55f, 0.55f, 0.55f);
        ropeRenderer.material = mat;
    }

    Vector3 GetWindForce() =>
        windDirection.normalized * windCoeff * windSpeed * windSpeed;

    // ─── Angular derivatives (no radial ODE — rope is constraint-based, three.js style) ───
    Derivs CalculateAngularDerivatives(State s)
    {
        Derivs d;
        d.d_t = s.o_t;
        d.d_p = s.o_p;

        float safeSinT = Mathf.Max(Mathf.Abs(Mathf.Sin(s.t)), 0.001f);

        Vector3 Fw = GetWindForce();
        Vector3 theta_hat = new Vector3(
            Mathf.Cos(s.t) * Mathf.Cos(s.p),
            Mathf.Sin(s.t),
            Mathf.Cos(s.t) * Mathf.Sin(s.p));
        Vector3 phi_hat = new Vector3(-Mathf.Sin(s.p), 0f, Mathf.Cos(s.p));

        float windAlpha_theta = Vector3.Dot(Fw, theta_hat) / (currentMass * L0);
        float windAlpha_phi = Vector3.Dot(Fw, phi_hat) / (currentMass * L0 * safeSinT);

        d.d_ot = Mathf.Sin(s.t) * Mathf.Cos(s.t) * s.o_p * s.o_p
               - (g / L0) * Mathf.Sin(s.t)
               - (b / currentMass) * s.o_t
               + windAlpha_theta;

        d.d_op = -2f * (Mathf.Cos(s.t) / safeSinT) * s.o_t * s.o_p
               - (b / currentMass) * s.o_p
               + windAlpha_phi;

        const float CLAMP = 1000f;
        d.d_ot = Mathf.Clamp(d.d_ot, -CLAMP, CLAMP);
        d.d_op = Mathf.Clamp(d.d_op, -CLAMP, CLAMP);

        return d;
    }

    static State StepAngular(State s, Derivs d, float h) => new State
    {
        t = s.t + h * d.d_t,
        o_t = s.o_t + h * d.d_ot,
        p = s.p + h * d.d_p,
        o_p = s.o_p + h * d.d_op
    };

    void FixedUpdate()
    {
        if (!Application.isPlaying || pivotPoint == null) return;

        // Match index.html: SUBSTEPS=10 per frame so the large angular RK4
        // integrates with the same effective step size as Three.js (≈0.0017s).
        const int SUBSTEPS = 10;
        float subDt = Time.fixedDeltaTime / SUBSTEPS;

        for (int step = 0; step < SUBSTEPS; step++)
        {
            State s = new State { t = theta, o_t = omega_theta, p = phi, o_p = omega_phi };

            Derivs d1 = CalculateAngularDerivatives(s);
            Derivs d2 = CalculateAngularDerivatives(StepAngular(s, d1, subDt * 0.5f));
            Derivs d3 = CalculateAngularDerivatives(StepAngular(s, d2, subDt * 0.5f));
            Derivs d4 = CalculateAngularDerivatives(StepAngular(s, d3, subDt));

            theta += (subDt / 6f) * (d1.d_t + 2 * d2.d_t + 2 * d3.d_t + d4.d_t);
            omega_theta += (subDt / 6f) * (d1.d_ot + 2 * d2.d_ot + 2 * d3.d_ot + d4.d_ot);
            phi += (subDt / 6f) * (d1.d_p + 2 * d2.d_p + 2 * d3.d_p + d4.d_p);
            omega_phi += (subDt / 6f) * (d1.d_op + 2 * d2.d_op + 2 * d3.d_op + d4.d_op);

            theta = Mathf.Clamp(theta, -Mathf.PI * 0.95f, Mathf.PI * 0.95f);

            // ── Three.js-style rigid rope constraint ──
            // Full position + velocity projection every substep (matches rigid mode).
            Vector3 idealPos = pivotPoint.position + SphericalToCartesian(L0, theta, phi);
            Vector3 velFromAngular = ComputeAngularVelocity(L0, theta, phi, omega_theta, omega_phi);

            Vector3 dirVec = idealPos - pivotPoint.position;
            float dist = dirVec.magnitude;
            if (dist > 0.001f) dirVec.Normalize();

            idealPos = pivotPoint.position + dirVec * L0;

            float vRadial = Vector3.Dot(velFromAngular, dirVec);
            velFromAngular -= dirVec * vRadial;

            currentLength = L0;

            if (!float.IsNaN(idealPos.x) && !float.IsNaN(idealPos.y) && !float.IsNaN(idealPos.z))
                transform.position = idealPos;
        }

        // Angular acceleration for effective gravity (uses final angular velocities)
        Derivs lastD = CalculateAngularDerivatives(new State { t = theta, o_t = omega_theta, p = phi, o_p = omega_phi });
        AngularAccelerationTheta = lastD.d_ot;

        // Velocity computed from total displacement over the full FixedUpdate frame
        BucketVelocity = firstFrame
            ? Vector3.zero
            : (transform.position - previousPosition) / Time.fixedDeltaTime;

        previousPosition = transform.position;
        firstFrame = false;

        float omega_tot = Mathf.Sqrt(omega_theta * omega_theta + omega_phi * omega_phi);
        EffectiveGravity = Mathf.Max(g * 0.5f,
            Mathf.Sqrt(
                Mathf.Pow(g + omega_tot * omega_tot * currentLength * Mathf.Cos(theta), 2) +
                Mathf.Pow(AngularAccelerationTheta * currentLength * Mathf.Sin(theta), 2)));

        UpdateRopeVisuals();
    }

    void UpdateRopeVisuals()
    {
        if (ropeRenderer == null || pivotPoint == null) return;

        ropeRenderer.positionCount = ropeSegments + 1;

        Vector3 start = pivotPoint.position;
        Vector3 end = transform.position;

        Vector3 ropeVec = end - start;
        Vector3 sagDir = Vector3.Cross(Vector3.Cross(ropeVec, Vector3.up), ropeVec);
        if (sagDir.sqrMagnitude > 0.0001f) sagDir.Normalize();
        else sagDir = Vector3.right;

        float extension = Mathf.Max(0f, currentLength - L0);
        float sag = sagFactor * currentLength * Mathf.Exp(-extension * 5f);

        float tensionRatio = Mathf.Clamp01(extension / (L0 * 0.2f));
        Color topColor = Color.Lerp(new Color(0.7f, 0.7f, 0.7f), new Color(0.8f, 0.3f, 0.3f), tensionRatio);
        Color bottomColor = Color.Lerp(new Color(0.25f, 0.25f, 0.25f), new Color(0.6f, 0.1f, 0.1f), tensionRatio);

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(topColor, 0f), new GradientColorKey(bottomColor, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
        );
        ropeRenderer.colorGradient = gradient;

        for (int i = 0; i <= ropeSegments; i++)
        {
            float t = i / (float)ropeSegments;
            Vector3 p = Vector3.Lerp(start, end, t);
            p += sagDir * (sag * Mathf.Sin(Mathf.PI * t));
            ropeRenderer.SetPosition(i, p);
        }
    }

    public void UpdateBucketMass(float lostMass)
    {
        if (float.IsNaN(lostMass) || float.IsInfinity(lostMass)) return;
        currentMass = Mathf.Max(0.5f, currentMass - lostMass);
    }

    public void ResetLength(float newL0)
    {
        L0 = Mathf.Max(0.5f, newL0);
        currentLength = L0;
    }
}
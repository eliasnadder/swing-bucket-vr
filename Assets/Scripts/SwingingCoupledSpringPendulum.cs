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
    public float L0 = 5f;
    public float m0 = 2f;
    public float g = 9.81f;
    public float b = 0.1f;

    [Header("Spring-Damper Rope")]
    public float k_rope = 50f;
    public float c_rope = 5f;

    [Header("Wind Parameters")]
    public float windSpeed = 0f;
    public Vector3 windDirection = Vector3.right;
    public float windCoeff = 0.5f;

    [Header("Initial State")]
    public float initialTheta = 30f;
    public float initialOmega = 0f;
    public float initialPhi = 0f;

    [Header("Editor Helper")]
    [Tooltip("في وضع التحرير فقط (Edit Mode): يحسب θ₀ وL0 وφ₀ تلقائياً من موضع السطل الحالي نسبة لنقطة التعليق الثابتة، بدلاً من تحريك نقطة التعليق نفسها")]
    public bool autoSyncFromScenePosition = true;

    // حالة النظام
    private float theta, omega_theta, phi, omega_phi;
    private float currentLength, ropeVelocity, currentMass;
    private Vector3 previousPosition;
    private bool firstFrame = true;

    // خصائص عامة
    public Vector3 BucketVelocity { get; private set; }
    public float EffectiveGravity { get; private set; }
    public float AngularAccelerationTheta { get; private set; }
    public Vector3 DailVelocity => BucketVelocity;
    public float CurrentLength => currentLength;
    public float CurrentTheta => theta;

    private struct State { public float t, o_t, p, o_p, L, dL; }
    private struct Derivs { public float d_t, d_ot, d_p, d_op, d_L, d_dL; }

    // ─────────────────────────────────────────
    void Start()
    {
        if (!Application.isPlaying) return; // أمان إضافي بسبب [ExecuteAlways]

        currentMass = m0;
        currentLength = L0;
        ropeVelocity = 0f;

        theta = initialTheta * Mathf.Deg2Rad;
        omega_theta = initialOmega;
        phi = initialPhi * Mathf.Deg2Rad;
        omega_phi = 0f;

        // ── نقطة التعليق ثابتة دائماً. إن لم تُعيَّن، أنشئ واحدة تلقائياً فوق السطل الحالي ──
        if (pivotPoint == null)
        {
            var goPivot = new GameObject("Auto_Pivot");
            Vector3 offset = SphericalToCartesian(L0, theta, phi);
            goPivot.transform.position = transform.position - offset;
            pivotPoint = goPivot.transform;
        }

        // ── السطل دائماً يُحسب من نقطة التعليق + الزاوية والطول ──
        // نقطة التعليق لا تتغيّر أبداً بسبب موضع السطل بعد الآن
        transform.position = pivotPoint.position + SphericalToCartesian(currentLength, theta, phi);

        previousPosition = transform.position;
        BucketVelocity = Vector3.zero;
        EffectiveGravity = g;

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

    // ── يعمل فقط في وضع التحرير: يقرأ موضع السطل الحالي ويحسب θ₀/L0/φ₀ المطابقة ──
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
        if (len < 0.01f) return; // تجنّب القسمة على صفر لو السطل فوق نقطة التعليق تماماً

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

    // ─── مشتقات RK4 ───
    Derivs CalculateDerivatives(State s)
    {
        Derivs d;
        d.d_t = s.o_t;
        d.d_p = s.o_p;
        d.d_L = s.dL;

        float safeL = Mathf.Max(s.L, 0.1f);
        float sinT = Mathf.Sin(s.t);
        float cosT = Mathf.Cos(s.t);
        float sinT_safe = Mathf.Max(Mathf.Abs(sinT), 0.001f);

        Vector3 Fw = GetWindForce();
        Vector3 theta_hat = new Vector3(cosT * Mathf.Cos(s.p), sinT, cosT * Mathf.Sin(s.p));
        Vector3 phi_hat = new Vector3(-Mathf.Sin(s.p), 0f, Mathf.Cos(s.p));
        Vector3 r_hat = new Vector3(sinT * Mathf.Cos(s.p), -cosT, sinT * Mathf.Sin(s.p));

        float Fw_theta = Vector3.Dot(Fw, theta_hat);
        float Fw_phi = Vector3.Dot(Fw, phi_hat);
        float Fw_r = Vector3.Dot(Fw, r_hat);

        d.d_ot = sinT * cosT * s.o_p * s.o_p
               - (g / safeL) * sinT
               - 2f * (s.dL / safeL) * s.o_t
               - (b / currentMass) * s.o_t
               + Fw_theta / (currentMass * safeL);

        d.d_op = -2f * (s.dL / safeL) * s.o_p
               - 2f * (cosT / sinT_safe) * s.o_t * s.o_p
               - (b / currentMass) * s.o_p
               + Fw_phi / (currentMass * safeL * sinT_safe);

        float F_cf = currentMass * safeL * (s.o_t * s.o_t + sinT * sinT * s.o_p * s.o_p);
        float F_grav_r = currentMass * g * cosT;
        float F_spring = -k_rope * (s.L - L0);
        float F_damp = -c_rope * s.dL;

        d.d_dL = (F_grav_r + F_cf + F_spring + F_damp + Fw_r) / currentMass;

        const float CLAMP = 1000f;
        d.d_ot = Mathf.Clamp(d.d_ot, -CLAMP, CLAMP);
        d.d_op = Mathf.Clamp(d.d_op, -CLAMP, CLAMP);
        d.d_dL = Mathf.Clamp(d.d_dL, -CLAMP, CLAMP);

        return d;
    }

    void FixedUpdate()
    {
        if (!Application.isPlaying || pivotPoint == null) return;

        float dt = Time.fixedDeltaTime;

        State s = new State
        { t = theta, o_t = omega_theta, p = phi, o_p = omega_phi, L = currentLength, dL = ropeVelocity };

        Derivs d1 = CalculateDerivatives(s);
        Derivs d2 = CalculateDerivatives(Step(s, d1, dt * 0.5f));
        Derivs d3 = CalculateDerivatives(Step(s, d2, dt * 0.5f));
        Derivs d4 = CalculateDerivatives(Step(s, d3, dt));

        theta += (dt / 6f) * (d1.d_t + 2 * d2.d_t + 2 * d3.d_t + d4.d_t);
        omega_theta += (dt / 6f) * (d1.d_ot + 2 * d2.d_ot + 2 * d3.d_ot + d4.d_ot);
        phi += (dt / 6f) * (d1.d_p + 2 * d2.d_p + 2 * d3.d_p + d4.d_p);
        omega_phi += (dt / 6f) * (d1.d_op + 2 * d2.d_op + 2 * d3.d_op + d4.d_op);
        currentLength += (dt / 6f) * (d1.d_L + 2 * d2.d_L + 2 * d3.d_L + d4.d_L);
        ropeVelocity += (dt / 6f) * (d1.d_dL + 2 * d2.d_dL + 2 * d3.d_dL + d4.d_dL);

        if (float.IsNaN(theta) || float.IsNaN(phi) || float.IsNaN(currentLength))
        {
            Debug.LogWarning("[Pendulum] NaN detected — resetting state");
            theta = initialTheta * Mathf.Deg2Rad;
            omega_theta = 0f;
            phi = 0f;
            omega_phi = 0f;
            currentLength = L0;
            ropeVelocity = 0f;
        }

        currentLength = Mathf.Clamp(currentLength, L0 * 0.5f, L0 * 2f);

        AngularAccelerationTheta =
            (d1.d_ot + 2 * d2.d_ot + 2 * d3.d_ot + d4.d_ot) / 6f;

        float omega_tot = Mathf.Sqrt(omega_theta * omega_theta + omega_phi * omega_phi);
        EffectiveGravity = Mathf.Max(g * 0.5f,
            Mathf.Sqrt(
                Mathf.Pow(g + omega_tot * omega_tot * currentLength * Mathf.Cos(theta), 2) +
                Mathf.Pow(AngularAccelerationTheta * currentLength * Mathf.Sin(theta), 2)));

        // نقطة التعليق ثابتة هنا — لا تُحسب من جديد أبداً
        Vector3 newPos = pivotPoint.position + SphericalToCartesian(currentLength, theta, phi);

        if (!float.IsNaN(newPos.x) && !float.IsNaN(newPos.y) && !float.IsNaN(newPos.z))
            transform.position = newPos;

        BucketVelocity = firstFrame
            ? Vector3.zero
            : (transform.position - previousPosition) / dt;

        previousPosition = transform.position;
        firstFrame = false;

        UpdateRopeVisuals();
    }

    static State Step(State s, Derivs d, float h) => new State
    {
        t = s.t + h * d.d_t,
        o_t = s.o_t + h * d.d_ot,
        p = s.p + h * d.d_p,
        o_p = s.o_p + h * d.d_op,
        L = Mathf.Max(s.L + h * d.d_L, 0.1f),
        dL = s.dL + h * d.d_dL
    };

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
        ropeVelocity = 0f;
    }
}
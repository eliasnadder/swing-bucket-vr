using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class SwingingCoupledSpringPendulum : MonoBehaviour
{
    [Header("Visual Components")]
    public Transform pivotPoint;
    private LineRenderer ropeRenderer;

    [Header("Pendulum Parameters (Table 3.1.1)")]
    public float L0 = 5f;
    public float m0 = 2f;
    public float g = 9.81f;
    public float b = 0.1f;

    [Header("Spring-Damper Rope (Section 2.3.3)")]
    public float k_rope = 500f;
    public float c_rope = 10f;

    [Header("Wind Parameters (Section 2.7.3)")]
    public float windSpeed = 0f;
    public Vector3 windDirection = Vector3.right;
    public float windCoeff = 0.5f;

    [Header("Initial State")]
    public float initialTheta = 45f;
    public float initialOmega = 0f;   // rad/s
    public float initialPhi = 0f;

    // حالة النظام
    private float theta, omega_theta, phi, omega_phi;
    private float currentLength, ropeVelocity, currentMass;

    // خصائص متاحة للأنظمة الأخرى
    public Vector3 BucketVelocity { get; private set; }
    public float EffectiveGravity { get; private set; }
    public float AngularAccelerationTheta { get; private set; }
    public Vector3 DailVelocity => BucketVelocity;

    // ── State struct ──
    private struct State
    {
        public float t, o_t, p, o_p, L, dL;
    }
    private struct Derivs
    {
        public float d_t, d_ot, d_p, d_op, d_L, d_dL;
    }

    void Start()
    {
        currentMass = m0;
        currentLength = L0;
        ropeVelocity = 0f;

        theta = initialTheta * Mathf.Deg2Rad;
        // initialOmega هو بالفعل rad/s — لا نضرب Deg2Rad مرة ثانية
        omega_theta = initialOmega;
        phi = initialPhi * Mathf.Deg2Rad;
        omega_phi = 0f;

        if (pivotPoint == null)
        {
            var go = new GameObject("Auto_Pivot");
            go.transform.position = transform.position + Vector3.up * L0;
            pivotPoint = go.transform;
        }

        SetupLineRenderer();
    }

    void SetupLineRenderer()
    {
        ropeRenderer = GetComponent<LineRenderer>();
        ropeRenderer.positionCount = 2;
        ropeRenderer.startWidth = 0.04f;
        ropeRenderer.endWidth = 0.04f;
        if (ropeRenderer.sharedMaterial == null)
        {
            ropeRenderer.material = new Material(Shader.Find("Sprites/Default"));
            ropeRenderer.startColor = Color.gray;
            ropeRenderer.endColor = Color.black;
        }
    }

    Vector3 GetWindForce() =>
        windDirection.normalized * windCoeff * windSpeed * windSpeed;

    Derivs CalculateDerivatives(State s)
    {
        Derivs d;
        d.d_t = s.o_t;
        d.d_p = s.o_p;
        d.d_L = s.dL;

        Vector3 Fw = GetWindForce();

        // متجهات الإحداثيات الكروية
        Vector3 r_hat = new Vector3(
             Mathf.Sin(s.t) * Mathf.Cos(s.p),
            -Mathf.Cos(s.t),
             Mathf.Sin(s.t) * Mathf.Sin(s.p));

        Vector3 theta_hat = new Vector3(
             Mathf.Cos(s.t) * Mathf.Cos(s.p),
             Mathf.Sin(s.t),                    // ← ملاحظة: +sin في الاتجاه الشعاعي الكروي
             Mathf.Cos(s.t) * Mathf.Sin(s.p));

        Vector3 phi_hat = new Vector3(-Mathf.Sin(s.p), 0f, Mathf.Cos(s.p));

        float cosT = Mathf.Cos(s.t);
        float sinT = Mathf.Sin(s.t);

        // حماية القسمة على sinT دون كسر الإشارة السالبة
        // نستخدم sinT المطلق فقط في المقامات، ليس في الحسابات الأخرى
        float sinT_safe = Mathf.Max(Mathf.Abs(sinT), 0.001f);

        float Fw_r = Vector3.Dot(Fw, r_hat);
        float Fw_theta = Vector3.Dot(Fw, theta_hat);
        float Fw_phi = Vector3.Dot(Fw, phi_hat);

        // معادلات الحركة المقترنة (Section 2.3.4)
        d.d_ot = sinT * cosT * s.o_p * s.o_p
                 - (g / s.L) * sinT
                 - 2f * (s.dL / s.L) * s.o_t
                 - (b / currentMass) * s.o_t
                 + (Fw_theta / (currentMass * s.L));

        d.d_op = -2f * (s.dL / s.L) * s.o_p
                 - 2f * (cosT / sinT_safe) * s.o_t * s.o_p
                 - (b / currentMass) * s.o_p
                 + (Fw_phi / (currentMass * s.L * sinT_safe));

        // Spring-Damper (Section 2.3.3)
        float F_centrifugal = currentMass * s.L
                            * (s.o_t * s.o_t + sinT * sinT * s.o_p * s.o_p);
        float F_gravity_rad = currentMass * g * cosT;
        float F_spring = -k_rope * (s.L - L0);
        float F_damper = -c_rope * s.dL;

        d.d_dL = (F_gravity_rad + F_centrifugal + F_spring + F_damper + Fw_r)
                 / currentMass;

        return d;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        State s = new State
        {
            t = theta,
            o_t = omega_theta,
            p = phi,
            o_p = omega_phi,
            L = currentLength,
            dL = ropeVelocity
        };

        // ── RK4 الكامل على كل المتغيرات ──
        Derivs d1 = CalculateDerivatives(s);
        Derivs d2 = CalculateDerivatives(Step(s, d1, dt * 0.5f));
        Derivs d3 = CalculateDerivatives(Step(s, d2, dt * 0.5f));
        Derivs d4 = CalculateDerivatives(Step(s, d3, dt));

        theta += (dt / 6f) * (d1.d_t + 2f * d2.d_t + 2f * d3.d_t + d4.d_t);
        omega_theta += (dt / 6f) * (d1.d_ot + 2f * d2.d_ot + 2f * d3.d_ot + d4.d_ot);
        phi += (dt / 6f) * (d1.d_p + 2f * d2.d_p + 2f * d3.d_p + d4.d_p);
        omega_phi += (dt / 6f) * (d1.d_op + 2f * d2.d_op + 2f * d3.d_op + d4.d_op);

        currentLength += (dt / 6f) * (d1.d_L + 2f * d2.d_L + 2f * d3.d_L + d4.d_L);
        ropeVelocity += (dt / 6f) * (d1.d_dL + 2f * d2.d_dL + 2f * d3.d_dL + d4.d_dL);

        // حد أدنى وأقصى للطول
        currentLength = Mathf.Clamp(currentLength, L0 * 0.2f, L0 * 3f);

        AngularAccelerationTheta =
            (d1.d_ot + 2f * d2.d_ot + 2f * d3.d_ot + d4.d_ot) / 6f;

        // الجاذبية الفعالة
        float omega_total = Mathf.Sqrt(omega_theta * omega_theta + omega_phi * omega_phi);
        float a_c = omega_total * omega_total * currentLength;
        float a_t = AngularAccelerationTheta * currentLength;
        EffectiveGravity = Mathf.Sqrt(
            Mathf.Pow(g + a_c * Mathf.Cos(theta), 2) +
            Mathf.Pow(a_t * Mathf.Sin(theta), 2));

        // تحديث الموضع
        Vector3 prev = transform.position;
        transform.position = new Vector3(
            pivotPoint.position.x + currentLength * Mathf.Sin(theta) * Mathf.Cos(phi),
            pivotPoint.position.y - currentLength * Mathf.Cos(theta),
            pivotPoint.position.z + currentLength * Mathf.Sin(theta) * Mathf.Sin(phi));

        BucketVelocity = (transform.position - prev) / dt;
        UpdateRopeVisuals();
    }

    // Helper: خطوة Euler لتوليد حالة وسيطة لـ RK4
    static State Step(State s, Derivs d, float h)
    {
        return new State
        {
            t = s.t + h * d.d_t,
            o_t = s.o_t + h * d.d_ot,
            p = s.p + h * d.d_p,
            o_p = s.o_p + h * d.d_op,
            L = s.L + h * d.d_L,
            dL = s.dL + h * d.d_dL
        };
    }

    void UpdateRopeVisuals()
    {
        if (ropeRenderer != null && pivotPoint != null)
        {
            ropeRenderer.SetPosition(0, pivotPoint.position);
            ropeRenderer.SetPosition(1, transform.position);
        }
    }

    public void UpdateBucketMass(float lostMass)
    {
        if (float.IsNaN(lostMass) || float.IsInfinity(lostMass)) return;
        currentMass = Mathf.Max(0.5f, currentMass - lostMass);
    }
}
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
    public float initialOmega = 0f; // تم إعادتها لتتوافق مع السلايدر في الـ UI
    public float initialPhi = 0f;

    // حالة النظام الموحدة (State Vector)
    private float theta;
    private float omega_theta;
    private float phi;
    private float omega_phi;
    private float currentLength;
    private float ropeVelocity;
    private float currentMass;

    // الخصائص المتاحة للأنظمة الأخرى
    public Vector3 BucketVelocity { get; private set; }
    public float EffectiveGravity { get; private set; }
    public float AngularAccelerationTheta { get; private set; }

    // توافقية مسبقة مع نظام السوائل القديم
    public Vector3 DailVelocity => BucketVelocity;

    private struct PendulumState
    {
        public float t, o_t;
        public float p, o_p;
        public float L, dL;
    }

    private struct PendulumDerivatives
    {
        public float d_theta;
        public float d_omega_theta;
        public float d_phi;
        public float d_omega_phi;
        public float d_L;
        public float d_dL;
    }

    void Start()
    {
        currentMass = m0;
        currentLength = L0;
        ropeVelocity = 0f;

        theta = initialTheta * Mathf.Deg2Rad;
        omega_theta = initialOmega * Mathf.Deg2Rad; // التعيين الابتدائي للسرعة الزاوية من الـ UI
        phi = initialPhi * Mathf.Deg2Rad;
        omega_phi = 0f;

        if (pivotPoint == null)
        {
            GameObject defaultPivot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            defaultPivot.name = "Default_Pendulum_Pivot";
            defaultPivot.transform.position = transform.position + Vector3.up * L0;
            Destroy(defaultPivot.GetComponent<Collider>());
            defaultPivot.transform.localScale = Vector3.one * 0.3f;
            pivotPoint = defaultPivot.transform;
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

    Vector3 GetWindForce()
    {
        return windDirection.normalized * windCoeff * windSpeed * windSpeed;
    }

    PendulumDerivatives CalculateDerivatives(PendulumState state)
    {
        PendulumDerivatives derivs;

        derivs.d_theta = state.o_t;
        derivs.d_phi = state.o_p;
        derivs.d_L = state.dL;

        Vector3 Fw = GetWindForce();

        Vector3 r_hat = new Vector3(Mathf.Sin(state.t) * Mathf.Cos(state.p), -Mathf.Cos(state.t), Mathf.Sin(state.t) * Mathf.Sin(state.p));
        Vector3 theta_hat = new Vector3(Mathf.Cos(state.t) * Mathf.Cos(state.p), Mathf.Sin(state.t), Mathf.Cos(state.t) * Mathf.Sin(state.p));
        Vector3 phi_hat = new Vector3(-Mathf.Sin(state.p), 0f, Mathf.Cos(state.p));

        float Fw_r = Vector3.Dot(Fw, r_hat);
        float Fw_theta = Vector3.Dot(Fw, theta_hat);
        float Fw_phi = Vector3.Dot(Fw, phi_hat);

        float sinTheta = Mathf.Max(Mathf.Sin(state.t), 0.001f);
        float cosTheta = Mathf.Cos(state.t);

        // حساب التسارع الزاوي المقترن بدقة (RK4 Engine)
        derivs.d_omega_theta = sinTheta * cosTheta * state.o_p * state.o_p
                               - (g / state.L) * sinTheta
                               - 2f * (state.dL / state.L) * state.o_t
                               - (b / currentMass) * state.o_t
                               + (Fw_theta / (currentMass * state.L));

        derivs.d_omega_phi = -2f * (state.dL / state.L) * state.o_p
                             - 2f * (cosTheta / sinTheta) * state.o_t * state.o_p
                             - (b / currentMass) * state.o_p
                             + (Fw_phi / (currentMass * state.L * sinTheta));

        float centrifugalForce = currentMass * state.L * (state.o_t * state.o_t + sinTheta * sinTheta * state.o_p * state.o_p);
        float gravityRadial = currentMass * g * cosTheta;
        float F_spring = -k_rope * (state.L - L0);
        float F_damper = -c_rope * state.dL;

        derivs.d_dL = (gravityRadial + centrifugalForce + F_spring + F_damper + Fw_r) / currentMass;

        return derivs;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        PendulumState s = new PendulumState { t = theta, o_t = omega_theta, p = phi, o_p = omega_phi, L = currentLength, dL = ropeVelocity };

        // خطوة التكامل الرابعي RK4
        PendulumDerivatives d1 = CalculateDerivatives(s);

        PendulumState s2 = new PendulumState
        {
            t = s.t + 0.5f * dt * d1.d_theta,
            o_t = s.o_t + 0.5f * dt * d1.d_omega_theta,
            p = s.p + 0.5f * dt * d1.d_phi,
            o_p = s.o_p + 0.5f * dt * d1.d_omega_phi,
            L = s.L + 0.5f * dt * d1.d_L,
            dL = s.dL + 0.5f * dt * d1.d_dL
        };
        PendulumDerivatives d2 = CalculateDerivatives(s2);

        PendulumState s3 = new PendulumState
        {
            t = s.t + 0.5f * dt * d2.d_theta,
            o_t = s.o_t + 0.5f * dt * d2.d_omega_theta,
            p = s.p + 0.5f * dt * d2.d_phi,
            o_p = s.o_p + 0.5f * dt * d2.d_omega_phi,
            L = s.L + 0.5f * dt * d2.d_L,
            dL = s.dL + 0.5f * dt * d2.d_dL
        };
        PendulumDerivatives d3 = CalculateDerivatives(s3);

        PendulumState s4 = new PendulumState
        {
            t = s.t + dt * d3.d_theta,
            o_t = s.o_t + dt * d3.d_omega_theta,
            p = s.p + dt * d3.d_phi,
            o_p = s.o_p + dt * d3.d_omega_phi,
            L = s.L + dt * d3.d_L,
            dL = s.dL + dt * d3.d_dL
        };
        PendulumDerivatives d4 = CalculateDerivatives(s4);

        theta += (dt / 6f) * (d1.d_theta + 2f * d2.d_theta + 2f * d3.d_theta + d4.d_theta);
        omega_theta += (dt / 6f) * (d1.d_omega_theta + 2f * d2.d_omega_theta + 2f * d3.d_omega_theta + d4.d_omega_theta);

        phi += (dt / 6f) * (d1.d_phi + 2f * d2.d_phi + 2f * d3.d_phi + d4.d_phi);
        omega_phi += (dt / 6f) * (d1.d_omega_phi + 2f * d2.d_omega_phi + 2f * d3.d_omega_phi + d4.d_omega_phi);

        currentLength += (dt / 6f) * (d1.d_L + 2f * d2.d_L + 2f * d3.d_L + d4.d_L);
        ropeVelocity += (dt / 6f) * (d1.d_dL + 2f * d2.d_dL + 2f * d3.d_dL + d4.d_dL);

        currentLength = Mathf.Max(currentLength, L0 * 0.2f);

        AngularAccelerationTheta = (d1.d_omega_theta + 2f * d2.d_omega_theta + 2f * d3.d_omega_theta + d4.d_omega_theta) / 6f;

        float omega_total = Mathf.Sqrt(omega_theta * omega_theta + omega_phi * omega_phi);
        float a_c = omega_total * omega_total * currentLength;
        float a_t = AngularAccelerationTheta * currentLength;
        EffectiveGravity = Mathf.Sqrt(Mathf.Pow(g + a_c * Mathf.Cos(theta), 2) + Mathf.Pow(a_t * Mathf.Sin(theta), 2));

        Vector3 previousPosition = transform.position;
        Vector3 newPos;
        newPos.x = pivotPoint.position.x + currentLength * Mathf.Sin(theta) * Mathf.Cos(phi);
        newPos.y = pivotPoint.position.y - currentLength * Mathf.Cos(theta);
        newPos.z = pivotPoint.position.z + currentLength * Mathf.Sin(theta) * Mathf.Sin(phi);

        transform.position = newPos;
        BucketVelocity = (transform.position - previousPosition) / dt;

        UpdateRopeVisuals();
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
        currentMass = Mathf.Max(0.5f, currentMass - lostMass);
    }
}
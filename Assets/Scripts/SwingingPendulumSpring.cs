using UnityEngine;

public class SwingingPendulumSpring : MonoBehaviour
{
    [Header("Pendulum Parameters (Table 3.1.1)")]
    public Transform pivotPoint;
    public float L0 = 5f;
    public float m0 = 2f;
    public float g = 9.81f;
    public float b = 0.1f;

    [Header("Spring-Damper Rope (Section 2.3.3)")]
    public float k_rope = 500f;     // ثابت مرونة الحبل  k
    public float c_rope = 10f;      // معامل تخميد الحبل c (جديد)

    [Header("Wind Parameters (Section 2.7.3)")]
    public float windSpeed = 0f;
    public Vector3 windDirection = Vector3.right;
    public float windCoeff = 0.5f;

    // المتغيرات الزاوية
    private float theta = 45f * Mathf.Deg2Rad;
    private float omega_theta = 0f;
    private float phi = 0f;
    private float omega_phi = 0f;

    // متغيرات Spring-Damper للحبل
    private float currentLength;    // L اللحظي
    private float ropeVelocity;     // dL/dt — سرعة تغيّر طول الحبل

    private float currentMass;

    public Vector3 BucketVelocity { get; private set; }
    public float EffectiveGravity { get; private set; }
    public float AngularAccelerationTheta { get; private set; }
    public Vector3 DailVelocity => BucketVelocity;

    // -------------------------------------------------------
    void Start()
    {
        currentLength = L0;
        ropeVelocity = 0f;
        currentMass = m0;
        if (pivotPoint == null) pivotPoint = transform;
    }

    // -------------------------------------------------------
    Vector3 GetWindForce() =>
        windDirection.normalized * windCoeff * windSpeed * windSpeed;

    void GetAngularAccelerations(
        float t, float o_t, float p, float o_p,
        out float alpha_theta, out float alpha_phi)
    {
        Vector3 Fw = GetWindForce();
        Vector3 theta_hat = new Vector3(
             Mathf.Cos(t) * Mathf.Cos(p),
            -Mathf.Sin(t),
             Mathf.Cos(t) * Mathf.Sin(p));
        Vector3 phi_hat = new Vector3(-Mathf.Sin(p), 0f, Mathf.Cos(p));

        float windAlpha_theta = Vector3.Dot(Fw, theta_hat) / (currentMass * currentLength);
        float windAlpha_phi = Vector3.Dot(Fw, phi_hat) / (currentMass * currentLength);

        alpha_theta = -(g / currentLength) * Mathf.Sin(t)
                      - (b / currentMass) * o_t
                      + windAlpha_theta;

        alpha_phi = -(b / currentMass) * o_p
                      + windAlpha_phi;
    }

    // -------------------------------------------------------
    // Spring-Damper: حساب تسارع تغيّر طول الحبل
    //
    // القوى على المحور الشعاعي (امتداد الحبل):
    //   F_gravity_radial  = m·g·cos(θ)         ← تشد الحبل للخارج
    //   F_centrifugal     = m·ω²·L             ← تشد الحبل للخارج
    //   F_spring          = −k·(L − L₀)        ← تسحب الحبل للداخل
    //   F_damper          = −c·(dL/dt)          ← تقاوم التغيير
    //
    // d²L/dt² = [F_gravity_radial + F_centrifugal + F_spring + F_damper] / m
    // -------------------------------------------------------
    float GetRopeAcceleration(float L, float dLdt, float omegaTotal)
    {
        float F_gravity_radial = currentMass * g * Mathf.Cos(theta);
        float F_centrifugal = currentMass * omegaTotal * omegaTotal * L;
        float F_spring = -k_rope * (L - L0);
        float F_damper = -c_rope * dLdt;

        return (F_gravity_radial + F_centrifugal + F_spring + F_damper) / currentMass;
    }

    // -------------------------------------------------------
    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        // ======= RK4 — الزوايا =======
        float k1_t, k1_p, k2_t, k2_p, k3_t, k3_p, k4_t, k4_p;
        float dw1_t, dw1_p, dw2_t, dw2_p, dw3_t, dw3_p, dw4_t, dw4_p;

        GetAngularAccelerations(theta, omega_theta, phi, omega_phi, out dw1_t, out dw1_p);
        k1_t = omega_theta; k1_p = omega_phi;

        GetAngularAccelerations(
            theta + 0.5f * dt * k1_t, omega_theta + 0.5f * dt * dw1_t,
            phi + 0.5f * dt * k1_p, omega_phi + 0.5f * dt * dw1_p,
            out dw2_t, out dw2_p);
        k2_t = omega_theta + 0.5f * dt * dw1_t;
        k2_p = omega_phi + 0.5f * dt * dw1_p;

        GetAngularAccelerations(
            theta + 0.5f * dt * k2_t, omega_theta + 0.5f * dt * dw2_t,
            phi + 0.5f * dt * k2_p, omega_phi + 0.5f * dt * dw2_p,
            out dw3_t, out dw3_p);
        k3_t = omega_theta + 0.5f * dt * dw2_t;
        k3_p = omega_phi + 0.5f * dt * dw2_p;

        GetAngularAccelerations(
            theta + dt * k3_t, omega_theta + dt * dw3_t,
            phi + dt * k3_p, omega_phi + dt * dw3_p,
            out dw4_t, out dw4_p);
        k4_t = omega_theta + dt * dw3_t;
        k4_p = omega_phi + dt * dw3_p;

        theta += (dt / 6f) * (k1_t + 2f * k2_t + 2f * k3_t + k4_t);
        omega_theta += (dt / 6f) * (dw1_t + 2f * dw2_t + 2f * dw3_t + dw4_t);
        AngularAccelerationTheta = (dw1_t + 2f * dw2_t + 2f * dw3_t + dw4_t) / 6f;
        phi += (dt / 6f) * (k1_p + 2f * k2_p + 2f * k3_p + k4_p);
        omega_phi += (dt / 6f) * (dw1_p + 2f * dw2_p + 2f * dw3_p + dw4_p);
        // ======= نهاية RK4 الزوايا =======

        // ======= Spring-Damper — طول الحبل =======
        // تكامل Euler للسرعة والطول (الحبل يهتز ديناميكياً)
        float omega_total = Mathf.Sqrt(omega_theta * omega_theta + omega_phi * omega_phi);

        float ropeAccel = GetRopeAcceleration(currentLength, ropeVelocity, omega_total);
        ropeVelocity += ropeAccel * dt;          // dL/dt
        currentLength += ropeVelocity * dt;         // L

        // منع ضغط الحبل — الحبل لا يدفع (يشد فقط)
        currentLength = Mathf.Max(currentLength, L0 * 0.5f);
        // ======= نهاية Spring-Damper =======

        // الجاذبية الفعالة داخل الدلو (Section 2.4.4.1)
        float a_c = omega_total * omega_total * currentLength;
        float a_t = AngularAccelerationTheta * currentLength;
        EffectiveGravity = Mathf.Sqrt(
            Mathf.Pow(g + a_c * Mathf.Cos(theta), 2) +
            Mathf.Pow(a_t * Mathf.Sin(theta), 2));

        // تحديث الموضع ثلاثي الأبعاد
        Vector3 prev = transform.position;
        transform.position = new Vector3(
            pivotPoint.position.x + currentLength * Mathf.Sin(theta) * Mathf.Cos(phi),
            pivotPoint.position.y - currentLength * Mathf.Cos(theta),
            pivotPoint.position.z + currentLength * Mathf.Sin(theta) * Mathf.Sin(phi));

        BucketVelocity = (transform.position - prev) / dt;
    }

    // -------------------------------------------------------
    public void UpdateBucketMass(float lostMass)
    {
        currentMass = Mathf.Max(0.5f, currentMass - lostMass);
    }
}
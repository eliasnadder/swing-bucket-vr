using UnityEngine;

public class SwingingPendulum : MonoBehaviour
{
    [Header("Pendulum Parameters (Table 3.1.1)")]
    public Transform pivotPoint;       // نقطة التعليق الثابتة
    public float L0 = 5f;             // الطول الأصلي للحبل
    public float m0 = 2f;             // الكتلة الابتدائية للدلو (فارغ + طلاء)
    public float g = 9.81f;           // تسارع الجاذبية
    public float b = 0.1f;            // معامل التخميد (مقاومة الهواء)
    public float k_rope = 500f;       // ثابت مرونة الحبل (قانون هوك)

    // المتغيرات الحركية للبندول ثلاثي الأبعاد (زاويتان)
    private float theta = 45f * Mathf.Deg2Rad; // زاوية الميل الرأسي
    private float omega_theta = 0f;            // السرعة الزاوية لـ theta
    private float phi = 0f * Mathf.Deg2Rad;   // زاوية الدوران الأفقي
    private float omega_phi = 0f;              // السرعة الزاوية لـ phi

    private float currentLength;
    private float currentMass;

    // خصائص متاحة للنظام السائل
    public Vector3 DailVelocity { get; private set; }
    public float EffectiveGravity { get; private set; }
    public float AngularAccelerationTheta { get; private set; }

    void Start()
    {
        currentLength = L0;
        currentMass = m0;
        if (pivotPoint == null) pivotPoint = transform;
    }

    // حساب التسارع الزاوي لـ Theta و Phi (المعادلات التفاضلية)
    void GetAngularAccelerations(float t, float o_t, float p, float o_p, out float alpha_theta, out float alpha_phi)
    {
        // حساب الجاذبية الفعالة والتخميد بناءً على تقريركم
        alpha_theta = -(g / currentLength) * Mathf.Sin(t) - (b / currentMass) * o_t;
        alpha_phi = -(b / currentMass) * o_p; // تخميد الحركة الأفقية
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        // 1. التكامل العددي باستخدام خوارزمية RK4 بدقة عالية
        float k1_t, k1_p, k2_t, k2_p, k3_t, k3_p, k4_t, k4_p;
        float dw1_t, dw1_p, dw2_t, dw2_p, dw3_t, dw3_p, dw4_t, dw4_p;

        // الخطوة الأولى
        GetAngularAccelerations(theta, omega_theta, phi, omega_phi, out dw1_t, out dw1_p);
        k1_t = omega_theta; k1_p = omega_phi;

        // الخطوة الثانية
        GetAngularAccelerations(theta + 0.5f * dt * k1_t, omega_theta + 0.5f * dt * dw1_t, phi + 0.5f * dt * k1_p, omega_phi + 0.5f * dt * dw1_p, out dw2_t, out dw2_p);
        k2_t = omega_theta + 0.5f * dt * dw1_t; k2_p = omega_phi + 0.5f * dt * dw1_p;

        // الخطوة الثالثة
        GetAngularAccelerations(theta + 0.5f * dt * k2_t, omega_theta + 0.5f * dt * dw2_t, phi + 0.5f * dt * k2_p, omega_phi + 0.5f * dt * dw2_p, out dw3_t, out dw3_p);
        k3_t = omega_theta + 0.5f * dt * dw2_t; k3_p = omega_phi + 0.5f * dt * dw2_p;

        // الخطوة الرابعة
        GetAngularAccelerations(theta + dt * k3_t, omega_theta + dt * dw3_t, phi + dt * k3_p, omega_phi + dt * dw3_p, out dw4_t, out dw4_p);
        k4_t = omega_theta + dt * dw3_t; k4_p = omega_phi + dt * dw3_p;

        // تحديث القيم النهائية عبر المتوسط المرجح لـ RK4
        theta += (dt / 6f) * (k1_t + 2f * k2_t + 2f * k3_t + k4_t);
        omega_theta += (dt / 6f) * (dw1_t + 2f * dw2_t + 2f * dw3_t + dw4_t);
        AngularAccelerationTheta = (dw1_t + 2f * dw2_t + 2f * dw3_t + dw4_t) / 6f;

        phi += (dt / 6f) * (k1_p + 2f * k2_p + 2f * k3_p + k4_p);
        omega_phi += (dt / 6f) * (dw1_p + 2f * dw2_p + 2f * dw3_p + dw4_p);

        // 2. حساب البندول المرن (قانون هوك F = -kx والتمدد اللحظي)
        float centrifugalForce = currentMass * (omega_theta * omega_theta) * currentLength;
        float gravityComponent = currentMass * g * Mathf.Cos(theta);
        float tensionForce = centrifugalForce + gravityComponent;
        float extension = tensionForce / k_rope;
        currentLength = L0 + extension; // الطول الجديد للحبل

        // 3. حساب الجاذبية الفعالة geff داخل الدلو المتأرجح 
        float a_c = omega_theta * omega_theta * currentLength; // التسارع المركزي
        float a_t = AngularAccelerationTheta * currentLength; // التسارع المماسي
        EffectiveGravity = Mathf.Sqrt(Mathf.Pow(g + a_c * Mathf.Cos(theta), 2) + Mathf.Pow(a_t * Mathf.Sin(theta), 2));

        // 4. تحديث الموضع اللحظي ثلاثي الأبعاد في الفضاء والسرعة الخطية
        Vector3 previousPosition = transform.position;
        Vector3 newPos;
        newPos.x = pivotPoint.position.x + currentLength * Mathf.Sin(theta) * Mathf.Cos(phi);
        newPos.y = pivotPoint.position.y - currentLength * Mathf.Cos(theta);
        newPos.z = pivotPoint.position.z + currentLength * Mathf.Sin(theta) * Mathf.Sin(phi);

        transform.position = newPos;
        DailVelocity = (transform.position - previousPosition) / dt;
    }

    public void UpdateBucketMass(float lostMass)
    {
        currentMass = Mathf.Max(0.5f, currentMass - lostMass); // الحفاظ على كتلة الدلو فارغاً كحد أدنى
    }
}
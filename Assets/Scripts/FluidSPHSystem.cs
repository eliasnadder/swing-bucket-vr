using UnityEngine;
using System.Collections.Generic;

public class FluidSPHSystem : MonoBehaviour
{
    public struct SPHParticle
    {
        public Vector3 position;
        public Vector3 velocity;
        public float density;
        public float pressure;
        public Color color; // Dynamic color tracking per particle
    }
    [Header("Dynamic Color Setup")]
    public Color currentPaintColor = Color.red; // Can be altered via UI mid-simulation

    [Header("Relying References")]
    public SwingingPendulum pendulum;

    [Header("Fluid Properties (Table 2.4.2)")]
    public float orificeDiameter = 0.05f; // قطر فتحة الخروج d
    public float h_paint = 0.3f;          // ارتفاع عمود الطلاء داخل الدلو h
    public float Cd = 0.6f;               // معامل التدفق
    public float paintDensity0 = 1200f;   // الكثافة الطبيعية للطلاء
    public float viscosity = 0.1f;        // معامل اللزوجة
    public float h_kernel = 0.2f;         // نصف قطر دالة التنعيم SPH

    private List<SPHParticle> particles = new List<SPHParticle>();
    private float initialVolume = 0.01f; // حجم الطلاء الابتدائي م3
    private float currentVolume;

    void Start()
    {
        currentVolume = initialVolume;
        if (pendulum == null) pendulum = GetComponent<SwingingPendulum>();
    }

    // Public method for UI interaction to refill or change fluid settings dynamically
    public void ChangePaintColor(Color newColor)
    {
        currentPaintColor = newColor;
    }

    void FixedUpdate()
    {
        if (currentVolume <= 0) return;

        float dt = Time.fixedDeltaTime;

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
        h_paint = (currentVolume / initialVolume) * 0.3f;

        // 2. توليد الجسيمات بناءً على معدل التدفق اللحظي
        int particlesToSpawn = Mathf.RoundToInt(Q * 10000f * dt); // عامل تحجيم بصري للجسيمات
        for (int i = 0; i < particlesToSpawn; i++)
        {
            SPHParticle p = new SPHParticle();
            p.position = transform.position; // تبدأ من أسفل الدلو
            // سرعة المقذوف = سرعة الدلو + سرعة الاندفاع لأسفل
            p.velocity = pendulum.DailVelocity + Vector3.down * v_out;
            p.density = paintDensity0;
            p.pressure = 0f;
            p.color = currentPaintColor; // Assign the active color at emission moment
            particles.Add(p);
        }

        // 3. تحديث جزيئات SPH (حساب الكثافة والضغط واللزوجة) والتكامل الحركي الجسيمي
        UpdateSPH();
    }

    void UpdateSPH()
    {
        float dt = Time.fixedDeltaTime;

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
                    densitySum += paintDensity0 * Mathf.Pow(h_kernel * h_kernel - dist * dist, 3);
                }
            }
            SPHParticle p = particles[i];
            p.density = Mathf.Max(paintDensity0, densitySum);
            p.pressure = 2000f * (p.density - paintDensity0); // معادلة الحالة للضغط
            particles[i] = p;
        }

        // تطبيق القوى وتحديث المواقع (محاكاة مقذوفات حرّة خاضعة للجاذبية ومقاومة الهواء)
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            SPHParticle p = particles[i];

            // القوى الخارجية المؤثرة (الجاذبية + مقاومة الهواء الفيزيائية Fdrag = 0.5 * Cd * rho * A * v^2)
            Vector3 F_gravity = Vector3.down * 9.81f;
            Vector3 F_drag = -0.5f * 0.47f * 1.2f * (Mathf.PI * 0.01f * 0.01f) * p.velocity.magnitude * p.velocity;

            Vector3 totalAcceleration = F_gravity + (F_drag / (paintDensity0 * 0.001f));
            p.velocity += totalAcceleration * dt;
            p.position += p.velocity * dt;

            particles[i] = p;

            // إرسال البيانات لنظام الرسم عند اصطدام الجسيم بسطح اللوحة (Y = 0 فرضا) بدون كولايدرز
            if (p.position.y <= 0f)
            {
                PaintSurfaceCanvas.Instance.PaintAt(p.position, p.velocity, p.color);
                particles.RemoveAt(i); // إزالة الجسيم بعد الاصطدام والامتصاص
            }
        }
    }
}
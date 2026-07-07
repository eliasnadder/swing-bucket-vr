using UnityEngine;

/// <summary>
/// يحسب اصطدام جسيمات SPH بجدران BoxContainer، مع دعم كامل لدوران/إزاحة الصندوق.
/// الفكرة: نحوّل موضع وسرعة كل جسيم من World Space إلى Local Space الخاص بالصندوق
/// (حيث الصندوق ثابت ومحاذي للمحاور)، نطبّق الـ clamp/reflect هناك، ثم نرجع النتيجة لـ World Space.
/// </summary>
public class BoxFluidBoundary : MonoBehaviour
{
    [Header("References")]
    public SPHFluidSolver solver;
    public BoxContainer box;

    [Header("Collision Response")]
    [Range(0f, 1f)] public float restitution = 0.25f;   // ارتداد عمودي على الجدار
    [Range(0f, 1f)] public float friction = 0.85f;      // احتكاك موازٍ للجدار
    [Tooltip("هامش صغير لمنع الجسيمات من الانغراس داخل الجدار")]
    public float skin = 0.05f;

    public void ResolveContacts()
    {
        if (solver == null || box == null) return;

        Matrix4x4 worldToLocal = box.transform.worldToLocalMatrix;
        Matrix4x4 localToWorld = box.transform.localToWorldMatrix;
        Vector3 half = box.HalfExtentsInner - Vector3.one * skin;

        int count = solver.ParticleCount;
        for (int i = 0; i < count; i++)
        {
            SPHParticle p = solver.GetParticle(i);

            Vector3 lp = worldToLocal.MultiplyPoint3x4(p.position);
            Vector3 lv = worldToLocal.MultiplyVector(p.velocity);
            bool hit = false;

            // ── المحور X ──
            if (lp.x < -half.x) { lp.x = -half.x; if (lv.x < 0f) { lv.x = -lv.x * restitution; lv.y *= friction; lv.z *= friction; } hit = true; }
            else if (lp.x > half.x) { lp.x = half.x; if (lv.x > 0f) { lv.x = -lv.x * restitution; lv.y *= friction; lv.z *= friction; } hit = true; }

            // ── المحور Y (الأرضية دائمًا صلبة، السقف اختياري) ──
            if (lp.y < -half.y) { lp.y = -half.y; if (lv.y < 0f) { lv.y = -lv.y * restitution; lv.x *= friction; lv.z *= friction; } hit = true; }
            else if (!box.openTop && lp.y > half.y) { lp.y = half.y; if (lv.y > 0f) { lv.y = -lv.y * restitution; lv.x *= friction; lv.z *= friction; } hit = true; }

            // ── المحور Z ──
            if (lp.z < -half.z) { lp.z = -half.z; if (lv.z < 0f) { lv.z = -lv.z * restitution; lv.x *= friction; lv.y *= friction; } hit = true; }
            else if (lp.z > half.z) { lp.z = half.z; if (lv.z > 0f) { lv.z = -lv.z * restitution; lv.x *= friction; lv.y *= friction; } hit = true; }

            if (hit)
            {
                p.position = localToWorld.MultiplyPoint3x4(lp);
                p.velocity = localToWorld.MultiplyVector(lv);
                solver.SetParticle(i, p);
            }
        }
    }
}

using UnityEngine;

public class BoxFluidBoundary : MonoBehaviour
{
    [Header("References")]
    public SPHFluidSolver solver;
    public BoxContainer box;

    [Header("Collision Response")]
    [Range(0f, 1f)] public float restitution = 0.25f;
    [Range(0f, 1f)] public float friction = 0.85f;
    public float skin = 0.05f;

    public void ResolveContacts()
    {
        if (solver == null || box == null) return;

        Matrix4x4 worldToLocal = box.transform.worldToLocalMatrix;
        Matrix4x4 localToWorld = box.transform.localToWorldMatrix;
        Vector3 half = box.HalfExtentsInner - Vector3.one * skin;

        bool hasHole = box.HasDrainHole;
        float holeR2 = box.HoleRadius * box.HoleRadius;
        Vector2 holeOffset = box.HoleOffset;

        int count = solver.ParticleCount;
        for (int i = 0; i < count; i++)
        {
            SPHParticle p = solver.GetParticle(i);

            Vector3 lp = worldToLocal.MultiplyPoint3x4(p.position);
            Vector3 lv = worldToLocal.MultiplyVector(p.velocity);
            bool hit = false;

            if (lp.x < -half.x) { lp.x = -half.x; if (lv.x < 0f) { lv.x = -lv.x * restitution; lv.y *= friction; lv.z *= friction; } hit = true; }
            else if (lp.x > half.x) { lp.x = half.x; if (lv.x > 0f) { lv.x = -lv.x * restitution; lv.y *= friction; lv.z *= friction; } hit = true; }

            // ── الأرضية: تسمح بالمرور عبر فتحة التصريف إن وُجدت ──
            bool inHole = hasHole &&
                (lp.x - holeOffset.x) * (lp.x - holeOffset.x) +
                (lp.z - holeOffset.y) * (lp.z - holeOffset.y) <= holeR2;

            if (lp.y < -half.y)
            {
                if (!inHole)
                {
                    lp.y = -half.y;
                    if (lv.y < 0f) { lv.y = -lv.y * restitution; lv.x *= friction; lv.z *= friction; }
                    hit = true;
                }
                // else: داخل الفتحة — الجسيم يسقط ويخرج من الصندوق بحرية
            }
            else if (!box.openTop && lp.y > half.y) { lp.y = half.y; if (lv.y > 0f) { lv.y = -lv.y * restitution; lv.x *= friction; lv.z *= friction; } hit = true; }

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
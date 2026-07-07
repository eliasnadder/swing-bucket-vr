using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// يملأ BoxContainer بجسيمات SPH (طلاء) عند بدء التشغيل، بدل الانبعاث من فوهة.
/// </summary>
public class BoxFluidSeeder : MonoBehaviour
{
    [Header("References")]
    public SPHFluidSolver solver;
    public BoxContainer box;

    [Header("Fill Settings")]
    [Range(0.05f, 0.95f)]
    [Tooltip("نسبة ارتفاع الطلاء داخل الصندوق عند البداية (0 = فارغ، 1 = ممتلئ للحافة)")]
    public float fillRatio = 0.4f;
    [Tooltip("المسافة بين الجسيمات عند التعبئة (cm) — يفضّل أن تكون أصغر من smoothingRadius")]
    public float particleSpacing = 1.5f;
    [Range(0f, 1f)] public float jitter = 0.15f;
    public Color paintColor = new Color(0.85f, 0.1f, 0.1f, 1f);

    // تأكد من أن Seeder ينفذ مرة واحدة فقط
    private bool hasSeeded = false;

    void Awake()
    {
        if (solver == null) solver = FindAnyObjectByType<SPHFluidSolver>();
        if (box == null) box = GetComponent<BoxContainer>();
    }

    void Start()
    {
        if (!hasSeeded)
            SeedParticles();
    }

    public void SeedParticles()
    {
        if (hasSeeded) return;
        if (solver == null || box == null) return;

        // مسح أي جسيمات موجودة مسبقًا
        solver.ClearParticles();

        Vector3 half = box.HalfExtentsInner;
        float fillTopLocalY = -half.y + (2f * half.y * fillRatio);
        float spacing = Mathf.Max(0.1f, particleSpacing);

        int spawned = 0;
        for (float x = -half.x + spacing * 0.5f; x <= half.x - spacing * 0.5f; x += spacing)
        {
            for (float z = -half.z + spacing * 0.5f; z <= half.z - spacing * 0.5f; z += spacing)
            {
                for (float y = -half.y + spacing * 0.5f; y <= fillTopLocalY; y += spacing)
                {
                    Vector3 jitterOffset = new Vector3(
                        Random.Range(-spacing, spacing) * jitter,
                        Random.Range(-spacing, spacing) * jitter,
                        Random.Range(-spacing, spacing) * jitter);

                    Vector3 localPos = new Vector3(x, y, z) + jitterOffset;
                    Vector3 worldPos = box.transform.TransformPoint(localPos);

                    solver.AddParticle(worldPos, Vector3.zero, paintColor);
                    spawned++;
                }
            }
        }

        hasSeeded = true;
        Debug.Log($"[BoxFluidSeeder] Seeded {spawned} SPH particles inside the box.");
    }
}

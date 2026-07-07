using UnityEngine;

/// <summary>
/// منسّق المحاكاة لمشهد الصندوق: يستدعي خطوة SPH ثم يحلّ التصادم مع جدران الصندوق كل FixedUpdate.
/// </summary>
public class BoxSimulationController : MonoBehaviour
{
    public SPHFluidSolver fluidSolver;
    public BoxFluidBoundary boundary;
    public BoxFluidSeeder seeder;
    public SPHRenderer sphRenderer;
    public bool autoRun = true;

    void Awake()
    {
        if (fluidSolver == null) fluidSolver = FindAnyObjectByType<SPHFluidSolver>();
        if (boundary == null) boundary = FindAnyObjectByType<BoxFluidBoundary>();
        if (seeder == null) seeder = FindAnyObjectByType<BoxFluidSeeder>();
        if (sphRenderer == null) sphRenderer = FindAnyObjectByType<SPHRenderer>();

        if (fluidSolver != null)
        {
            fluidSolver.useInternalEmission = false; // لا يوجد فوهة في هذا المشهد
            fluidSolver.enabled = false; // نوقف StepSimulation التلقائي من SPHFluidSolver
        }

        if (sphRenderer != null)
            sphRenderer.showAirStream = false; // لا حاجة لخط الانبعاث هنا
    }

    void Start()
    {
        // تأكد من أن Seeder ينفذ قبل أي شيء آخر
        if (seeder != null)
            seeder.SeedParticles();
    }

    void FixedUpdate()
    {
        if (!autoRun || fluidSolver == null) return;

        float dt = Time.fixedDeltaTime;
        fluidSolver.StepSimulation(dt);

        if (boundary != null)
            boundary.ResolveContacts();
    }
}

using UnityEngine;

public class SimulationController : MonoBehaviour
{
    [Header("References")]
    public PaintEmitter paintEmitter;
    public SPHFluidSolver fluidSolver;
    public CustomBoundary boundary;
    public PaintCanvas paintCanvas;
    public SPHRenderer sphRenderer;

    [Header("Simulation")]
    public bool autoRun = true;

    private void Awake()
    {
        if (paintEmitter == null)
            paintEmitter = FindAnyObjectByType<PaintEmitter>();
        if (fluidSolver == null)
            fluidSolver = FindAnyObjectByType<SPHFluidSolver>();
        if (boundary == null)
            boundary = FindAnyObjectByType<CustomBoundary>();
        if (paintCanvas == null)
            paintCanvas = FindAnyObjectByType<PaintCanvas>();
        if (sphRenderer == null)
            sphRenderer = FindAnyObjectByType<SPHRenderer>();

        WireDependencies();
    }

    private void WireDependencies()
    {
        if (paintEmitter != null)
            paintEmitter.solver = fluidSolver;

        if (boundary != null)
        {
            boundary.solver = fluidSolver;
            boundary.paintCanvas = paintCanvas;
        }

        if (sphRenderer != null)
            sphRenderer.solver = fluidSolver;
    }

    private void FixedUpdate()
    {
        if (!autoRun) return;

        float dt = Time.fixedDeltaTime;

        if (paintEmitter != null)
            paintEmitter.Emit(dt);

        if (fluidSolver != null)
            fluidSolver.StepSimulation(dt);

        if (boundary != null)
            boundary.ResolveContacts(dt);
        if (Time.frameCount % 30 == 0)
            Debug.Log($"ParticleCount={fluidSolver.ParticleCount}");
    }
}
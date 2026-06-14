using UnityEngine;

public class SimulationController : MonoBehaviour
{
    [Header("References")]
    public BucketPendulum bucketPendulum;
    public PaintEmitter paintEmitter;
    public SPHFluidSolver fluidSolver;
    public CustomBoundary boundary;
    public PaintCanvas paintCanvas;
    public SPHRenderer sphRenderer;

    [Header("Simulation")]
    public bool autoRun = true;
    public float fixedStep = 0.0166667f;
    public int maxSubSteps = 4;

    private float accumulator;

    private void Awake()
    {
        if (bucketPendulum == null)
            bucketPendulum = FindAnyObjectByType<BucketPendulum>();
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

    private void OnValidate()
    {
        fixedStep = Mathf.Max(0.0001f, fixedStep);
        maxSubSteps = Mathf.Max(1, maxSubSteps);
    }

    private void WireDependencies()
    {
        if (paintEmitter != null)
        {
            paintEmitter.bucket = bucketPendulum;
            paintEmitter.solver = fluidSolver;
        }

        if (boundary != null)
        {
            boundary.solver = fluidSolver;
            boundary.paintCanvas = paintCanvas;
        }

        if (sphRenderer != null)
            sphRenderer.solver = fluidSolver;
    }

    private void Update()
    {
        if (!autoRun)
            return;

        accumulator += Time.deltaTime;
        int subSteps = 0;

        while (accumulator >= fixedStep && subSteps < maxSubSteps)
        {
            StepSimulation(fixedStep);
            accumulator -= fixedStep;
            subSteps++;
        }
    }

    public void StepSimulation(float dt)
    {
        if (bucketPendulum != null)
        {
            bucketPendulum.simulateAutomatically = false;
            bucketPendulum.Step(dt);
        }

        if (paintEmitter != null)
            paintEmitter.Emit(dt);

        if (fluidSolver != null)
            fluidSolver.StepSimulation(dt);

        if (boundary != null)
            boundary.ResolveContacts(dt);
    }
}

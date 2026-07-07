using UnityEngine;
using TMPro;

public class LiveDataOverlay : MonoBehaviour
{
    [Header("References")]
    public SwingingCoupledSpringPendulum pendulum;
    public SPHFluidSolver fluidSystem;   // ← تم التحويل من FluidSPHSystem
    public BucketBuilder  bucketBuilder; // ← needed for bucket-interior particle test
    public PaintCanvas    paintCanvas;   // ← needed for canvas splat count

    [Header("UI")]
    public TextMeshProUGUI overlayText;

    [Header("Refresh")]
    [Range(0.05f, 0.5f)] public float updateInterval = 0.1f;
    private float timer;

    // cached per-refresh so we don't iterate particles every frame
    private int cachedInBucket;

    void Start()
    {
        // تعيين تلقائي إذا لم يُربط في Inspector
        if (fluidSystem == null)
            fluidSystem = FindAnyObjectByType<SPHFluidSolver>();
        if (pendulum == null)
            pendulum = FindAnyObjectByType<SwingingCoupledSpringPendulum>();
        if (bucketBuilder == null)
            bucketBuilder = FindAnyObjectByType<BucketBuilder>();
        if (paintCanvas == null)
            paintCanvas = FindAnyObjectByType<PaintCanvas>();
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer < updateInterval) return;
        timer = 0f;

        // Count particles inside bucket every refresh interval (not every frame)
        cachedInBucket = CountParticlesInBucket();

        if (overlayText != null)
            overlayText.text = BuildText();
    }

    // ── Counts how many active SPH particles are currently inside the bucket. ──
    // Uses a cylinder test: horizontal distance from bucket axis < topRadius,
    // and world Y between bucket bottom and top.
    private int CountParticlesInBucket()
    {
        if (fluidSystem == null || bucketBuilder == null) return 0;

        // Derive bucket world-space cylinder from BucketBuilder's public fields.
        // Spawn point is at the bottom of the bucket interior.
        Vector3 spawnWorld  = bucketBuilder.GetPaintSpawnPosition();
        float   bucketTop   = spawnWorld.y + bucketBuilder.bucketHeight;
        float   radiusSq    = bucketBuilder.topRadius * bucketBuilder.topRadius; // generous top radius

        // Bucket pivot X/Z (the cylinder axis projected on XZ)
        float axisX = spawnWorld.x;
        float axisZ = spawnWorld.z;

        var particles = fluidSystem.Particles;
        int count = 0;
        for (int i = 0; i < particles.Count; i++)
        {
            Vector3 pos = particles[i].position;
            float dx = pos.x - axisX;
            float dz = pos.z - axisZ;
            if (pos.y >= spawnWorld.y && pos.y <= bucketTop &&
                dx * dx + dz * dz <= radiusSq)
                count++;
        }
        return count;
    }

    string BuildText()
    {
        const float U2M = 100f;

        bool hasPendulum = pendulum != null;

        float speed    = hasPendulum ? pendulum.BucketVelocity.magnitude / U2M : 0f;
        float L        = hasPendulum ? pendulum.CurrentLength / U2M : 0f;
        float gEff     = hasPendulum ? pendulum.EffectiveGravity / U2M : 0f;
        float thetaDeg = hasPendulum ? pendulum.CurrentTheta * Mathf.Rad2Deg : 0f;

        float ke = 0f, pe = 0f;
        if (hasPendulum)
        {
            float m = pendulum.m0;
            float g = pendulum.g / U2M;
            ke = 0.5f * m * speed * speed;
            pe = m * g * L * (1f - Mathf.Cos(pendulum.CurrentTheta));
        }

        int   particles   = fluidSystem != null ? fluidSystem.ActiveParticleCount : 0;
        float Q           = fluidSystem != null ? fluidSystem.CurrentFlowRate : 0f;
        int   inBucket    = cachedInBucket;
        int   onCanvas    = paintCanvas != null ? paintCanvas.SplatCount : 0;

        var sb = new System.Text.StringBuilder();
        sb.Append("<b>━━ Live Data ━━</b>\n");
        sb.Append($"On Canvas : <color=#4FC3F7>{onCanvas}</color>\n");
        sb.Append($"Flow Q    : <color=#4FC3F7>{Q * 1000f:F2} mL/s</color>\n");

        if (hasPendulum)
        {
            sb.Append($"Speed     : <color=#FFD54F>{speed:F2} m/s</color>\n");
            sb.Append($"Rope L    : <color=#FFD54F>{L:F3} m</color>\n");
            sb.Append($"θ         : <color=#FFD54F>{thetaDeg:F1}°</color>\n");
            sb.Append($"g_eff     : <color=#CE93D8>{gEff:F2} m/s²</color>\n");
            sb.Append($"KE        : <color=#A5D6A7>{ke:F2} J</color>\n");
            sb.Append($"PE        : <color=#A5D6A7>{pe:F2} J</color>\n");
            sb.Append($"E_total   : <color=#EF9A9A>{ke + pe:F2} J</color>");
        }
        else
        {
            sb.Append("<color=#888888><i>(no pendulum in scene)</i></color>");
        }

        return sb.ToString();
    }
}

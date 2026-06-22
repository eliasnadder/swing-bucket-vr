using UnityEngine;
using TMPro;

public class LiveDataOverlay : MonoBehaviour
{
    [Header("References")]
    public SwingingCoupledSpringPendulum pendulum;
    public SPHFluidSolver fluidSystem;   // ← تم التحويل من FluidSPHSystem

    [Header("UI")]
    public TextMeshProUGUI overlayText;

    [Header("Refresh")]
    [Range(0.05f, 0.5f)] public float updateInterval = 0.1f;
    private float timer;

    void Start()
    {
        // تعيين تلقائي إذا لم يُربط في Inspector
        if (fluidSystem == null)
            fluidSystem = FindAnyObjectByType<SPHFluidSolver>();
        if (pendulum == null)
            pendulum = FindAnyObjectByType<SwingingCoupledSpringPendulum>();
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer < updateInterval) return;
        timer = 0f;
        if (overlayText != null)
            overlayText.text = BuildText();
    }

    string BuildText()
    {
        const float U2M = 100f;

        float speed  = pendulum != null ? pendulum.BucketVelocity.magnitude / U2M : 0f;
        float L      = pendulum != null ? pendulum.CurrentLength / U2M : 0f;
        float gEff   = pendulum != null ? pendulum.EffectiveGravity / U2M : 0f;
        float thetaDeg = pendulum != null ? pendulum.CurrentTheta * Mathf.Rad2Deg : 0f;

        float ke = 0f, pe = 0f;
        if (pendulum != null)
        {
            float m = pendulum.m0;
            float g = pendulum.g / U2M;
            ke = 0.5f * m * speed * speed;
            pe = m * g * L * (1f - Mathf.Cos(pendulum.CurrentTheta));
        }

        int   particles = fluidSystem != null ? fluidSystem.ActiveParticleCount : 0;
        float Q         = fluidSystem != null ? fluidSystem.CurrentFlowRate : 0f;

        return
            "<b>━━ Live Data ━━</b>\n" +
            $"Particles : <color=#4FC3F7>{particles}</color>\n" +
            $"Flow Q    : <color=#4FC3F7>{Q * 1000f:F2} mL/s</color>\n" +
            $"Speed     : <color=#FFD54F>{speed:F2} m/s</color>\n" +
            $"Rope L    : <color=#FFD54F>{L:F3} m</color>\n" +
            $"θ         : <color=#FFD54F>{thetaDeg:F1}°</color>\n" +
            $"g_eff     : <color=#CE93D8>{gEff:F2} m/s²</color>\n" +
            $"KE        : <color=#A5D6A7>{ke:F2} J</color>\n" +
            $"PE        : <color=#A5D6A7>{pe:F2} J</color>\n" +
            $"E_total   : <color=#EF9A9A>{ke + pe:F2} J</color>";
    }
}
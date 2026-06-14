using UnityEngine;

public class CustomBoundary : MonoBehaviour
{
    [Header("References")]
    public SPHFluidSolver solver;
    public PaintCanvas paintCanvas;

    [Header("Canvas Contact")]
    public float bounceDamping = 0.15f;
    public float surfaceFriction = 0.9f;
    public bool killParticleAfterPaint = true;
    public float collisionPadding = 0.002f;
    public float splatRadiusWorld = 0.03f;

    [Header("Optional Box Bounds")]
    public bool useBoxBounds;
    public Vector3 boxCenter;
    public Vector3 boxSize = new Vector3(10f, 10f, 10f);
    public float boxBounce = 0.2f;

    public void ResolveContacts(float dt)
    {
        if (solver == null)
            return;

        for (int i = solver.ParticleCount - 1; i >= 0; i--)
        {
            SPHParticle particle = solver.GetParticle(i);
            bool painted = false;

            if (paintCanvas != null && paintCanvas.IsWorldPointOnCanvas(particle.position) && particle.velocity.y <= 0f)
            {
                paintCanvas.QueueSplat(
                    particle.position,
                    particle.color,
                    splatRadiusWorld,
                    solver.viscosity,
                    particle.velocity);

                painted = true;
                particle.position.y += collisionPadding;
                particle.velocity.y = Mathf.Abs(particle.velocity.y) * bounceDamping;
                particle.velocity.x *= surfaceFriction;
                particle.velocity.z *= surfaceFriction;
            }

            if (useBoxBounds)
                ResolveBoxBounds(ref particle);

            if (painted && killParticleAfterPaint)
            {
                solver.RemoveParticleAt(i);
                continue;
            }

            solver.SetParticle(i, particle);
        }
    }

    private void ResolveBoxBounds(ref SPHParticle particle)
    {
        Vector3 min = boxCenter - boxSize * 0.5f;
        Vector3 max = boxCenter + boxSize * 0.5f;

        if (particle.position.x < min.x)
        {
            particle.position.x = min.x;
            particle.velocity.x = Mathf.Abs(particle.velocity.x) * boxBounce;
        }
        else if (particle.position.x > max.x)
        {
            particle.position.x = max.x;
            particle.velocity.x = -Mathf.Abs(particle.velocity.x) * boxBounce;
        }

        if (particle.position.y < min.y)
        {
            particle.position.y = min.y;
            particle.velocity.y = Mathf.Abs(particle.velocity.y) * boxBounce;
        }
        else if (particle.position.y > max.y)
        {
            particle.position.y = max.y;
            particle.velocity.y = -Mathf.Abs(particle.velocity.y) * boxBounce;
        }

        if (particle.position.z < min.z)
        {
            particle.position.z = min.z;
            particle.velocity.z = Mathf.Abs(particle.velocity.z) * boxBounce;
        }
        else if (particle.position.z > max.z)
        {
            particle.position.z = max.z;
            particle.velocity.z = -Mathf.Abs(particle.velocity.z) * boxBounce;
        }
    }
}

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
    public float splatRadiusWorld = 1.5f;

    [Header("Optional Box Bounds")]
    public bool useBoxBounds;
    public Vector3 boxCenter;
    public Vector3 boxSize = new Vector3(10f, 10f, 10f);
    public float boxBounce = 0.2f;

    // ── موضع كل جسيم في الفريم السابق لكشف العبور (sweep test) ──
    private readonly System.Collections.Generic.Dictionary<int, Vector3> previousPositions
        = new System.Collections.Generic.Dictionary<int, Vector3>();

    public void ResolveContacts(float dt)
    {
        if (solver == null)
            return;

        if (paintCanvas == null)
        {
            previousPositions.Clear();
            return;
        }

        // احسب Y اللوحة مرة واحدة لكل الجسيمات
        float canvasY = paintCanvas.transform.position.y;

        for (int i = solver.ParticleCount - 1; i >= 0; i--)
        {
            SPHParticle particle = solver.GetParticle(i);
            bool painted = false;

            // ── Sweep Test: هل عبر الجسيم مستوى Y اللوحة منذ الفريم الأخير؟ ──
            bool hasPrev = previousPositions.TryGetValue(i, out Vector3 prevPos);
            Vector3 currPos = particle.position;

            bool crossedDown = hasPrev
                ? (prevPos.y > canvasY && currPos.y <= canvasY)
                : currPos.y <= canvasY + paintCanvas.contactThickness;

            if (crossedDown && particle.velocity.y <= 0f)
            {
                // احسب نقطة التقاطع الدقيقة على مستوى Y اللوحة
                Vector3 hitPoint = hasPrev
                    ? Vector3.Lerp(prevPos, currPos, (canvasY - prevPos.y) / (currPos.y - prevPos.y))
                    : currPos;
                hitPoint.y = canvasY;

                // تحقق من أن النقطة داخل حدود اللوحة (XZ فقط)
                if (paintCanvas.TryWorldToPixel(hitPoint, out _, out _))
                {
                    paintCanvas.QueueSplat(
                        hitPoint,
                        particle.color,
                        splatRadiusWorld,
                        solver.viscosity,
                        particle.velocity);

                    painted = true;
                    particle.position.y = canvasY + collisionPadding;
                    particle.velocity.y = Mathf.Abs(particle.velocity.y) * bounceDamping;
                    particle.velocity.x *= surfaceFriction;
                    particle.velocity.z *= surfaceFriction;
                }
            }

            if (useBoxBounds)
                ResolveBoxBounds(ref particle);

            if (painted && killParticleAfterPaint)
            {
                solver.RemoveParticleAt(i);
                previousPositions.Remove(i);
                continue;
            }

            solver.SetParticle(i, particle);
            previousPositions[i] = particle.position;
        }

        // نظّف مواضع الجسيمات المحذوفة (indices فوق العدد الحالي)
        var keysToRemove = new System.Collections.Generic.List<int>();
        foreach (var key in previousPositions.Keys)
            if (key >= solver.ParticleCount) keysToRemove.Add(key);
        foreach (var key in keysToRemove)
            previousPositions.Remove(key);
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
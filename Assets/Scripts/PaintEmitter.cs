using UnityEngine;

public class PaintEmitter : MonoBehaviour
{
    [Header("References")]
    public BucketPendulum bucket;
    public SPHFluidSolver solver;

    [Header("Emitter")]
    public Transform holeTransform;
    public Vector3 holeLocalOffset = new Vector3(0f, -0.12f, 0f);
    public float holeRadius = 0.02f;
    public float flowRate = 0.25f;
    public float paintViscosity = 0.25f;
    public float particleInitialSpeed = 0.2f;
    public float spraySpread = 0.02f;
    public Color color = Color.red;

    [Header("Paint Amount")]
    public float paintVolume = 1f;
    public float particleVolume = 0.00002f;

    [Header("Emission")]
    public bool emitOnStart = true;
    public float emissionAccumulator;

    private void Start()
    {
        if (bucket == null)
            bucket = GetComponentInParent<BucketPendulum>();

        if (holeTransform == null && bucket != null)
        {
            GameObject hole = new GameObject("BucketHole");
            hole.transform.SetParent(bucket.transform, false);
            hole.transform.localPosition = holeLocalOffset;
            holeTransform = hole.transform;
        }
    }

    public void Emit(float dt)
    {
        if (solver == null || bucket == null || dt <= 0f || paintVolume <= 0f)
            return;

        float holeArea = Mathf.PI * holeRadius * holeRadius;
        float flowMultiplier = Mathf.Lerp(1f, 0.4f, Mathf.Clamp01(paintViscosity));
        float volumePerSecond = flowRate * holeArea * flowMultiplier;
        float particlesPerSecond = volumePerSecond / Mathf.Max(0.000001f, particleVolume);

        emissionAccumulator += particlesPerSecond * dt;
        int emitCount = Mathf.FloorToInt(emissionAccumulator);
        emissionAccumulator -= emitCount;

        if (emitCount <= 0)
            return;

        for (int i = 0; i < emitCount && paintVolume > 0f; i++)
        {
            Vector3 spawnPosition = GetHoleWorldPosition();
            Vector3 baseVelocity = bucket.GetBucketVelocity();
            Vector3 downward = Vector3.down * particleInitialSpeed;
            Vector3 spread = new Vector3(
                Random.Range(-spraySpread, spraySpread),
                Random.Range(-spraySpread * 0.5f, spraySpread * 0.2f),
                Random.Range(-spraySpread, spraySpread));

            solver.AddParticle(spawnPosition, baseVelocity + downward + spread, color);
            paintVolume -= particleVolume;
        }
    }

    public Vector3 GetHoleWorldPosition()
    {
        if (holeTransform != null)
            return holeTransform.position;

        if (bucket != null)
            return bucket.transform.TransformPoint(holeLocalOffset);

        return transform.position;
    }
}

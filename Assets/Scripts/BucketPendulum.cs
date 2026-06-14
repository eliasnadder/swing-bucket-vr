using UnityEngine;

public class BucketPendulum : MonoBehaviour
{
    [Header("Pendulum")]
    public float ropeLength = 1.0f;
    public float gravity = 9.81f;
    public float damping = 0.02f;
    public float initialAngle = 0f;
    public float angularVelocity = 0f;
    public Transform pivotPoint;
    public bool autoCreatePivot = true;

    [Header("Visual")]
    public bool rotateBucketToMotion = true;
    public bool simulateAutomatically = false;

    public float Angle { get; private set; }
    public Vector3 BucketVelocity { get; private set; }
    public float EffectiveGravity => gravity;

    private Vector3 previousBucketPosition;
    private bool initialized;

    private void Start()
    {
        Initialize();
    }

    private void FixedUpdate()
    {
        if (simulateAutomatically)
            Step(Time.fixedDeltaTime);
    }

    public void Initialize()
    {
        ropeLength = Mathf.Max(0.01f, ropeLength);
        Angle = initialAngle * Mathf.Deg2Rad;
        Vector3 offset = GetOffsetFromAngle();

        if (pivotPoint == null && autoCreatePivot)
        {
            GameObject pivot = new GameObject("PendulumPivot");
            pivot.transform.position = transform.position - offset;
            pivotPoint = pivot.transform;
        }

        if (pivotPoint == null)
            pivotPoint = transform.parent != null ? transform.parent : transform;

        transform.position = pivotPoint.position + offset;

        previousBucketPosition = transform.position;
        BucketVelocity = Vector3.zero;
        initialized = true;
    }

    public void Step(float dt)
    {
        if (!initialized)
            Initialize();

        if (dt <= 0f || pivotPoint == null)
            return;

        float angularAcceleration = -(gravity / ropeLength) * Mathf.Sin(Angle) - damping * angularVelocity;
        angularVelocity += angularAcceleration * dt;
        Angle += angularVelocity * dt;

        Vector3 newPosition = GetBucketPosition();
        BucketVelocity = (newPosition - previousBucketPosition) / dt;
        previousBucketPosition = newPosition;

        transform.position = newPosition;
        if (rotateBucketToMotion)
            transform.rotation = Quaternion.Euler(0f, 0f, Angle * Mathf.Rad2Deg);
    }

    public Vector3 GetBucketPosition()
    {
        if (pivotPoint == null)
            return transform.position;

        return pivotPoint.position + GetOffsetFromAngle();
    }

    public Vector3 GetBucketVelocity()
    {
        return BucketVelocity;
    }

    public void UpdateBucketMass(float lostMass)
    {
        // The lightweight pendulum model does not vary mass, but the method
        // is kept for compatibility with the older bucket code.
    }

    private Vector3 GetOffsetFromAngle()
    {
        float x = ropeLength * Mathf.Sin(Angle);
        float y = -ropeLength * Mathf.Cos(Angle);
        return new Vector3(x, y, 0f);
    }
}

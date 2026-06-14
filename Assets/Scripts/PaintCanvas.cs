using System.Collections.Generic;
using UnityEngine;

public class PaintCanvas : MonoBehaviour
{
    public enum CanvasPlane
    {
        XZ,
        XY,
        YZ
    }

    [Header("Texture")]
    public Renderer targetRenderer;
    public int textureWidth = 1024;
    public int textureHeight = 1024;
    public Color backgroundColor = Color.white;

    [Header("World Space Size")]
    public Vector2 worldSize = new Vector2(4f, 4f);
    public CanvasPlane plane = CanvasPlane.XZ;

    [Header("Paint")]
    public float paintOpacity = 0.75f;
    public float defaultSplatRadiusWorld = 0.03f;
    public float viscosityRadiusScale = 0.15f;
    public float contactThickness = 0.03f;

    private Texture2D paintTexture;
    private Color[] pixelBuffer;
    private bool dirty;

    private struct PaintStamp
    {
        public Vector3 worldPosition;
        public Color color;
        public float radiusWorld;
        public float viscosity;
        public Vector3 impactVelocity;
    }

    private readonly List<PaintStamp> pendingStamps = new List<PaintStamp>(1024);

    public Texture2D GetPaintTexture() => paintTexture;

    private void Awake()
    {
        if (targetRenderer == null)
            targetRenderer = GetComponent<Renderer>();
    }

    private void Start()
    {
        InitializeTexture();
    }

    private void OnValidate()
    {
        textureWidth = Mathf.Max(8, textureWidth);
        textureHeight = Mathf.Max(8, textureHeight);
        worldSize.x = Mathf.Max(0.01f, worldSize.x);
        worldSize.y = Mathf.Max(0.01f, worldSize.y);
    }

    private void InitializeTexture()
    {
        if (paintTexture != null)
            return;

        paintTexture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
        paintTexture.wrapMode = TextureWrapMode.Clamp;
        paintTexture.filterMode = FilterMode.Bilinear;

        pixelBuffer = new Color[textureWidth * textureHeight];
        for (int i = 0; i < pixelBuffer.Length; i++)
            pixelBuffer[i] = backgroundColor;

        paintTexture.SetPixels(pixelBuffer);
        paintTexture.Apply(false, false);

        if (targetRenderer != null)
            targetRenderer.material.mainTexture = paintTexture;
    }

    public void ClearCanvas()
    {
        if (pixelBuffer == null)
            return;

        for (int i = 0; i < pixelBuffer.Length; i++)
            pixelBuffer[i] = backgroundColor;

        dirty = true;
    }

    public void QueueSplat(Vector3 worldPosition, Color color, float radiusWorld, float viscosity, Vector3 impactVelocity)
    {
        if (paintTexture == null)
            InitializeTexture();

        pendingStamps.Add(new PaintStamp
        {
            worldPosition = worldPosition,
            color = color,
            radiusWorld = radiusWorld,
            viscosity = viscosity,
            impactVelocity = impactVelocity
        });
    }

    public bool TryWorldToPixel(Vector3 worldPoint, out Vector2Int pixel, out float signedDistance)
    {
        pixel = default;
        signedDistance = 0f;

        Vector3 local = transform.InverseTransformPoint(worldPoint);
        float u;
        float v;

        switch (plane)
        {
            case CanvasPlane.XY:
                u = (local.x / worldSize.x) + 0.5f;
                v = (local.y / worldSize.y) + 0.5f;
                signedDistance = local.z;
                break;
            case CanvasPlane.YZ:
                u = (local.y / worldSize.x) + 0.5f;
                v = (local.z / worldSize.y) + 0.5f;
                signedDistance = local.x;
                break;
            default:
                u = (local.x / worldSize.x) + 0.5f;
                v = (local.z / worldSize.y) + 0.5f;
                signedDistance = local.y;
                break;
        }

        if (u < 0f || u > 1f || v < 0f || v > 1f)
            return false;

        pixel = new Vector2Int(
            Mathf.Clamp(Mathf.RoundToInt(u * (textureWidth - 1)), 0, textureWidth - 1),
            Mathf.Clamp(Mathf.RoundToInt(v * (textureHeight - 1)), 0, textureHeight - 1));
        return true;
    }

    public bool IsWorldPointOnCanvas(Vector3 worldPoint)
    {
        return TryWorldToPixel(worldPoint, out _, out float distance) && Mathf.Abs(distance) <= contactThickness;
    }

    // Compatibility helpers for older scripts.
    public bool CheckCollision(Vector3 worldPoint) => IsWorldPointOnCanvas(worldPoint);

    public void PaintAt(Vector3 worldPoint, Vector3 impactVelocity, Color paintColor)
    {
        QueueSplat(worldPoint, paintColor, defaultSplatRadiusWorld, 0f, impactVelocity);
    }

    private void LateUpdate()
    {
        FlushPending();
    }

    public void FlushPending()
    {
        if (pendingStamps.Count > 0)
            ProcessPendingStamps();

        if (dirty && paintTexture != null)
        {
            paintTexture.SetPixels(pixelBuffer);
            paintTexture.Apply(false, false);
            dirty = false;
        }
    }

    private void ProcessPendingStamps()
    {
        for (int i = 0; i < pendingStamps.Count; i++)
        {
            PaintStamp stamp = pendingStamps[i];
            ApplySplat(stamp.worldPosition, stamp.color, stamp.radiusWorld, stamp.viscosity, stamp.impactVelocity);
        }

        pendingStamps.Clear();
    }

    private void ApplySplat(Vector3 worldPosition, Color color, float radiusWorld, float viscosity, Vector3 impactVelocity)
    {
        if (!TryWorldToPixel(worldPosition, out Vector2Int center, out float signedDistance))
            return;

        float radiusPx = Mathf.Max(1f, WorldRadiusToPixels(radiusWorld));
        radiusPx *= 1f + viscosity * viscosityRadiusScale;
        radiusPx *= 1f + Mathf.Clamp01(impactVelocity.magnitude) * 0.15f;

        int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radiusPx));
        int maxX = Mathf.Min(textureWidth - 1, Mathf.CeilToInt(center.x + radiusPx));
        int minY = Mathf.Max(0, Mathf.FloorToInt(center.y - radiusPx));
        int maxY = Mathf.Min(textureHeight - 1, Mathf.CeilToInt(center.y + radiusPx));

        float radiusSq = radiusPx * radiusPx;
        float paintStrength = paintOpacity * Mathf.Clamp01(1f - Mathf.Abs(signedDistance) / Mathf.Max(contactThickness, 0.0001f));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dx = x - center.x;
                float dy = y - center.y;
                float distSq = dx * dx + dy * dy;
                if (distSq > radiusSq)
                    continue;

                float falloff = Mathf.Exp(-(distSq / Mathf.Max(1f, radiusSq)) * 2.2f);
                float blend = Mathf.Clamp01(falloff * paintStrength);
                int index = y * textureWidth + x;
                pixelBuffer[index] = Color.Lerp(pixelBuffer[index], color, blend);
            }
        }

        dirty = true;
    }

    private float WorldRadiusToPixels(float radiusWorld)
    {
        float worldSpan = Mathf.Max(0.0001f, Mathf.Min(worldSize.x, worldSize.y));
        return radiusWorld / Mathf.Max(0.0001f, worldSpan) * textureWidth;
    }
}

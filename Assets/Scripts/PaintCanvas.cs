using System.Collections.Generic;
using UnityEngine;

public class PaintCanvas : MonoBehaviour
{
    public enum CanvasPlane { XZ, XY, YZ }
    public enum SurfaceType { Canvas, Wood, Metal, Paper }

    private static readonly float[,] SurfaceTable =
    {
        /* α Absorption */ { 0.75f, 0.48f, 0.90f, 0.15f },
        /* μ Friction    */ { 0.85f, 0.68f, 0.15f, 0.75f },
        /* Roughness     */ { 0.07f, 0.20f, 0.70f, 0.05f },
    };

    int   SurfaceIdx => (int)surfaceType;
    float Absorption => SurfaceTable[0, SurfaceIdx];
    float Friction   => SurfaceTable[1, SurfaceIdx];

    [Header("Texture")]
    public Renderer targetRenderer;
    public int textureWidth  = 1024;
    public int textureHeight = 1024;
    public Color backgroundColor = Color.white;

    [Header("World Space Size")]
    [Tooltip("حجم اللوحة بوحدات Unity في المستوى المحدد (X=عرض, Y=عمق/طول)")]
    public Vector2 worldSize = new Vector2(4f, 4f);
    public CanvasPlane plane = CanvasPlane.XZ;

    [Header("UV Mapping")]
    [Tooltip("اعكس U أفقياً")]
    public bool flipU = false;
    [Tooltip("اعكس V عمودياً")]
    public bool flipV = false;
    [Tooltip("بدّل U↔V — استخدم إذا كان الرسم في الاتجاه الخاطئ")]
    public bool swapUV = false;

    [Header("Surface Properties")]
    public SurfaceType surfaceType = SurfaceType.Canvas;
    [Range(0f, 90f)] public float tiltAngle = 0f;

    [Header("Weber Model")]
    public float surfaceTension    = 0.07f;
    public float particleDiameter  = 0.01f;
    public float weberRadiusScale  = 0.002f;
    public float sigma             = 0.05f;

    [Header("Paint")]
    public float paintOpacity           = 0.85f;
    public float defaultSplatRadiusWorld = 8f;
    public float viscosityRadiusScale   = 0.15f;
    [Tooltip("نصف سماكة التلاصق — بوحدات Unity")]
    public float contactThickness = 5f;

    // ── Private ──
    private Texture2D paintTexture;
    private Color[]   pixelBuffer;
    private bool      dirty;
    private Bounds    cachedBounds;       // computed once per frame in LateUpdate
    private bool      boundsReady;

    private struct PaintStamp
    {
        public Vector3 worldPosition;
        public Color   color;
        public float   radiusWorld;
        public float   viscosity;
        public Vector3 impactVelocity;
    }

    private readonly List<PaintStamp> pendingStamps = new List<PaintStamp>(1024);

    // ── Public API ──
    public Texture2D GetPaintTexture() => paintTexture;

    // ─────────────────────────────────────────
    private void Awake()
    {
        if (targetRenderer == null)
            targetRenderer = GetComponent<Renderer>();
    }

    private void Start()
    {
        RefreshBounds();
        InitializeTexture();
    }

    private void OnValidate()
    {
        textureWidth  = Mathf.Max(8, textureWidth);
        textureHeight = Mathf.Max(8, textureHeight);
        worldSize.x   = Mathf.Max(0.01f, worldSize.x);
        worldSize.y   = Mathf.Max(0.01f, worldSize.y);
    }

    // ── Bounds helper ──────────────────────────────────
    /// <summary>
    /// Returns a flat 2D bounding box (only the 2 axes used by the active CanvasPlane).
    /// Uses renderer.bounds when available, falls back to worldSize.
    /// </summary>
    private void RefreshBounds()
    {
        if (targetRenderer != null)
        {
            Bounds rb = targetRenderer.bounds;
            // For XZ: centre = (cx, cy, cz), size built from X and Z extents only.
            // We ignore Y so a thick Cube doesn't corrupt the mapping.
            Vector3 c = rb.center;
            switch (plane)
            {
                case CanvasPlane.XY:
                    cachedBounds = new Bounds(c, new Vector3(rb.size.x, rb.size.y, 1f));
                    break;
                case CanvasPlane.YZ:
                    cachedBounds = new Bounds(c, new Vector3(1f, rb.size.y, rb.size.z));
                    break;
                default: // XZ
                    cachedBounds = new Bounds(c, new Vector3(rb.size.x, 1f, rb.size.z));
                    break;
            }
        }
        else
        {
            // Manual fallback: use worldSize centred on transform.position
            Vector3 c = transform.position;
            switch (plane)
            {
                case CanvasPlane.XY:
                    cachedBounds = new Bounds(c, new Vector3(worldSize.x, worldSize.y, 1f));
                    break;
                case CanvasPlane.YZ:
                    cachedBounds = new Bounds(c, new Vector3(1f, worldSize.x, worldSize.y));
                    break;
                default: // XZ
                    cachedBounds = new Bounds(c, new Vector3(worldSize.x, 1f, worldSize.y));
                    break;
            }
        }
        boundsReady = true;
    }

    // ── Texture ────────────────────────────────────────
    private void InitializeTexture()
    {
        if (paintTexture != null) return;

        paintTexture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false)
        {
            wrapMode   = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        pixelBuffer = new Color[textureWidth * textureHeight];
        for (int i = 0; i < pixelBuffer.Length; i++)
            pixelBuffer[i] = backgroundColor;

        paintTexture.SetPixels(pixelBuffer);
        paintTexture.Apply(false, false);

        if (targetRenderer != null)
        {
            Material mat = targetRenderer.material;
            mat.mainTexture = paintTexture;
            if (mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", paintTexture);
        }
    }

    public void ClearCanvas()
    {
        if (pixelBuffer == null) return;
        for (int i = 0; i < pixelBuffer.Length; i++)
            pixelBuffer[i] = backgroundColor;
        dirty = true;
    }

    // ── Painting ───────────────────────────────────────
    public void QueueSplat(Vector3 worldPosition, Color color, float radiusWorld, float viscosity, Vector3 impactVelocity)
    {
        if (paintTexture == null) InitializeTexture();
        pendingStamps.Add(new PaintStamp
        {
            worldPosition = worldPosition,
            color         = color,
            radiusWorld   = radiusWorld,
            viscosity     = viscosity,
            impactVelocity = impactVelocity
        });
    }

    /// <summary>
    /// Converts a world point to a texture pixel using the correct plane bounds.
    /// Returns false if the point is outside the canvas XZ/XY/YZ footprint.
    /// signedDistance = signed distance from the canvas plane surface.
    /// </summary>
    public bool TryWorldToPixel(Vector3 worldPoint, out Vector2Int pixel, out float signedDistance)
    {
        pixel         = default;
        signedDistance = 0f;

        if (!boundsReady) RefreshBounds();
        Bounds b = cachedBounds;

        float u, v;

        switch (plane)
        {
            case CanvasPlane.XY:
                if (b.size.x < 0.0001f || b.size.y < 0.0001f) return false;
                u             = (worldPoint.x - b.min.x) / b.size.x;
                v             = (worldPoint.y - b.min.y) / b.size.y;
                signedDistance = worldPoint.z - transform.position.z;
                break;

            case CanvasPlane.YZ:
                if (b.size.y < 0.0001f || b.size.z < 0.0001f) return false;
                u             = (worldPoint.y - b.min.y) / b.size.y;
                v             = (worldPoint.z - b.min.z) / b.size.z;
                signedDistance = worldPoint.x - transform.position.x;
                break;

            default: // XZ — horizontal canvas (floor/table)
                if (b.size.x < 0.0001f || b.size.z < 0.0001f) return false;
                u             = (worldPoint.x - b.min.x) / b.size.x;
                v             = (worldPoint.z - b.min.z) / b.size.z;
                signedDistance = worldPoint.y - transform.position.y;
                break;
        }

        if (u < 0f || u > 1f || v < 0f || v > 1f) return false;

        if (swapUV) { float t = u; u = v; v = t; }
        if (flipU)  u = 1f - u;
        if (flipV)  v = 1f - v;

        pixel = new Vector2Int(
            Mathf.Clamp(Mathf.RoundToInt(u * (textureWidth  - 1)), 0, textureWidth  - 1),
            Mathf.Clamp(Mathf.RoundToInt(v * (textureHeight - 1)), 0, textureHeight - 1));
        return true;
    }

    public bool IsWorldPointOnCanvas(Vector3 worldPoint)
        => TryWorldToPixel(worldPoint, out _, out float d) && Mathf.Abs(d) <= contactThickness;

    public bool CheckCollision(Vector3 worldPoint) => IsWorldPointOnCanvas(worldPoint);

    public void PaintAt(Vector3 worldPoint, Vector3 impactVelocity, Color paintColor)
        => QueueSplat(worldPoint, paintColor, defaultSplatRadiusWorld, 0f, impactVelocity);

    // ── Update loop ────────────────────────────────────
    private void LateUpdate()
    {
        RefreshBounds();   // refresh every frame so moving canvas works
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
            PaintStamp s = pendingStamps[i];
            ApplySplat(s.worldPosition, s.color, s.radiusWorld, s.viscosity, s.impactVelocity);
        }
        pendingStamps.Clear();
    }

    private void ApplySplat(Vector3 worldPosition, Color color, float radiusWorld, float viscosity, Vector3 impactVelocity)
    {
        if (!TryWorldToPixel(worldPosition, out Vector2Int center, out float signedDistance))
            return;

        float speed = impactVelocity.magnitude;

        // Weber model
        float We            = 1200f * speed * speed * particleDiameter / Mathf.Max(0.0001f, surfaceTension);
        float effectiveSigma = sigma * (1f + weberRadiusScale * We) * Absorption;
        float impactEnergy  = speed * Absorption;

        float radiusPx;
        if (radiusWorld > 0f)
        {
            radiusPx  = Mathf.Max(1f, WorldRadiusToPixels(radiusWorld));
            radiusPx *= 1f + viscosity * viscosityRadiusScale;
            radiusPx *= 1f + Mathf.Clamp01(speed) * 0.15f;
        }
        else
        {
            radiusPx = Mathf.Clamp(
                Mathf.RoundToInt(effectiveSigma * textureWidth * Mathf.Max(impactEnergy, 0.1f)),
                2, textureWidth / 4);
        }

        // Humidity spread
        if (SPHFluidSolver.Instance != null)
            radiusPx *= SPHFluidSolver.Instance.HumiditySpreadFactor;

        // Tilt shift
        float tiltRad  = tiltAngle * Mathf.Deg2Rad;
        int   tiltShift = Mathf.RoundToInt(Mathf.Sin(tiltRad) * radiusPx * Friction);

        int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radiusPx));
        int maxX = Mathf.Min(textureWidth  - 1, Mathf.CeilToInt(center.x + radiusPx));
        int minY = Mathf.Max(0, Mathf.FloorToInt(center.y - radiusPx + tiltShift));
        int maxY = Mathf.Min(textureHeight - 1, Mathf.CeilToInt(center.y + radiusPx + tiltShift));

        float radiusSq = radiusPx * radiusPx;
        float strength = paintOpacity * Absorption
            * Mathf.Clamp01(1f - Mathf.Abs(signedDistance) / Mathf.Max(contactThickness, 0.0001f));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dx = x - center.x;
                float dy = y - (center.y + tiltShift);
                float dq = dx * dx + dy * dy;
                if (dq > radiusSq) continue;

                float falloff = Mathf.Exp(-(dq / Mathf.Max(1f, radiusSq)) * 2.2f);
                float blend   = Mathf.Clamp01(falloff * strength);
                int   idx     = y * textureWidth + x;
                pixelBuffer[idx] = Color.Lerp(pixelBuffer[idx], color, blend);
            }
        }

        dirty = true;
    }

    private float WorldRadiusToPixels(float radiusWorld)
    {
        if (!boundsReady) RefreshBounds();
        Bounds b = cachedBounds;
        float span = plane switch
        {
            CanvasPlane.XY => Mathf.Min(b.size.x, b.size.y),
            CanvasPlane.YZ => Mathf.Min(b.size.y, b.size.z),
            _              => Mathf.Min(b.size.x, b.size.z),
        };
        return radiusWorld / Mathf.Max(0.0001f, span) * textureWidth;
    }

    // ── Editor helpers ─────────────────────────────────
#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        RefreshBounds();
        Bounds b = cachedBounds;
        Gizmos.color = new Color(0f, 1f, 0.4f, 0.35f);
        Gizmos.DrawCube(b.center, b.size + Vector3.up * 0.1f);
        Gizmos.color = new Color(0f, 1f, 0.4f, 0.9f);
        Gizmos.DrawWireCube(b.center, b.size + Vector3.up * 0.1f);

        // Draw bucket projection point if found
        var pendulum = FindAnyObjectByType<SwingingCoupledSpringPendulum>();
        if (pendulum != null)
        {
            Vector3 proj = pendulum.transform.position;
            proj.y = transform.position.y;
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(proj, b.size.x * 0.01f);
            Gizmos.DrawLine(pendulum.transform.position, proj);
        }
    }
#endif
}

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
        // نستخدم transform.position دائماً كـ center للـ bounds لضمان الدقة
        // حتى عند وجود renderer، لأن renderer.bounds.center قد يختلف بسبب Scale
        Vector3 c = transform.position;

        float sizeX, sizeZ;

        if (targetRenderer != null)
        {
            // نأخذ الحجم من renderer لكن المركز من transform
            Bounds rb = targetRenderer.bounds;
            sizeX = rb.size.x;
            sizeZ = rb.size.z;
        }
        else
        {
            sizeX = worldSize.x;
            sizeZ = worldSize.y;
        }

        switch (plane)
        {
            case CanvasPlane.XY:
                cachedBounds = new Bounds(c, new Vector3(sizeX, sizeZ, 1f));
                break;
            case CanvasPlane.YZ:
                cachedBounds = new Bounds(c, new Vector3(1f, sizeX, sizeZ));
                break;
            default: // XZ
                cachedBounds = new Bounds(c, new Vector3(sizeX, 1f, sizeZ));
                break;
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

        // ── يستخدم transform.localScale مباشرة لحساب الحجم الفعلي ──
        // هذا يعمل بغض النظر عن worldSize في Inspector
        Vector3 cp = transform.position;

        // للـ Quad/Plane الافتراضي: local size = 1×1، world size = scale.x × scale.z
        // للـ Cube: local size = 1×1×1، world size = scale.x × scale.z
        float actualSizeX = Mathf.Abs(transform.lossyScale.x);
        float actualSizeZ = Mathf.Abs(transform.lossyScale.z);

        // إذا كان Quad (Unity built-in) أو Plane، يجب ضرب scale في mesh size
        // Quad local = 1×1، Plane local = 10×10
        // نتحقق من worldSize كـ override إذا كان مختلفاً
        float sizeX = worldSize.x > 0.1f ? worldSize.x : actualSizeX;
        float sizeZ = worldSize.y > 0.1f ? worldSize.y : actualSizeZ;

        float halfX = sizeX * 0.5f;
        float halfZ = sizeZ * 0.5f;

        float u, v;

        switch (plane)
        {
            case CanvasPlane.XY:
                u             = (worldPoint.x - (cp.x - halfX)) / sizeX;
                v             = (worldPoint.y - (cp.y - halfZ)) / sizeZ;
                signedDistance = worldPoint.z - cp.z;
                break;
            case CanvasPlane.YZ:
                u             = (worldPoint.y - (cp.y - halfX)) / sizeX;
                v             = (worldPoint.z - (cp.z - halfZ)) / sizeZ;
                signedDistance = worldPoint.x - cp.x;
                break;
            default: // XZ
                u             = (worldPoint.x - (cp.x - halfX)) / sizeX;
                v             = (worldPoint.z - (cp.z - halfZ)) / sizeZ;
                signedDistance = worldPoint.y - cp.y;
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

        // DEBUG
#if UNITY_EDITOR
        Debug.Log($"[SPLAT] worldPos=({worldPosition.x:F1},{worldPosition.z:F1}) pixel=({center.x},{center.y}) " +
                  $"canvas=({transform.position.x:F1},{transform.position.z:F1}) scale=({transform.lossyScale.x:F1},{transform.lossyScale.z:F1})");
#endif

        float impactSpeed = Mathf.Abs(impactVelocity.y);

        // ── مطابق index.html تماماً: radius = size * 25 + impactSpeed * 1.5 ──
        // نسبة المقياس: index.html يستخدم canvas=20 وحدة/1024px
        // نطبّق نفس المعادلة مع تصحيح النسبة بين المشهدين
        float canvasSpan  = boundsReady && cachedBounds.size.x > 0.001f ? cachedBounds.size.x : 150f;
        float scaleRatio  = 20f / canvasSpan;
        float sizePx      = radiusWorld * scaleRatio * 25f;
        // نقسم impactSpeed على نسبة الارتفاع لأن السطل في Unity أعلى بكثير من index.html
        float scaledImpact = impactSpeed * (20f / canvasSpan) * 1.5f;
        float radiusPx    = Mathf.Max(sizePx, 0.5f) + scaledImpact;
        radiusPx          = Mathf.Clamp(radiusPx, 1f, textureWidth * 0.1f);

        int tiltShiftInt = Mathf.RoundToInt(
            Mathf.Sin(tiltAngle * Mathf.Deg2Rad) * radiusPx * Friction);

        // بقعة رئيسية بـ radial gradient
        DrawGradientCircle(center.x, center.y + tiltShiftInt, radiusPx, color, 1f);

        // Droplets عند السرعة العالية — مطابق index.html
        if (impactSpeed > 3f)
        {
            int droplets = Mathf.Min(Mathf.FloorToInt(impactSpeed / 2f), 12);
            for (int d = 0; d < droplets; d++)
            {
                float angle   = Random.value * Mathf.PI * 2f;
                float dist    = Random.value * radiusPx * 2f;
                int   offX    = Mathf.RoundToInt(Mathf.Cos(angle) * dist);
                int   offY    = Mathf.RoundToInt(Mathf.Sin(angle) * dist);
                float dropRad = Random.value * (radiusPx / 5f);
                DrawGradientCircle(
                    center.x + offX,
                    center.y + tiltShiftInt + offY,
                    dropRad,
                    color,
                    0.67f);
            }
        }

        dirty = true;
    }

    /// <summary>
    /// يرسم دائرة بـ radial gradient (كثيف في المركز → شفاف في الحافة)
    /// مطابق لـ createRadialGradient في index.html
    /// </summary>
    private void DrawGradientCircle(int cx, int cy, float radiusPx, Color color, float alphaMultiplier)
    {
        if (radiusPx < 0.5f) return;

        int minX = Mathf.Max(0, Mathf.FloorToInt(cx - radiusPx));
        int maxX = Mathf.Min(textureWidth  - 1, Mathf.CeilToInt(cx + radiusPx));
        int minY = Mathf.Max(0, Mathf.FloorToInt(cy - radiusPx));
        int maxY = Mathf.Min(textureHeight - 1, Mathf.CeilToInt(cy + radiusPx));

        float radiusSq = radiusPx * radiusPx;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float distSq = dx * dx + dy * dy;
                if (distSq > radiusSq) continue;

                float t = Mathf.Sqrt(distSq) / radiusPx; // 0=مركز, 1=حافة

                // gradient: stop 0→full color, stop 0.6→50% alpha, stop 1→0 alpha
                // مطابق: addColorStop(0, hex), addColorStop(0.6, hex+'80'), addColorStop(1, hex+'00')
                float alpha;
                if (t <= 0.6f)
                    alpha = Mathf.Lerp(1f, 0.5f, t / 0.6f);
                else
                    alpha = Mathf.Lerp(0.5f, 0f, (t - 0.6f) / 0.4f);

                alpha *= paintOpacity * alphaMultiplier;
                alpha  = Mathf.Clamp01(alpha);

                int idx = y * textureWidth + x;
                pixelBuffer[idx] = Color.Lerp(pixelBuffer[idx], color, alpha);
            }
        }
    }

    private float WorldRadiusToPixels(float radiusWorld)
    {
        if (!boundsReady) RefreshBounds();
        Bounds b = cachedBounds;

        // استخدم المحور الأكبر كمرجع للتحويل (مطابق منطق index.html)
        float span = plane switch
        {
            CanvasPlane.XY => Mathf.Max(b.size.x, b.size.y),
            CanvasPlane.YZ => Mathf.Max(b.size.y, b.size.z),
            _              => Mathf.Max(b.size.x, b.size.z),
        };

        // index.html: radius = size * 25، حيث size بوحدات المشهد
        // Unity: نحوّل radiusWorld → pixels بنفس النسبة
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

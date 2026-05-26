using UnityEngine;

/// <summary>
/// Procedurally builds a bucket from Unity primitives at runtime and
/// animates the paint surface level based on FluidSPHSystem.
///
/// Usage:
///   1. Attach this script to the same GameObject that has
///      SwingingCoupledSpringPendulum (i.e. the "bucket" object).
///   2. Assign fluidSystem in the Inspector (or it will be found automatically).
///   3. Press Play — the bucket is built automatically.
/// </summary>
[RequireComponent(typeof(SwingingCoupledSpringPendulum))]
public class BucketBuilder : MonoBehaviour
{
    [Header("References")]
    public FluidSPHSystem fluidSystem;

    [Header("Bucket Dimensions")]
    [Tooltip("Radius at the bottom of the bucket")]
    public float bottomRadius = 0.18f;
    [Tooltip("Radius at the top of the bucket")]
    public float topRadius    = 0.22f;
    [Tooltip("Height of the bucket body")]
    public float bucketHeight = 0.40f;
    [Tooltip("Thickness of the wall/rim (visual only)")]
    public float wallThickness = 0.015f;

    [Header("Paint")]
    [Tooltip("Max paint height in FluidSPHSystem (should match initialVolume-derived h_paint)")]
    public float maxPaintHeight = 0.30f;

    // ── runtime refs ──
    private Transform paintSurface;
    private Renderer  paintRenderer;
    private float     paintFullLocalY;
    private float     paintEmptyLocalY;

    // ── materials ──
    private Material metalMat;
    private Material paintMat;

    // ─────────────────────────────────────────────────────────────
    void Start()
    {
        if (fluidSystem == null)
            fluidSystem = FindObjectOfType<FluidSPHSystem>();

        BuildMaterials();
        BuildBucket();
    }

    // ─────────────────────────────────────────────────────────────
    void Update()
    {
        if (fluidSystem == null || paintSurface == null) return;

        // Drive paint level
        float t = Mathf.Clamp01(fluidSystem.h_paint / Mathf.Max(0.001f, maxPaintHeight));
        Vector3 lp = paintSurface.localPosition;
        lp.y = Mathf.Lerp(paintEmptyLocalY, paintFullLocalY, t);
        paintSurface.localPosition = lp;

        // Sync paint colour + alpha
        Color c = fluidSystem.currentPaintColor;
        c.a = 0.88f;
        paintMat.color = c;
    }

    // ─────────────────────────────────────────────────────────────
    void BuildMaterials()
    {
        // Metal — opaque, slightly reflective grey
        metalMat = new Material(Shader.Find("Standard"));
        metalMat.color = new Color(0.72f, 0.72f, 0.72f);
        metalMat.SetFloat("_Metallic",   0.75f);
        metalMat.SetFloat("_Glossiness", 0.55f);

        // Paint — transparent, glossy
        paintMat = new Material(Shader.Find("Standard"));
        paintMat.color = Color.red;
        // Enable transparency
        paintMat.SetFloat("_Mode", 3);                          // Transparent
        paintMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        paintMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        paintMat.SetInt("_ZWrite", 0);
        paintMat.DisableKeyword("_ALPHATEST_ON");
        paintMat.EnableKeyword("_ALPHABLEND_ON");
        paintMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        paintMat.renderQueue = 3000;
        paintMat.SetFloat("_Glossiness", 0.80f);
        paintMat.SetFloat("_Metallic",   0.0f);
    }

    // ─────────────────────────────────────────────────────────────
    void BuildBucket()
    {
        // ── 1. BODY ──────────────────────────────────────────────
        // Unity cylinders can't be tapered, so we approximate with
        // a cylinder whose radius is the average of top/bottom.
        float avgRadius = (bottomRadius + topRadius) * 0.5f;

        GameObject body = CreateCylinder("BucketBody", metalMat);
        body.transform.SetParent(transform, false);
        body.transform.localPosition = Vector3.zero;
        // Cylinder primitive has height 2 in local space → scale Y = height/2
        body.transform.localScale = new Vector3(
            avgRadius * 2f,
            bucketHeight * 0.5f,
            avgRadius * 2f);

        // ── 2. BOTTOM ────────────────────────────────────────────
        GameObject bottom = CreateCylinder("BucketBottom", metalMat);
        bottom.transform.SetParent(transform, false);
        bottom.transform.localPosition = new Vector3(0f, -bucketHeight * 0.5f, 0f);
        bottom.transform.localScale = new Vector3(
            bottomRadius * 2f,
            wallThickness,
            bottomRadius * 2f);

        // ── 3. RIM ───────────────────────────────────────────────
        GameObject rim = CreateCylinder("BucketRim", metalMat);
        rim.transform.SetParent(transform, false);
        rim.transform.localPosition = new Vector3(0f, bucketHeight * 0.5f, 0f);
        rim.transform.localScale = new Vector3(
            (topRadius + wallThickness) * 2f,
            wallThickness,
            (topRadius + wallThickness) * 2f);

        // ── 4. HANDLE ────────────────────────────────────────────
        // Two vertical posts + one arc approximated by a rotated capsule
        float handleHeight = bucketHeight * 0.55f;
        float handleSpan   = topRadius * 1.6f;

        // Left post
        GameObject postL = CreateCapsule("HandlePostL", metalMat);
        postL.transform.SetParent(transform, false);
        postL.transform.localPosition = new Vector3(-topRadius * 0.85f,
                                                     bucketHeight * 0.5f + handleHeight * 0.5f,
                                                     0f);
        postL.transform.localScale = new Vector3(wallThickness * 1.5f,
                                                  handleHeight * 0.5f,
                                                  wallThickness * 1.5f);

        // Right post
        GameObject postR = CreateCapsule("HandlePostR", metalMat);
        postR.transform.SetParent(transform, false);
        postR.transform.localPosition = new Vector3( topRadius * 0.85f,
                                                     bucketHeight * 0.5f + handleHeight * 0.5f,
                                                     0f);
        postR.transform.localScale = postL.transform.localScale;

        // Arc (capsule rotated 90° on Z)
        GameObject arc = CreateCapsule("HandleArc", metalMat);
        arc.transform.SetParent(transform, false);
        arc.transform.localPosition = new Vector3(0f,
                                                   bucketHeight * 0.5f + handleHeight,
                                                   0f);
        arc.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        arc.transform.localScale = new Vector3(wallThickness * 1.5f,
                                                handleSpan * 0.5f,
                                                wallThickness * 1.5f);

        // ── 5. ORIFICE (drip hole) ────────────────────────────────
        GameObject orifice = CreateCylinder("Orifice", MakeDarkMat());
        orifice.transform.SetParent(transform, false);
        orifice.transform.localPosition = new Vector3(0f, -bucketHeight * 0.5f - wallThickness * 1.1f, 0f);
        orifice.transform.localScale = new Vector3(
            fluidSystem != null ? fluidSystem.orificeDiameter : 0.05f,
            wallThickness * 0.5f,
            fluidSystem != null ? fluidSystem.orificeDiameter : 0.05f);

        // ── 6. PAINT SURFACE ─────────────────────────────────────
        GameObject paint = CreateCylinder("PaintSurface", paintMat);
        paint.transform.SetParent(transform, false);

        // Full = just below the rim; Empty = just above the bottom
        paintFullLocalY  =  bucketHeight * 0.5f - wallThickness * 2f;
        paintEmptyLocalY = -bucketHeight * 0.5f + wallThickness * 2f;

        paint.transform.localPosition = new Vector3(0f, paintFullLocalY, 0f);
        paint.transform.localScale = new Vector3(
            (avgRadius - wallThickness) * 2f,
            wallThickness * 0.5f,
            (avgRadius - wallThickness) * 2f);

        paintSurface  = paint.transform;
        paintRenderer = paint.GetComponent<Renderer>();
        paintRenderer.material = paintMat;
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────
    static GameObject CreateCylinder(string name, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.GetComponent<Renderer>().material = mat;
        // Remove collider — physics is handled by the pendulum script
        Destroy(go.GetComponent<Collider>());
        return go;
    }

    static GameObject CreateCapsule(string name, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = name;
        go.GetComponent<Renderer>().material = mat;
        Destroy(go.GetComponent<Collider>());
        return go;
    }

    Material MakeDarkMat()
    {
        Material m = new Material(Shader.Find("Standard"));
        m.color = new Color(0.1f, 0.1f, 0.1f);
        return m;
    }
}

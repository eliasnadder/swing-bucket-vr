using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a more realistic tapered bucket and a visible liquid volume.
/// The paint is no longer a flat disc; it is rendered as a fill volume
/// that rises and shrinks with FluidSPHSystem.h_paint.
/// </summary>
[RequireComponent(typeof(SwingingCoupledSpringPendulum))]
public class BucketBuilder : MonoBehaviour
{
    [Header("References")]
    public FluidSPHSystem fluidSystem;
    [Tooltip("Optional imported bucket prefab/FBX. If assigned, this model is used instead of the procedural shell.")]
    public GameObject bucketModelPrefab;
    [Tooltip("Local position offset for the imported model.")]
    public Vector3 bucketModelLocalPosition = Vector3.zero;
    [Tooltip("Local rotation offset for the imported model.")]
    public Vector3 bucketModelLocalEulerAngles = Vector3.zero;
    [Tooltip("Local scale multiplier for the imported model.")]
    public Vector3 bucketModelLocalScale = Vector3.one;

    [Header("Bucket Dimensions")]
    [Tooltip("Radius at the bottom of the bucket")]
    public float bottomRadius = 0.18f;
    [Tooltip("Radius at the top of the bucket")]
    public float topRadius = 0.22f;
    [Tooltip("Height of the bucket body")]
    public float bucketHeight = 0.40f;
    [Tooltip("Thickness of the wall/rim")]
    public float wallThickness = 0.015f;

    [Header("Paint")]
    [Tooltip("Max paint height in FluidSPHSystem")]
    public float maxPaintHeight = 0.30f;

    [Header("Mesh Quality")]
    [Range(12, 64)]
    public int radialSegments = 28;

    private Material metalMat;
    private Material innerMat;
    private Material paintMat;
    private GameObject bucketModelInstance;
    private bool usingRootMesh;
    private Bounds bucketVisualBoundsLocal;
    private bool hasBucketVisualBounds;

    private MeshFilter outerFilter;
    private MeshFilter innerFilter;
    private MeshFilter bottomFilter;
    private MeshFilter rimFilter;
    private MeshFilter liquidFilter;
    private Transform paintSpawnPoint;

    private Renderer paintRenderer;
    private float liquidBottomLocalY;
    private float liquidTopLocalY;
    private float lastFillT = -1f;
    private Color lastPaintColor = Color.clear;

    void Start()
    {
        if (fluidSystem == null)
            fluidSystem = FindAnyObjectByType<FluidSPHSystem>();

        var existingFilter = GetComponent<MeshFilter>();
        var existingRenderer = GetComponent<MeshRenderer>();
        usingRootMesh = bucketModelPrefab == null &&
                        existingFilter != null &&
                        existingFilter.sharedMesh != null;

        // Only hide the root mesh when we are replacing it with a prefab or
        // procedural geometry. If the imported model is already on Bucket,
        // keep it visible and use it as the bucket body.
        if (!usingRootMesh)
        {
            if (existingFilter != null)
                existingFilter.sharedMesh = null;

            if (existingRenderer != null)
                existingRenderer.enabled = false;
        }

        BuildMaterials();
        if (bucketModelPrefab != null)
            BuildImportedBucket();
        else if (usingRootMesh)
        {
            CaptureBucketVisualBounds(transform);
            BuildImportedLiquidVolume();
        }
        else if (!usingRootMesh)
            BuildBucket();
        UpdateLiquidVisual(true);
    }

    void Update()
    {
        UpdateLiquidVisual(false);
    }

    void BuildMaterials()
    {
        Shader litShader = FindLitShader();

        metalMat = new Material(litShader);
        metalMat.color = new Color(0.70f, 0.72f, 0.74f);
        metalMat.SetFloat("_Metallic", 0.78f);
        metalMat.SetFloat("_Glossiness", 0.58f);

        innerMat = new Material(litShader);
        innerMat.color = new Color(0.10f, 0.10f, 0.10f);
        innerMat.SetFloat("_Metallic", 0.25f);
        innerMat.SetFloat("_Glossiness", 0.20f);

        paintMat = new Material(litShader);
        paintMat.color = new Color(0.90f, 0.05f, 0.05f, 0.94f);
        paintMat.SetFloat("_Mode", 3);
        paintMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        paintMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        paintMat.SetInt("_ZWrite", 0);
        paintMat.DisableKeyword("_ALPHATEST_ON");
        paintMat.EnableKeyword("_ALPHABLEND_ON");
        paintMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        paintMat.renderQueue = 3000;
        paintMat.SetFloat("_Glossiness", 0.72f);
        paintMat.SetFloat("_Metallic", 0.0f);
    }

    void BuildBucket()
    {
        float innerBottomRadius = Mathf.Max(0.01f, bottomRadius);
        float innerTopRadius = Mathf.Max(innerBottomRadius + 0.01f, topRadius);
        float outerBottomRadius = innerBottomRadius + wallThickness;
        float outerTopRadius = innerTopRadius + wallThickness;

        outerFilter = CreateMeshChild(
            "BucketOuter",
            metalMat,
            CreateFrustumWallMesh(outerBottomRadius, outerTopRadius, bucketHeight, radialSegments, false));

        innerFilter = CreateMeshChild(
            "BucketInner",
            innerMat,
            CreateFrustumWallMesh(innerBottomRadius, innerTopRadius, bucketHeight, radialSegments, true));

        bottomFilter = CreateMeshChild(
            "BucketBottom",
            metalMat,
            CreateDiscMesh(innerBottomRadius, radialSegments, true));
        bottomFilter.transform.localPosition = new Vector3(0f, -bucketHeight * 0.5f + wallThickness * 0.5f, 0f);

        rimFilter = CreateMeshChild(
            "BucketRim",
            metalMat,
            CreateFrustumWallMesh(innerTopRadius, outerTopRadius + wallThickness * 0.25f, wallThickness * 1.35f, radialSegments, false));
        rimFilter.transform.localPosition = new Vector3(0f, bucketHeight * 0.5f - wallThickness * 0.18f, 0f);

        BuildHandle();
        BuildOrifice();
        hasBucketVisualBounds = false;
        BuildLiquidVolume(innerBottomRadius, innerTopRadius);
    }

    void BuildHandle()
    {
        float handleHeight = bucketHeight * 0.55f;
        float handleSpan = topRadius * 1.6f;

        GameObject postL = CreatePrimitiveChild("HandlePostL", PrimitiveType.Capsule, metalMat);
        postL.transform.localPosition = new Vector3(-topRadius * 0.85f, bucketHeight * 0.5f + handleHeight * 0.5f, 0f);
        postL.transform.localScale = new Vector3(wallThickness * 1.5f, handleHeight * 0.5f, wallThickness * 1.5f);

        GameObject postR = CreatePrimitiveChild("HandlePostR", PrimitiveType.Capsule, metalMat);
        postR.transform.localPosition = new Vector3(topRadius * 0.85f, bucketHeight * 0.5f + handleHeight * 0.5f, 0f);
        postR.transform.localScale = postL.transform.localScale;

        GameObject arc = CreatePrimitiveChild("HandleArc", PrimitiveType.Capsule, metalMat);
        arc.transform.localPosition = new Vector3(0f, bucketHeight * 0.5f + handleHeight, 0f);
        arc.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        arc.transform.localScale = new Vector3(wallThickness * 1.5f, handleSpan * 0.5f, wallThickness * 1.5f);
    }

    void BuildOrifice()
    {
        GameObject orifice = CreatePrimitiveChild("Orifice", PrimitiveType.Cylinder, innerMat);
        orifice.transform.localPosition = new Vector3(0f, -bucketHeight * 0.5f - wallThickness * 1.1f, 0f);
        orifice.transform.localScale = new Vector3(
            fluidSystem != null ? fluidSystem.orificeDiameter : 0.05f,
            wallThickness * 0.5f,
            fluidSystem != null ? fluidSystem.orificeDiameter : 0.05f);
        paintSpawnPoint = orifice.transform;
    }

    void BuildLiquidVolume(float innerBottomRadius, float innerTopRadius)
    {
        GameObject paint = new GameObject("PaintVolume");
        paint.transform.SetParent(transform, false);

        liquidFilter = paint.AddComponent<MeshFilter>();
        paint.AddComponent<MeshRenderer>().sharedMaterial = paintMat;

        liquidBottomLocalY = -bucketHeight * 0.5f + wallThickness * 0.7f;
        liquidTopLocalY = bucketHeight * 0.5f - wallThickness * 2.0f;
        liquidFilter.sharedMesh = CreateLiquidMesh(innerBottomRadius, innerTopRadius, liquidTopLocalY - liquidBottomLocalY, radialSegments);
        paint.transform.localPosition = new Vector3(0f, liquidBottomLocalY, 0f);

        paintRenderer = paint.GetComponent<Renderer>();
    }

    void CaptureBucketVisualBounds(Transform visualRoot)
    {
        Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        bool initialized = false;
        Bounds localBounds = new Bounds();

        foreach (Renderer renderer in renderers)
        {
            if (renderer is LineRenderer)
                continue;

            Bounds worldBounds = renderer.bounds;
            foreach (Vector3 corner in GetBoundsCorners(worldBounds))
            {
                Vector3 localPoint = visualRoot.InverseTransformPoint(corner);
                if (!initialized)
                {
                    localBounds = new Bounds(localPoint, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    localBounds.Encapsulate(localPoint);
                }
            }
        }

        hasBucketVisualBounds = initialized;
        bucketVisualBoundsLocal = localBounds;
    }

    void BuildImportedBucket()
    {
        bucketModelInstance = Instantiate(bucketModelPrefab, transform);
        bucketModelInstance.name = "BucketModel";
        bucketModelInstance.transform.localPosition = bucketModelLocalPosition;
        bucketModelInstance.transform.localRotation = Quaternion.Euler(bucketModelLocalEulerAngles);
        bucketModelInstance.transform.localScale = bucketModelLocalScale;

        CaptureBucketVisualBounds(bucketModelInstance.transform);
        BuildImportedLiquidVolume();
    }

    void BuildImportedLiquidVolume()
    {
        if (!hasBucketVisualBounds)
            return;

        float modelRadius = Mathf.Max(bucketVisualBoundsLocal.extents.x, bucketVisualBoundsLocal.extents.z);
        float innerBottomRadius = Mathf.Max(0.01f, modelRadius * 0.82f);
        float innerTopRadius = Mathf.Max(innerBottomRadius + 0.01f, modelRadius * 0.95f);

        liquidBottomLocalY = bucketVisualBoundsLocal.min.y + wallThickness * 0.6f;
        liquidTopLocalY = bucketVisualBoundsLocal.max.y - wallThickness * 1.4f;
        if (liquidTopLocalY <= liquidBottomLocalY)
            liquidTopLocalY = liquidBottomLocalY + Mathf.Max(0.05f, bucketVisualBoundsLocal.size.y * 0.9f);

        if (paintSpawnPoint == null)
        {
            GameObject spawn = new GameObject("PaintSpawnPoint");
            spawn.transform.SetParent(transform, false);
            spawn.transform.localPosition = new Vector3(0f, bucketVisualBoundsLocal.min.y + wallThickness * 0.5f, 0f);
            paintSpawnPoint = spawn.transform;
        }

        BuildLiquidVolume(innerBottomRadius, innerTopRadius);
    }

    public Vector3 GetPaintSpawnPosition()
    {
        return paintSpawnPoint != null ? paintSpawnPoint.position : transform.position;
    }

    void UpdateLiquidVisual(bool force)
    {
        if (fluidSystem == null || liquidFilter == null || paintRenderer == null)
            return;

        float fillT = Mathf.Clamp01(fluidSystem.h_paint / Mathf.Max(0.001f, maxPaintHeight));
        Color paintColor = fluidSystem.currentPaintColor;
        paintColor.a = 0.94f;

        if (!force &&
            Mathf.Approximately(fillT, lastFillT) &&
            paintColor == lastPaintColor)
        {
            return;
        }

        lastFillT = fillT;
        lastPaintColor = paintColor;

        float fillHeight = Mathf.Lerp(0.02f, liquidTopLocalY - liquidBottomLocalY, fillT);

        float innerBottomRadius;
        float innerTopRadius;
        if (hasBucketVisualBounds)
        {
            float modelRadius = Mathf.Max(bucketVisualBoundsLocal.extents.x, bucketVisualBoundsLocal.extents.z);
            innerBottomRadius = Mathf.Max(0.01f, modelRadius * 0.82f);
            innerTopRadius = Mathf.Max(innerBottomRadius + 0.01f, modelRadius * 0.95f);
        }
        else
        {
            innerBottomRadius = Mathf.Max(0.01f, bottomRadius);
            innerTopRadius = Mathf.Max(innerBottomRadius + 0.01f, topRadius);
        }
        float topRadiusAtFill = Mathf.Lerp(innerBottomRadius, innerTopRadius, Mathf.Clamp01(fillHeight / Mathf.Max(0.001f, liquidTopLocalY - liquidBottomLocalY)));

        liquidFilter.sharedMesh = CreateLiquidMesh(innerBottomRadius * 0.995f, topRadiusAtFill * 0.995f, fillHeight, radialSegments);
        liquidFilter.transform.localPosition = new Vector3(0f, liquidBottomLocalY, 0f);

        paintMat.color = paintColor;
        paintRenderer.sharedMaterial = paintMat;
        paintRenderer.enabled = fillT > 0.001f;
    }

    static Vector3[] GetBoundsCorners(Bounds bounds)
    {
        Vector3 c = bounds.center;
        Vector3 e = bounds.extents;
        return new[]
        {
            c + new Vector3( e.x,  e.y,  e.z),
            c + new Vector3( e.x,  e.y, -e.z),
            c + new Vector3( e.x, -e.y,  e.z),
            c + new Vector3( e.x, -e.y, -e.z),
            c + new Vector3(-e.x,  e.y,  e.z),
            c + new Vector3(-e.x,  e.y, -e.z),
            c + new Vector3(-e.x, -e.y,  e.z),
            c + new Vector3(-e.x, -e.y, -e.z),
        };
    }

    MeshFilter CreateMeshChild(string name, Material material, Mesh mesh)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        MeshFilter filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        return filter;
    }

    GameObject CreatePrimitiveChild(string name, PrimitiveType type, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(transform, false);
        go.GetComponent<Renderer>().material = material;
        Destroy(go.GetComponent<Collider>());
        return go;
    }

    static Shader FindLitShader()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Diffuse");
        return shader;
    }

    static Mesh CreateFrustumWallMesh(float bottomRadius, float topRadius, float height, int segments, bool invert)
    {
        Mesh mesh = new Mesh();
        mesh.name = "FrustumWall";

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();

        float slope = (bottomRadius - topRadius) / Mathf.Max(0.0001f, height);

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = t * Mathf.PI * 2f;
            float x = Mathf.Cos(angle);
            float z = Mathf.Sin(angle);
            Vector3 wallNormal = new Vector3(x, slope, z).normalized;
            if (invert)
                wallNormal = -wallNormal;

            vertices.Add(new Vector3(x * bottomRadius, 0f, z * bottomRadius));
            vertices.Add(new Vector3(x * topRadius, height, z * topRadius));
            normals.Add(wallNormal);
            normals.Add(wallNormal);
            uvs.Add(new Vector2(t, 0f));
            uvs.Add(new Vector2(t, 1f));
        }

        for (int i = 0; i < segments; i++)
        {
            int baseIndex = i * 2;
            if (!invert)
            {
                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 1);

                triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 3);
            }
            else
            {
                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex + 2);

                triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex + 3);
                triangles.Add(baseIndex + 2);
            }
        }

        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    static Mesh CreateDiscMesh(float radius, int segments, bool upward)
    {
        Mesh mesh = new Mesh();
        mesh.name = "Disc";

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();

        Vector3 normal = upward ? Vector3.up : Vector3.down;
        vertices.Add(Vector3.zero);
        normals.Add(normal);
        uvs.Add(new Vector2(0.5f, 0.5f));

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = t * Mathf.PI * 2f;
            float x = Mathf.Cos(angle);
            float z = Mathf.Sin(angle);
            vertices.Add(new Vector3(x * radius, 0f, z * radius));
            normals.Add(normal);
            uvs.Add(new Vector2(x * 0.5f + 0.5f, z * 0.5f + 0.5f));
        }

        for (int i = 1; i <= segments; i++)
        {
            if (upward)
            {
                triangles.Add(0);
                triangles.Add(i);
                triangles.Add(i + 1);
            }
            else
            {
                triangles.Add(0);
                triangles.Add(i + 1);
                triangles.Add(i);
            }
        }

        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    static Mesh CreateLiquidMesh(float bottomRadius, float topRadius, float height, int segments)
    {
        Mesh mesh = new Mesh();
        mesh.name = "LiquidVolume";

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        var topCapRingIndices = new List<int>(segments + 1);

        float slope = (bottomRadius - topRadius) / Mathf.Max(0.0001f, height);

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = t * Mathf.PI * 2f;
            float x = Mathf.Cos(angle);
            float z = Mathf.Sin(angle);
            Vector3 normal = new Vector3(x, slope, z).normalized;

            vertices.Add(new Vector3(x * bottomRadius, 0f, z * bottomRadius));
            vertices.Add(new Vector3(x * topRadius, height, z * topRadius));
            normals.Add(normal);
            normals.Add(normal);
            uvs.Add(new Vector2(t, 0f));
            uvs.Add(new Vector2(t, 1f));
        }

        for (int i = 0; i < segments; i++)
        {
            int baseIndex = i * 2;
            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 1);

            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 3);
        }

        int topCenterIndex = vertices.Count;
        vertices.Add(new Vector3(0f, height, 0f));
        normals.Add(Vector3.up);
        uvs.Add(new Vector2(0.5f, 0.5f));

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = t * Mathf.PI * 2f;
            float x = Mathf.Cos(angle);
            float z = Mathf.Sin(angle);
            topCapRingIndices.Add(vertices.Count);
            vertices.Add(new Vector3(x * topRadius, height, z * topRadius));
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(x * 0.5f + 0.5f, z * 0.5f + 0.5f));
        }

        for (int i = 0; i < segments; i++)
        {
            int ringIndex = topCapRingIndices[i];
            int nextRingIndex = topCapRingIndices[i + 1];
            triangles.Add(topCenterIndex);
            triangles.Add(ringIndex);
            triangles.Add(nextRingIndex);
        }

        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}

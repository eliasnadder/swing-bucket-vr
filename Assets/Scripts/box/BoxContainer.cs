using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// حاوية صندوقية (متوازي مستطيلات) مجوّفة يمكن ملؤها بجسيمات SPH ودورانها.
/// جميع الأبعاد بوحدة cm لتطابق نظام وحدات المشروع (g=981, ...).
/// </summary>
public class BoxContainer : MonoBehaviour
{
    [Header("Box Dimensions (cm)")]
    [Tooltip("الأبعاد الداخلية للصندوق (العرض X، الارتفاع Y، العمق Z)")]
    public Vector3 innerSize = new Vector3(30f, 20f, 30f);
    [Tooltip("سماكة الجدران")]
    public float wallThickness = 1f;
    [Tooltip("هل السقف مفتوح؟")]
    public bool openTop = true;

    [Header("Mesh Quality")]
    public Material wallMaterial;

    [Header("Drain Hole (bottom)")]
    [Tooltip("يفتح ثقب تصريف في أرضية الصندوق يسمح للطلاء بالخروج منه")]
    public bool hasDrainHole = true;
    [Tooltip("نصف قطر فتحة التصريف (cm)")]
    public float holeRadius = 3f;
    [Tooltip("إزاحة مركز الفتحة عن مركز الأرضية على X وZ (cm)")]
    public Vector2 holeLocalOffset = Vector2.zero;
    [Tooltip("طول أنبوب التصريف المرئي أسفل الفتحة (cm). 0 = بدون أنبوب")]
    public float spoutLength = 2f;

    private GameObject wallsRoot;

    public Vector3 HalfExtentsInner => innerSize * 0.5f;
    public bool HasDrainHole => hasDrainHole;
    public float HoleRadius => Mathf.Max(0.01f, holeRadius);
    public Vector2 HoleOffset => holeLocalOffset;

    void Awake()
    {
        if (wallsRoot == null)
            BuildBox();
    }

    void BuildBox()
    {
        wallsRoot = new GameObject("BoxWalls");
        wallsRoot.transform.SetParent(transform, false);

        // شفافة بوضوح — alpha منخفض حتى يظهر الطلاء بداخلها
        Material mat = wallMaterial != null ? wallMaterial : CreateDefaultMaterial(new Color(0.75f, 0.8f, 0.85f, 0.28f), true);

        Vector3 half = HalfExtentsInner;
        float t = wallThickness;

        if (hasDrainHole)
            BuildFloorWithHole(mat, half, t);
        else
            CreatePanel("Floor",
                center: new Vector3(0f, -half.y - t * 0.5f, 0f),
                size: new Vector3(innerSize.x + 2f * t, t, innerSize.z + 2f * t),
                mat);

        if (!openTop)
        {
            CreatePanel("Ceiling",
                center: new Vector3(0f, half.y + t * 0.5f, 0f),
                size: new Vector3(innerSize.x + 2f * t, t, innerSize.z + 2f * t),
                mat);
        }

        CreatePanel("Wall_+X", new Vector3(half.x + t * 0.5f, 0f, 0f), new Vector3(t, innerSize.y, innerSize.z + 2f * t), mat);
        CreatePanel("Wall_-X", new Vector3(-half.x - t * 0.5f, 0f, 0f), new Vector3(t, innerSize.y, innerSize.z + 2f * t), mat);
        CreatePanel("Wall_+Z", new Vector3(0f, 0f, half.z + t * 0.5f), new Vector3(innerSize.x + 2f * t, innerSize.y, t), mat);
        CreatePanel("Wall_-Z", new Vector3(0f, 0f, -half.z - t * 0.5f), new Vector3(innerSize.x + 2f * t, innerSize.y, t), mat);

        // ← تمت إزالة طبقة "InnerFace" القديمة هنا (كانت مادة معتمة شبه سوداء
        // ومثلثات مزدوجة الوجه، فكانت تُرسم حتى من خارج الصندوق خلف الجدار
        // الشفاف مباشرة وتُغطي كل شيء بالأسود). جدران الـ Cube وحدها كافية.
    }

    void BuildFloorWithHole(Material mat, Vector3 half, float t)
    {
        GameObject floor = new GameObject("Floor");
        floor.transform.SetParent(wallsRoot.transform, false);
        floor.transform.localPosition = new Vector3(0f, -half.y, 0f);

        Mesh floorMesh = CreateFloorWithHoleMesh(half.x + t, half.z + t, HoleRadius, holeLocalOffset);

        MeshFilter mf = floor.AddComponent<MeshFilter>();
        mf.sharedMesh = floorMesh;
        MeshRenderer mr = floor.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;

        if (spoutLength > 0.001f)
        {
            GameObject spout = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            spout.name = "DrainSpout";
            Destroy(spout.GetComponent<Collider>());
            spout.transform.SetParent(wallsRoot.transform, false);
            spout.transform.localPosition =
                new Vector3(holeLocalOffset.x, -half.y - spoutLength * 0.5f, holeLocalOffset.y);
            // الأسطوانة الافتراضية في Unity: ارتفاع 2، قطر 1 (محلياً)
            spout.transform.localScale =
                new Vector3(HoleRadius * 2f, spoutLength * 0.5f, HoleRadius * 2f);
            spout.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }

    /// <summary>
    /// أرضية مسطحة بفتحة (إطار من 4 قطع مستطيلة) بدل لوح مصمت.
    /// الفتحة تبدو مستديرة بصرياً بفضل أسطوانة التصريف الموضوعة تحتها.
    /// </summary>
    static Mesh CreateFloorWithHoleMesh(float halfX, float halfZ, float holeHalfSize, Vector2 holeOffset)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();

        float ox = holeOffset.x, oz = holeOffset.y;
        float hx = holeHalfSize, hz = holeHalfSize;

        void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int baseIdx = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            for (int k = 0; k < 4; k++) { normals.Add(Vector3.up); uvs.Add(Vector2.zero); }
            triangles.Add(baseIdx); triangles.Add(baseIdx + 1); triangles.Add(baseIdx + 2);
            triangles.Add(baseIdx); triangles.Add(baseIdx + 2); triangles.Add(baseIdx + 3);
        }

        AddQuad(new Vector3(-halfX, 0f, oz + hz), new Vector3(halfX, 0f, oz + hz),
                new Vector3(halfX, 0f, halfZ), new Vector3(-halfX, 0f, halfZ));       // +Z strip
        AddQuad(new Vector3(-halfX, 0f, -halfZ), new Vector3(halfX, 0f, -halfZ),
                new Vector3(halfX, 0f, oz - hz), new Vector3(-halfX, 0f, oz - hz));   // -Z strip
        AddQuad(new Vector3(ox + hx, 0f, oz - hz), new Vector3(halfX, 0f, oz - hz),
                new Vector3(halfX, 0f, oz + hz), new Vector3(ox + hx, 0f, oz + hz)); // +X strip
        AddQuad(new Vector3(-halfX, 0f, oz - hz), new Vector3(ox - hx, 0f, oz - hz),
                new Vector3(ox - hx, 0f, oz + hz), new Vector3(-halfX, 0f, oz + hz)); // -X strip

        Mesh mesh = new Mesh { name = "FloorWithHole" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    void CreatePanel(string name, Vector3 center, Vector3 size, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(wallsRoot.transform, false);
        go.transform.localPosition = center;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static Material CreateDefaultMaterial(Color color, bool transparent)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material mat = new Material(shader);
        mat.color = color;
        if (transparent)
        {
            mat.SetFloat("_Surface", 1);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.renderQueue = 3000;
        }
        return mat;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, innerSize);

        if (hasDrainHole)
        {
            Gizmos.color = Color.yellow;
            Vector3 c = new Vector3(holeLocalOffset.x, -HalfExtentsInner.y, holeLocalOffset.y);
            Gizmos.DrawWireSphere(c, HoleRadius);
        }
    }
#endif
}
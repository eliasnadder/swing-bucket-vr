using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// حاوية صندوقية (متوازي مستطيلات) مجوّفة يمكن ملؤها بجسيمات SPH ودورانها.
/// بديل BucketBuilder لكن لصندوق بدل السطل.
/// جميع الأبعاد بوحدة cm لتطابق نظام وحدات المشروع (g=981, ...).
/// </summary>
public class BoxContainer : MonoBehaviour
{
    [Header("Box Dimensions (cm)")]
    [Tooltip("الأبعاد الداخلية للصندوق (العرض X، الارتفاع Y، العمق Z)")]
    public Vector3 innerSize = new Vector3(30f, 20f, 30f);
    [Tooltip("سماكة الجدران")]
    public float wallThickness = 1f;
    [Tooltip("هل السقف مفتوح؟ (يسمح بانسكاب الطلاء لو مال الصندوق كثير)")]
    public bool openTop = true;

    [Header("Mesh Quality")]
    public Material wallMaterial;
    public Material innerFaceMaterial;

    private GameObject wallsRoot;

    /// <summary>نصف الأبعاد الداخلية بالإحداثيات المحلية — تُستخدم من BoxFluidBoundary وBoxFluidSeeder.</summary>
    public Vector3 HalfExtentsInner => innerSize * 0.5f;

    void Awake()
    {
        if (wallsRoot == null)
            BuildBox();
    }

    void BuildBox()
    {
        wallsRoot = new GameObject("BoxWalls");
        wallsRoot.transform.SetParent(transform, false);

        Material mat = wallMaterial != null ? wallMaterial : CreateDefaultMaterial(new Color(0.75f, 0.8f, 0.85f, 0.35f), true);
        Material inner = innerFaceMaterial != null ? innerFaceMaterial : CreateDefaultMaterial(new Color(0.2f, 0.2f, 0.22f), false);

        Vector3 half = HalfExtentsInner;
        float t = wallThickness;

        // الأرض (Floor)
        CreatePanel("Floor",
            center: new Vector3(0f, -half.y - t * 0.5f, 0f),
            size: new Vector3(innerSize.x + 2f * t, t, innerSize.z + 2f * t),
            mat);

        // السقف — فقط إذا مو مفتوح
        if (!openTop)
        {
            CreatePanel("Ceiling",
                center: new Vector3(0f, half.y + t * 0.5f, 0f),
                size: new Vector3(innerSize.x + 2f * t, t, innerSize.z + 2f * t),
                mat);
        }

        // الجدران الأربعة الجانبية
        CreatePanel("Wall_+X", new Vector3(half.x + t * 0.5f, 0f, 0f), new Vector3(t, innerSize.y, innerSize.z + 2f * t), mat);
        CreatePanel("Wall_-X", new Vector3(-half.x - t * 0.5f, 0f, 0f), new Vector3(t, innerSize.y, innerSize.z + 2f * t), mat);
        CreatePanel("Wall_+Z", new Vector3(0f, 0f, half.z + t * 0.5f), new Vector3(innerSize.x + 2f * t, innerSize.y, t), mat);
        CreatePanel("Wall_-Z", new Vector3(0f, 0f, -half.z - t * 0.5f), new Vector3(innerSize.x + 2f * t, innerSize.y, t), mat);

        // وجوه داخلية (اختياري، تحسّن الشكل البصري من الداخل)
        CreateInnerFace("InnerFace_Floor", new Vector3(0f, -half.y, 0f), new Vector3(innerSize.x, innerSize.z), Vector3.up, inner);
        CreateInnerFace("InnerFace_-X", new Vector3(-half.x, 0f, 0f), new Vector3(innerSize.z, innerSize.y), Vector3.right, inner);
        CreateInnerFace("InnerFace_+X", new Vector3(half.x, 0f, 0f), new Vector3(innerSize.z, innerSize.y), Vector3.left, inner);
        CreateInnerFace("InnerFace_-Z", new Vector3(0f, 0f, -half.z), new Vector3(innerSize.x, innerSize.y), Vector3.forward, inner);
        CreateInnerFace("InnerFace_+Z", new Vector3(0f, 0f, half.z), new Vector3(innerSize.x, innerSize.y), Vector3.back, inner);
    }

    void CreatePanel(string name, Vector3 center, Vector3 size, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(wallsRoot.transform, false);
        go.transform.localPosition = center;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        // نحتفظ بالـ Collider الافتراضي لأغراض بصرية فقط؛ اصطدام الـ SPH يُحسب رياضيًا في BoxFluidBoundary
    }

    void CreateInnerFace(string name, Vector3 center, Vector2 size, Vector3 normal, Material mat)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(wallsRoot.transform, false);
        go.transform.localPosition = center;
        go.transform.localRotation = Quaternion.LookRotation(-normal, Vector3.up);

        Mesh mesh = new Mesh();
        float hx = size.x * 0.5f, hy = size.y * 0.5f;
        mesh.vertices = new[]
        {
            new Vector3(-hx, -hy, 0f), new Vector3(hx, -hy, 0f),
            new Vector3(hx, hy, 0f), new Vector3(-hx, hy, 0f)
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 }; // ثنائي الجهة
        mesh.RecalculateNormals();

        MeshFilter mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
    }

    static Material CreateDefaultMaterial(Color color, bool transparent)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material mat = new Material(shader);
        mat.color = color;
        if (transparent)
        {
            mat.SetFloat("_Surface", 1); // URP Lit transparent
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
    }
#endif
}

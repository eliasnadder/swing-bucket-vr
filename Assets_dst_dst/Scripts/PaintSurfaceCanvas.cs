using UnityEngine;

public class PaintSurfaceCanvas : MonoBehaviour
{
    public static PaintSurfaceCanvas Instance;

    [Header("Canvas Texturing")]
    public Renderer canvasRenderer;
    public int textureSize = 512;

    [Header("Gaussian Model Parameters (Section 2.6.1)")]
    public float sigma = 0.05f;           // معامل الانتشار الغاوسي القياسي
    public float absorptionCoeff = 0.8f;  // معامل الامتصاص للسطح

    private Texture2D paintTexture;
    private Color[] bufferColors;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // إنشاء خامة مخصصة للرسم الفوري دون كولايدرات
        paintTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
        bufferColors = new Color[textureSize * textureSize];

        // تلوين اللوحة بالخلفية البيضاء الأساسية
        for (int i = 0; i < bufferColors.Length; i++) bufferColors[i] = Color.white;

        paintTexture.SetPixels(bufferColors);
        paintTexture.Apply();
        canvasRenderer.material.mainTexture = paintTexture;
    }

    // تطبيق النموذج الغاوسي الرياضي عند الاصطدام
    // Enhanced method accepting dynamic particle colors
    public void PaintAt(Vector3 worldPos, Vector3 impactVelocity, Color paintColor)
    {
        Vector3 localPos = transform.InverseTransformPoint(worldPos);
        float u = localPos.x + 0.5f;
        float v = localPos.z + 0.5f;

        int centerX = Mathf.RoundToInt(u * textureSize);
        int centerY = Mathf.RoundToInt(v * textureSize);

        float impactEnergy = impactVelocity.magnitude * absorptionCoeff;
        int radius = Mathf.RoundToInt(sigma * textureSize * impactEnergy);

        for (int x = centerX - radius; x <= centerX + radius; x++)
        {
            for (int y = centerY - radius; y <= centerY + radius; y++)
            {
                if (x >= 0 && x < textureSize && y >= 0 && y < textureSize)
                {
                    float distSq = Mathf.Pow(x - centerX, 2) + Mathf.Pow(y - centerY, 2);
                    float maxDistSq = radius * radius;

                    if (distSq <= maxDistSq)
                    {
                        float r_normalized = distSq / maxDistSq;
                        float intensity = Mathf.Exp(-r_normalized * 2f) * impactEnergy;

                        int pixelIndex = y * textureSize + x;

                        // Blends the incoming fluid color safely into the structural canvas map
                        bufferColors[pixelIndex] = Color.Lerp(bufferColors[pixelIndex], paintColor, intensity);
                    }
                }
            }
        }

        paintTexture.SetPixels(bufferColors);
        paintTexture.Apply();
    }
}
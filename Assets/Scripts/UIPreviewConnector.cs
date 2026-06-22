using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RawImage))]
public class UIPreviewConnector : MonoBehaviour
{
    [Tooltip("اربط هنا الـ PaintCanvas Component الموجود على الـ Quad")]
    public PaintCanvas canvasEngine;   // ← تم التحويل من PaintSurfaceCanvas
    private RawImage previewImage;

    void Start()
    {
        previewImage = GetComponent<RawImage>();

        if (canvasEngine == null)
            canvasEngine = FindAnyObjectByType<PaintCanvas>();

        if (canvasEngine != null)
            previewImage.texture = canvasEngine.GetPaintTexture();
    }
}
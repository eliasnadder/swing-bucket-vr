using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RawImage))]
public class UIPreviewConnector : MonoBehaviour
{
    public PaintCanvas canvasEngine;
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
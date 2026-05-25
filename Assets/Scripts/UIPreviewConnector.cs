using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RawImage))]
public class UIPreviewConnector : MonoBehaviour
{
    public PaintSurfaceCanvas canvasEngine;
    private RawImage previewImage;

    void Start()
    {
        previewImage = GetComponent<RawImage>();

        if (canvasEngine != null)
        {
            // Bind the internal texture directly to the UI component
            previewImage.texture = canvasEngine.GetPaintTexture();
        }
    }
}
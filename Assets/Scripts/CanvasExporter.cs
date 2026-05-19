using UnityEngine;
using System.IO;

public class CanvasExporter : MonoBehaviour
{
    [Header("Canvas Reference")]
    public PaintSurfaceCanvas canvasTarget;

    [Header("UI Export Button")]
    public UnityEngine.UI.Button exportButton;

    void Start()
    {
        if (exportButton != null)
        {
            exportButton.onClick.AddListener(ExportCanvasToPNG);
        }
    }

    public void ExportCanvasToPNG()
    {
        if (canvasTarget == null)
        {
            Debug.LogError("Export Error: PaintSurfaceCanvas reference is missing!");
            return;
        }

        // 1. Grab the active runtime texture from our canvas script
        // We look for the main texture on the renderer assigned to your canvas
        Texture2D structuralTexture = canvasTarget.canvasRenderer.material.mainTexture as Texture2D;

        if (structuralTexture == null)
        {
            Debug.LogError("Export Error: Could not extract Texture2D from the Canvas material.");
            return;
        }

        // 2. Read the pixel array directly from the structural texture buffer
        Color[] pixelBuffer = structuralTexture.GetPixels();

        // Create a temporary clone texture to safely encode to PNG format without pipeline compression conflicts
        Texture2D exportTexture = new Texture2D(structuralTexture.width, structuralTexture.height, TextureFormat.RGBA32, false);
        exportTexture.SetPixels(pixelBuffer);
        exportTexture.Apply();

        // 3. Mathematical conversion to raw PNG byte arrays
        byte[] textureBytes = exportTexture.EncodeToPNG();

        // Clean up temporary texture memory immediately
        Destroy(exportTexture);

        // 4. Generate a unique file name using a clean timestamp
        string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string fileName = "PendulumArt_" + timestamp + ".png";

        // Save directly into your main project folder
        string totalPath = Path.Combine(Application.dataPath, "../" + fileName);

        // 5. Write bytes straight to disk storage safely
        File.WriteAllBytes(totalPath, textureBytes);

        Debug.Log("🎨 Success! Finalized artwork saved cleanly to: " + Path.GetFullPath(totalPath));
    }
}
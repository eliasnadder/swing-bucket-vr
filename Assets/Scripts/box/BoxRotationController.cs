using UnityEngine;

/// <summary>
/// يتحكم بدوران الصندوق حول محورين (Tilt X, Tilt Z) عبر Sliders من الواجهة،
/// أو تلقائيًا عبر موجة جيبية لعرض توضيحي للـ sloshing.
/// </summary>
public class BoxRotationController : MonoBehaviour
{
    [Header("Manual Tilt (deg)")]
    [Range(-80f, 80f)] public float tiltX = 0f;
    [Range(-80f, 80f)] public float tiltZ = 0f;

    [Header("Auto Demo Mode")]
    public bool autoRotate = false;
    public float autoAmplitude = 25f;   // درجة
    public float autoSpeed = 0.6f;      // Hz تقريبًا

    private float autoTimer;

    void Update()
    {
        if (autoRotate)
        {
            autoTimer += Time.deltaTime;
            tiltX = Mathf.Sin(autoTimer * autoSpeed * Mathf.PI * 2f) * autoAmplitude;
            tiltZ = Mathf.Cos(autoTimer * autoSpeed * 0.7f * Mathf.PI * 2f) * autoAmplitude * 0.6f;
        }

        transform.localRotation = Quaternion.Euler(tiltX, 0f, tiltZ);
    }

    // ── دوال عامة للربط مع Sliders في SimulationUIManager أو سكريبت مبسّط ──
    public void SetTiltX(float value) => tiltX = value;
    public void SetTiltZ(float value) => tiltZ = value;
    public void SetAutoRotate(bool value) => autoRotate = value;
}

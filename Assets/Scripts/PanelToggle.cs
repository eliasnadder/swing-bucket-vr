using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PanelToggle : MonoBehaviour
{
    public GameObject panel;
    public TextMeshProUGUI buttonText;

    private bool isVisible = true;

    public void Toggle()
    {
        isVisible = !isVisible;
        panel.SetActive(isVisible);
        buttonText.text = isVisible ? "✕" : "☰";
    }
}
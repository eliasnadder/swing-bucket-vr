using UnityEngine;
using UnityEngine.UI; // Kept for Sliders and Buttons
using TMPro;          // Added to support TextMeshPro elements

public class SimulationUIManager : MonoBehaviour
{
    [Header("Engine References")]
    public SwingingPendulum pendulumEngine;
    public FluidSPHSystem fluidEngine;

    [Header("UI Sliders - Pendulum Properties")]
    public Slider lengthSlider;
    public TextMeshProUGUI lengthValueText; // Changed to TextMeshProUGUI

    public Slider elasticitySlider;
    public TextMeshProUGUI elasticityValueText; // Changed to TextMeshProUGUI

    public Slider airResistanceSlider;
    public TextMeshProUGUI airResistanceValueText; // Changed to TextMeshProUGUI

    public Slider gravitySlider;
    public TextMeshProUGUI gravityValueText; // Changed to TextMeshProUGUI

    [Header("UI Sliders - Fluid Properties")]
    public Slider orificeSlider;
    public TextMeshProUGUI orificeValueText; // Changed to TextMeshProUGUI

    [Header("UI Color Selection")]
    public TMP_Dropdown colorDropdown; // Changed to TMP_Dropdown

    [Header("Simulation Control")]
    public Button restartButton;

    void Start()
    {
        if (pendulumEngine != null)
        {
            lengthSlider.value = pendulumEngine.L0;
            elasticitySlider.value = pendulumEngine.k_rope;
            airResistanceSlider.value = pendulumEngine.b;
            gravitySlider.value = pendulumEngine.g;
        }

        if (fluidEngine != null)
        {
            orificeSlider.value = fluidEngine.orificeDiameter;
        }

        lengthSlider.onValueChanged.AddListener(UpdateLength);
        elasticitySlider.onValueChanged.AddListener(UpdateElasticity);
        airResistanceSlider.onValueChanged.AddListener(UpdateAirResistance);
        gravitySlider.onValueChanged.AddListener(UpdateGravity);
        orificeSlider.onValueChanged.AddListener(UpdateOrificeDiameter);

        if (colorDropdown != null)
            colorDropdown.onValueChanged.AddListener(UpdatePaintColor);

        if (restartButton != null)
            restartButton.onClick.AddListener(RestartSimulationScene);

        UpdateLabels();
    }

    void UpdateLength(float value)
    {
        if (pendulumEngine != null) pendulumEngine.L0 = value;
        UpdateLabels();
    }

    void UpdateElasticity(float value)
    {
        if (pendulumEngine != null) pendulumEngine.k_rope = value;
        UpdateLabels();
    }

    void UpdateAirResistance(float value)
    {
        if (pendulumEngine != null) pendulumEngine.b = value;
        UpdateLabels();
    }

    void UpdateGravity(float value)
    {
        if (pendulumEngine != null) pendulumEngine.g = value;
        UpdateLabels();
    }

    void UpdateOrificeDiameter(float value)
    {
        if (fluidEngine != null) fluidEngine.orificeDiameter = value;
        UpdateLabels();
    }

    void UpdatePaintColor(int index)
    {
        if (fluidEngine == null) return;

        switch (index)
        {
            case 0: fluidEngine.ChangePaintColor(Color.red); break;
            case 1: fluidEngine.ChangePaintColor(Color.blue); break;
            case 2: fluidEngine.ChangePaintColor(Color.green); break;
            case 3: fluidEngine.ChangePaintColor(Color.yellow); break;
            default: fluidEngine.ChangePaintColor(Color.red); break;
        }
    }

    void UpdateLabels()
    {
        if (lengthValueText != null) lengthValueText.text = lengthSlider.value.ToString("F2") + " m";
        if (elasticityValueText != null) elasticityValueText.text = elasticitySlider.value.ToString("F0") + " N/m";
        if (airResistanceValueText != null) airResistanceValueText.text = airResistanceSlider.value.ToString("F3");
        if (gravityValueText != null) gravityValueText.text = gravitySlider.value.ToString("F2") + " m/s²";
        if (orificeValueText != null) orificeValueText.text = (orificeSlider.value * 1000f).ToString("F1") + " mm";
    }

    void RestartSimulationScene()
    {
        string activeSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        UnityEngine.SceneManagement.SceneManager.LoadScene(activeSceneName);
    }
}
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// سكريبت لتوليد مشهد BoxSPH تلقائيًا في المحرر
/// استخدم: Tools > Setup Box SPH Scene
/// </summary>
public class BoxSceneSetup : MonoBehaviour
{
    [MenuItem("Tools/Setup Box SPH Scene", false, 1000)]
    public static void SetupBoxSPHScene()
    {
        // إنشاء مشهد جديد
        var newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        
        // 1. إنشاء BoxContainer
        GameObject boxContainerGO = new GameObject("BoxContainer");
        var boxContainer = boxContainerGO.AddComponent<BoxContainer>();
        boxContainer.innerSize = new Vector3(30f, 20f, 30f);
        boxContainer.wallThickness = 1f;
        boxContainer.openTop = true;
        
        // 2. إضافة BoxFluidSeeder
        var seeder = boxContainerGO.AddComponent<BoxFluidSeeder>();
        seeder.fillRatio = 0.4f;
        seeder.particleSpacing = 1.5f;
        seeder.jitter = 0.15f;
        seeder.paintColor = new Color(0.85f, 0.1f, 0.1f, 1f);
        
        // 3. إضافة BoxRotationController
        var rotationController = boxContainerGO.AddComponent<BoxRotationController>();
        rotationController.autoRotate = true;
        rotationController.autoAmplitude = 25f;
        rotationController.autoSpeed = 0.6f;
        
        // 4. إنشاء SPHSystem
        GameObject sphSystemGO = new GameObject("SPHSystem");
        
        // 4a. إضافة SPHFluidSolver
        var fluidSolver = sphSystemGO.AddComponent<SPHFluidSolver>();
        fluidSolver.smoothingRadius = 8f;
        fluidSolver.particleMass = 0.02f;
        fluidSolver.restDensity = 1000f;
        fluidSolver.gasConstant = 2000f;
        fluidSolver.viscosity = 0.25f;
        fluidSolver.gravity = new Vector3(0f, -981f, 0f);
        fluidSolver.damping = 0.02f;
        fluidSolver.timeStep = 0.0166667f;
        fluidSolver.useFull3D = true;
        fluidSolver.depthPlane = 0f;
        fluidSolver.depthDamping = 0.65f;
        fluidSolver.initialVolume = 0.5f;
        fluidSolver.maxPaintHeight = 0.30f;
        fluidSolver.Cd = 0.6f;
        fluidSolver.orificeDiameter = 0.05f;
        fluidSolver.paintDensity = 1200f;
        fluidSolver.currentPaintColor = new Color(0.85f, 0.1f, 0.1f, 1f);
        fluidSolver.useBernoulliApproximation = true;
        fluidSolver.surfaceTensionCoeff = 0f;
        fluidSolver.surfaceTensionMinDist = 0f;
        fluidSolver.temperature = 25f;
        fluidSolver.humidity = 0.5f;
        fluidSolver.windSpeed = 0f;
        fluidSolver.windDirection = Vector3.right;
        fluidSolver.windCoeff = 0.5f;
        fluidSolver.drawGizmos = false;
        fluidSolver.particlesPerVolumeUnit = 50000f;
        fluidSolver.maxSpawnPerFrame = 20;
        fluidSolver.useInternalEmission = false; // Important: no emitter in box system
        fluidSolver.enableCoalescence = false;
        fluidSolver.coalescenceRadius = 1f;
        fluidSolver.maxCoalesceRelSpeed = 5f;
        
        // 4b. إضافة SPHRenderer
        var sphRenderer = sphSystemGO.AddComponent<SPHRenderer>();
        sphRenderer.solver = fluidSolver;
        sphRenderer.initialPoolSize = 512;
        sphRenderer.particleSize = 1.5f;
        sphRenderer.createMaterialIfMissing = true;
        sphRenderer.showAirStream = false;
        sphRenderer.streamRadiusMultiplier = 1f;
        sphRenderer.dropletRadiusMultiplier = 1f;
        sphRenderer.maxStreamPoints = 32;
        sphRenderer.streamSubdivisions = 6;
        sphRenderer.streamSmoothing = 0.45f;
        sphRenderer.streamThinBelowFill = 0.15f;
        sphRenderer.streamStopBelowFill = 0.03f;
        sphRenderer.streamMinWidthRatio = 0.15f;
        sphRenderer.dripRadiusRatio = 0.6f;
        sphRenderer.dripInterval = 0.35f;
        sphRenderer.dripLifetime = 1.2f;
        sphRenderer.dripInitialSpeed = 1.5f;
        sphRenderer.splashPoolSize = 48;
        sphRenderer.splashDropletLifetime = 0.25f;
        sphRenderer.splashInitialSpeed = 20f;
        sphRenderer.splashGravity = 300f;
        
        // 4c. إضافة BoxFluidBoundary
        var boundary = sphSystemGO.AddComponent<BoxFluidBoundary>();
        boundary.solver = fluidSolver;
        boundary.box = boxContainer;
        boundary.restitution = 0.25f;
        boundary.friction = 0.85f;
        boundary.skin = 0.05f;
        
        // 4d. إضافة BoxSimulationController
        var simulationController = sphSystemGO.AddComponent<BoxSimulationController>();
        simulationController.fluidSolver = fluidSolver;
        simulationController.boundary = boundary;
        simulationController.seeder = seeder;
        simulationController.sphRenderer = sphRenderer;
        simulationController.autoRun = true;
        simulationController.particleRenderSize = 1.5f;
        
        // 5. ربط المراجع
        seeder.solver = fluidSolver;
        seeder.box = boxContainer;
        
        // 6. إنشاء كاميرا
        GameObject cameraGO = new GameObject("Main Camera");
        cameraGO.AddComponent<Camera>();
        cameraGO.AddComponent<AudioListener>();
        cameraGO.transform.position = new Vector3(0f, 18f, -55f);
        cameraGO.transform.rotation = Quaternion.Euler(18f, 0f, 0f);
        
        Camera camera = cameraGO.GetComponent<Camera>();
        camera.fieldOfView = 60f;
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 1000f;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.backgroundColor = new Color(0.19215686f, 0.19215686f, 0.19215686f, 1f);
        
        // 7. إنشاء ضوء
        GameObject lightGO = new GameObject("Directional Light");
        Light light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.95686275f, 0.83921567f, 1f);
        light.intensity = 1f;
        lightGO.transform.position = new Vector3(0f, 15f, 0f);
        lightGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        
        // 8. حفظ المشهد
        string scenePath = "Assets/Parthenon/Box.unity";
        bool saved = EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scenePath);
        
        if (saved)
        {
            Debug.Log("[BoxSceneSetup] Scene created and saved to: " + scenePath);
            EditorSceneManager.OpenScene(scenePath);
        }
        else
        {
            Debug.LogError("[BoxSceneSetup] Failed to save scene");
        }
        
        // 9. تحديد الكاميرا ككاميرا رئيسية
        cameraGO.tag = "MainCamera";
        
        // 10. تحديد GameObjects في التسلسل الهرمي
        boxContainerGO.transform.SetSiblingIndex(0);
        sphSystemGO.transform.SetSiblingIndex(1);
        cameraGO.transform.SetSiblingIndex(2);
        lightGO.transform.SetSiblingIndex(3);
    }
    
    [MenuItem("Tools/Setup Box SPH Scene", true, 1000)]
    public static bool ValidateSetupBoxSPHScene()
    {
        return true;
    }
}
#endif

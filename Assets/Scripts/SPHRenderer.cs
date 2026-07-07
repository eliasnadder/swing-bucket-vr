using System.Collections.Generic;
using UnityEngine;

public class SPHRenderer : MonoBehaviour
{
    [Header("References")]
    public SPHFluidSolver solver;
    public PaintEmitter paintEmitter;

    [Header("Visuals")]
    public int initialPoolSize = 512;
    public float particleSize = 0.03f;
    public Material particleMaterial;
    public bool createMaterialIfMissing = true;

    [Header("Air Stream Visuals")]
    public bool showAirStream = true;
    public float streamRadiusMultiplier = 1f;
    public float dropletRadiusMultiplier = 1f;
    public int maxStreamPoints = 32;
    public Material streamMaterial;

    [Header("Stream Smoothing")]
    [Tooltip("\u0646\u0642\u0627\u0637 Catmull-Rom \u0628\u064a\u0646 \u0643\u0644 \u0632\u0648\u062c \u0645\u0646 \u0627\u0644\u0631\u0643\u0627\u0626\u0632 \u2014 \u0643\u0644\u0645\u0627 \u0632\u0627\u062f \u0632\u0627\u062f \u0627\u0644\u0646\u0639\u0648\u0645\u0629")]
    public int streamSubdivisions = 6;
    [Range(0f, 1f), Tooltip("0 = \u0644\u0627 \u062a\u0646\u0639\u064a\u0645 \u0632\u0645\u0627\u0646\u064a (\u064a\u062a\u0628\u0639 \u0627\u0644\u062c\u0633\u064a\u0645\u0627\u062a \u0641\u0648\u0631\u0627\u0643)\u060c 1 = \u062a\u062c\u0645\u064a\u062f. 0.45 \u064a\u0642\u062a\u0644 \u0627\u0644\u062a\u0630\u0628\u0630\u0628 \u0628\u062f\u0648\u0646 \u0623\u0646 \u064a\u062a\u0627\u062e\u0631 \u0643\u062b\u064a\u0631\u0627\u064b")]
    public float streamSmoothing = 0.45f;

    private readonly List<ParticleVisual> visuals = new List<ParticleVisual>();
    private readonly List<Vector3> streamPoints = new List<Vector3>(64);
    private readonly List<Vector3> targetPath = new List<Vector3>(128);
    private readonly List<Vector3> smoothedPath = new List<Vector3>(128);
    private Mesh particleMesh;
    private Material runtimeMaterial;
    private Material runtimeStreamMaterial;
    private LineRenderer streamRenderer;

    [Header("Paint Level Feedback")]
    [Tooltip("\u0646\u0633\u0628\u0629 \u0627\u0644\u0645\u0644\u0621 (0\u20131) \u062f\u0648\u0646 \u0647\u0630\u0627 \u0627\u0644\u062d\u062f \u064a\u0628\u062f\u0623 \u0627\u0644\u062e\u0637 \u0628\u0627\u0644\u062a\u0636\u0627\u0621\u0644")]
    [Range(0f, 1f)] public float streamThinBelowFill = 0.15f;
    [Tooltip("\u0646\u0633\u0628\u0629 \u0627\u0644\u0645\u0644\u0621 (0\u20131) \u062f\u0648\u0646 \u0647\u0630\u0627 \u0627\u0644\u062d\u062f \u064a\u062a\u0648\u0642\u0641 \u0627\u0644\u062e\u0637 \u0627\u0644\u0645\u0633\u062a\u0645\u0631 \u0648\u064a\u062a\u062d\u0648\u0644 \u0644\u0642\u0637\u0631\u0627\u062a")]
    [Range(0f, 1f)] public float streamStopBelowFill = 0.03f;
    [Tooltip("\u0623\u062f\u0646\u0649 \u0639\u0631\u0636 \u0644\u0644\u062e\u0637 \u0643\u0646\u0633\u0628\u0629 \u0645\u0646 \u0627\u0644\u0639\u0631\u0636 \u0627\u0644\u0643\u0627\u0645\u0644 (\u0639\u0646\u062f \u062d\u0627\u0641\u0629 \u0627\u0644\u062a\u062d\u0648\u0644 \u0644\u0644\u0642\u0637\u0631\u0627\u062a)")]
    [Range(0f, 1f)] public float streamMinWidthRatio = 0.15f;
    [Tooltip("\u062d\u062c\u0645 \u0642\u0637\u0631\u0629 \u0627\u0644\u062a\u0642\u0637\u0631 \u0643\u0646\u0633\u0628\u0629 \u0645\u0646 \u0646\u0635\u0641 \u0642\u0637\u0631 \u0627\u0644\u0641\u062a\u062d\u0629")]
    [Range(0.1f, 3f)] public float dripRadiusRatio = 0.6f;
    [Tooltip("\u0627\u0644\u0641\u062a\u0631\u0629 \u0627\u0644\u0632\u0645\u0646\u064a\u0629 \u0628\u064a\u0646 \u0643\u0644 \u0642\u0637\u0631\u0629 (\u062b\u0627\u0646\u064a\u0629) \u0639\u0646\u062f \u062d\u062f \u0627\u0644\u062a\u0642\u0637\u0631")]
    public float dripInterval = 0.35f;
    [Tooltip("\u062d\u064a\u0627\u0629 \u0627\u0644\u0642\u0637\u0631\u0629 (\u062b\u0627\u0646\u064a\u0629)")]
    public float dripLifetime = 1.2f;
    [Tooltip("\u0633\u0631\u0639\u0629 \u0627\u0644\u0642\u0637\u0631\u0629 \u0627\u0644\u0623\u0648\u0644\u064a\u0629 \u0644\u0644\u0623\u0633\u0644")]
    public float dripInitialSpeed = 1.5f;

    // \u0641\u0644\u0627\u062c \u064a\u064f\u0641\u0639\u064e\u0651\u0644 \u0628\u0639\u062f \u0623\u0648\u0644 \u0641\u0631\u064a\u0645 \u0641\u064a\u0647 solver.PaintHeight > 0 \u2014 \u064a\u0645\u0646\u0639 \u0627\u0644\u0642\u0637\u0631\u0627\u062a \u0645\u0646 \u0627\u0644\u0648\u0647\u0648\u0631 \u0642\u0628\u0644 \u0623\u0646 \u064a\u0628\u062f\u0623 \u0627\u0644\u0646\u0638\u0627\u0645
    private bool paintSystemReady;

    private float dripTimer;
    private readonly List<SplashDroplet> dripPool = new List<SplashDroplet>(16);
    private int dripCursor;

    [Header("Canvas Splash (transient, cosmetic)")]
    public int splashPoolSize = 48;
    public float splashDropletLifetime = 0.25f;
    public float splashInitialSpeed = 20f;   // world units/s \u2014 cosmetic
    public float splashGravity = 300f;  // cosmetic, snappier than world g
    private readonly List<SplashDroplet> splashPool = new List<SplashDroplet>();
    private int splashCursor;

    [Header("Enhanced Realism Settings")]
    [Tooltip("Amount of natural wobble/perturbation in the stream (0 = perfectly smooth, 1 = very turbulent)")]
    [Range(0f, 1f)] public float streamTurbulence = 0.15f;
    [Tooltip("How much the stream tapers/narrows as it falls (0 = no tapering, 1 = strong tapering)")]
    [Range(0f, 1f)] public float streamTaperAmount = 0.4f;
    [Tooltip("How much particles vary in size for a more organic look")]
    [Range(0f, 0.5f)] public float particleSizeVariation = 0.1f;
    [Tooltip("Multiplier for particle size based on velocity (faster particles appear slightly stretched)")]
    [Range(0f, 2f)] public float velocitySizeFactor = 0.3f;
    [Tooltip("Adds subtle color variation to particles for depth")]
    [Range(0f, 0.3f)] public float particleColorVariation = 0.05f;
    [Tooltip("Stream curvature based on bucket swing direction")]
    public bool useStreamCurvature = true;
    [Tooltip("How much the stream bends with bucket movement")]
    [Range(0f, 1f)] public float streamCurvatureAmount = 0.3f;

    private class ParticleVisual
    {
        public GameObject gameObject;
        public Renderer renderer;
        public MaterialPropertyBlock block;
    }

    private class SplashDroplet
    {
        public GameObject gameObject;
        public Renderer renderer;
        public MaterialPropertyBlock block;
        public Color color;
        public Vector3 velocity;
        public float baseRadius;
        public float age;
        public float lifetime;
        public bool active;
    }

    private void Awake()
    {
        if (solver == null)
            solver = FindAnyObjectByType<SPHFluidSolver>();
        if (paintEmitter == null)
            paintEmitter = FindAnyObjectByType<PaintEmitter>();

        if (particleMesh == null)
            particleMesh = CreateSphereMesh(8, 12);

        if (particleMaterial == null && createMaterialIfMissing)
            particleMaterial = CreateDefaultMaterial();

        runtimeMaterial = particleMaterial;
        runtimeStreamMaterial = streamMaterial != null ? streamMaterial : CreateStreamMaterial();
        EnsurePool(initialPoolSize);
        EnsureStreamRenderer();
        EnsureSplashPool(Mathf.Max(1, splashPoolSize));
        EnsureDripPool(16);
        SubscribeSplash();
    }

    private void OnDestroy()
    {
        CustomBoundary boundary = FindAnyObjectByType<CustomBoundary>();
        if (boundary != null)
            boundary.OnCanvasImpact -= SpawnSplash;
    }

    private void SubscribeSplash()
    {
        CustomBoundary boundary = FindAnyObjectByType<CustomBoundary>();
        if (boundary != null)
            boundary.OnCanvasImpact += SpawnSplash;
    }

    private void LateUpdate()
    {
        RenderParticles();
        UpdateSplash();
        UpdateDrips();

        // \u062a\u0642\u0637\u0651\u0631 \u0645\u0633\u062a\u0642\u0644 \u0639\u0646 \u0648\u062c\u0648\u062f \u0627\u0644\u062c\u0633\u064a\u0645\u0627\u062a \u2014 \u064a\u0639\u0645\u0644 \u062d\u062a\u0649 \u0644\u0648 \u062a\u0648\u0642\u0641\u062a \u0627\u0644\u062c\u0633\u064e\u0645\u0627\u062a \u0639\u0646 \u0627\u0644\u062e\u0644\u0648\u062c
        if (showAirStream && solver != null && paintEmitter != null)
        {
            float fillRatio = solver.maxPaintHeight > 0f
                ? Mathf.Clamp01(solver.PaintHeight / solver.maxPaintHeight)
                : 0f;

            // \u0627\u0646\u062a\u0638\u0631 \u062d\u062a\u0649 \u064e\u0647\u0644 \u0627\u0644\u0646\u0638\u0627\u0645 \u0645\u0644\u0621\u064b \u062d\u0642\u064a\u0642\u064a\u0627\u064b \u0642\u0628\u0644 \u062a\u0641\u0639\u064a\u0644 \u0627\u0644\u0642\u0637\u0631\u0627\u062a
            // \u0647\u0630\u0627 \u064a\u0645\u0646\u0639 \u0627\u0644\u0642\u0637\u0631\u0627\u062a \u0645\u0646 \u0627\u0644\u0648\u0647\u0648\u0631 \u0641\u064a \u0623\u0648\u0644 \u0641\u0631\u064e\u0645\u0627\u062a \u0642\u0628\u0644 \u0623\u0646 \u064a\u0628\u062f\u0623 SPHFluidSolver.Start()
            if (!paintSystemReady)
            {
                if (fillRatio > streamThinBelowFill)
                    paintSystemReady = true;
                return; // \u0644\u0627 \u0642\u0637\u0631\u0627\u062a \u062d\u062a\u0649 \u064a\u062b\u0628\u062a \u0627\u0644\u0646\u0638\u0627\u0645 \u0639\u0644\u0649 \u0642\u064a\u0645\u0629 \u0645\u0644\u0621 \u0645\u0639\u0642\u0648\u0644\u0629
            }

            UpdateDripEmission(fillRatio);
        }
    }

    public void RenderParticles()
    {
        if (solver == null)
            return;

        EnsurePool(solver.ParticleCount);

        int activeCount = solver.ParticleCount;
        float dropletDiameter = GetHoleRadius() * 2f * Mathf.Max(0.01f, dropletRadiusMultiplier);
        float visualDiameter = Mathf.Max(particleSize, dropletDiameter);

        // Get bucket velocity for motion-based effects
        Vector3 bucketVelocity = Vector3.zero;
        if (paintEmitter != null && paintEmitter.bucket != null)
        {
            bucketVelocity = paintEmitter.bucket.BucketVelocity;
        }

        for (int i = 0; i < activeCount; i++)
        {
            SPHParticle particle = solver.GetParticle(i);
            ParticleVisual visual = visuals[i];

            if (!visual.gameObject.activeSelf)
                visual.gameObject.SetActive(true);

            // Calculate particle scale with variations for realism
            float particleScale = visualDiameter;
            
            // Add random size variation for organic look
            if (particleSizeVariation > 0f)
            {
                float variation = (Hash(i, frameCount) * 2f - 1f) * particleSizeVariation;
                particleScale *= (1f + variation);
            }
            
            // Add velocity-based scaling (faster particles appear slightly stretched in direction of motion)
            if (velocitySizeFactor > 0f && particle.velocity.magnitude > 0.1f)
            {
                float speedFactor = Mathf.Clamp01(particle.velocity.magnitude / 5f);
                particleScale *= (1f + speedFactor * velocitySizeFactor);
            }

            visual.gameObject.transform.position = particle.position;
            visual.gameObject.transform.localScale = Vector3.one * particleScale;

            // Add subtle color variation for depth
            Color finalColor = particle.color;
            if (particleColorVariation > 0f)
            {
                float colorOffset = (Hash(i * 3, frameCount) * 2f - 1f) * particleColorVariation;
                finalColor = Color.Lerp(particle.color, Color.white, colorOffset);
            }

            visual.block.Clear();
            visual.block.SetColor("_BaseColor", finalColor);
            visual.block.SetColor("_Color", finalColor);
            visual.renderer.SetPropertyBlock(visual.block);
        }

        for (int i = activeCount; i < visuals.Count; i++)
        {
            if (visuals[i].gameObject.activeSelf)
                visuals[i].gameObject.SetActive(false);
        }

        RenderAirStream(activeCount, bucketVelocity);
    }

    private void RenderAirStream(int activeCount, Vector3 bucketVelocity)
    {
        if (!showAirStream || streamRenderer == null || paintEmitter == null)
        {
            SetStreamVisible(false);
            return;
        }

        // \u2500\u2500 \u0646\u0633\u0628\u0629 \u0627\u0644\u0645\u0644\u0621: \u0645\u0635\u062f\u0631 \u0627\u0644\u062d\u0642\u064a\u0642\u0629 \u0647\u0648 solver.PaintHeight / maxPaintHeight \u2500\u2500
        float fillRatio = 1f;
        if (solver != null && solver.maxPaintHeight > 0f)
            fillRatio = Mathf.Clamp01(solver.PaintHeight / solver.maxPaintHeight);

        // \u0625\u0630\u0627 \u0644\u0645 \u064e\u0647\u064e\u0647\u064a\u0651\u0623 \u0627\u0644\u0646\u0638\u0627\u0645 \u0628\u0639\u062f (\u0623\u0648\u0644 \u0641\u0631\u064e\u0645\u0627\u062a)\u060c \u0627\u0641\u062a\u0636 \u0645\u0644\u0621 \u0643\u0627\u0645\u0644 \u0644\u062a\u062c\u0646\u0628 \u0638\u0647\u0648\u0631 \u0642\u0637\u0631\u0627\u062a \u062e\u0627\u0637\u0626
        if (!paintSystemReady)
            fillRatio = 1f;

        // \u2500\u2500 \u0627\u0644\u062e\u064e\u064a\u0637 \u064e\u0648\u0647\u0631 \u0641\u0642\u0637 \u0625\u0630\u0627 \u0643\u0627\u0646 \u0627\u0644\u062f\u0644\u0648 \u064e\u0647\u0648 \u0637\u0644\u0627\u0621\u064b \u0641\u0639\u0644\u0627\u064b \u2500\u2500
        // \u0646\u062a\u062d\u0642\u0642 \u0645\u0646 \u0645\u0639\u062f\u0644 \u0627\u0644\u062a\u062f\u0641\u0642: \u0625\u0630\u0627 \u0643\u0627\u0646 \u0635\u0641\u0631\u0627\u064b (\u062f\u0644\u0648 \u0645\u062a\u0648\u0642\u0641 \u0623\u0648 \u0641\u0627\u0631\u063a) \u0646\u062e\u0644\u064a \u0627\u0644\u062e\u064e\u064a\u0637
        // \u062d\u062a\u0649 \u0644\u0648 \u0645\u0627 \u0632\u0627\u0644\u062a \u062a\u0648\u062c\u062f \u062c\u0633\u064e\u0645\u0627\u062a \u0641\u064e\u0645 \u0627\u0644\u0625\u0635\u062f\u0627\u0631 \u0627\u0644\u0633\u0627\u0628\u0642
        float flowRate = solver != null ? solver.CurrentFlowRate : 0f;

        // \u0645\u0639\u064e\u064e\u0627\u064a\u0627\u0631 \u0627\u062d\u062a\u064e\u064e\u0627\u0637\u064e\u064a: \u0633\u0631\u0639\u0629 \u0627\u0644\u062f\u0644\u0648 \u0646\u0641\u0633\u0647 \u2014 \u0639\u0646\u062f\u0645\u0627 \u064e\u064e\u064a\u062a\u062e\u0627\u0645\u062f \u0648\u064e\u064e\u064a\u062a\u0648\u0642\u0641 \u062a\u0635\u0628\u062d \u0627\u0644\u0633\u064e\u064e\u0639\u0629 \u2248 0
        // \u0647\u0630\u0627 \u064e\u064e\u064e\u0639\u0645\u0644 \u062d\u062a\u0649 \u0645\u0639 PaintEmitter \u0627\u0644\u062e\u0627\u0631\u062c\u064e \u0627\u0644\u0630\u064e \u0644\u0627 \u064e\u064e\u064e\u062d\u062f\u0651\u062b CurrentFlowRate
        bool bucketMoving = true;
        if (paintEmitter != null && paintEmitter.bucket != null)
        {
            float bucketSpeed = paintEmitter.bucket.BucketVelocity.magnitude;
            // \u0639\u062a\u0628\u0629 \u0645\u0646\u062e\u0641\u0636\u0629 \u062c\u064e\u064e\u064e\u062f\u064b (0.5 cm/s \u0628\u0648\u062d\u062f\u0627\u062a Unity) \u2014 \u0627\u0644\u062f\u0644\u0648 \u0627\u0644\u0633\u0627\u0643\u0646 \u062a\u0645\u0627\u0645\u0627\u064b \u0623\u0642\u0644 \u0645\u0646 \u0647\u0630\u0627
            bucketMoving = bucketSpeed > 0.5f;
        }

        bool isFlowing = (flowRate > 0.0001f || bucketMoving) && fillRatio > streamStopBelowFill;

        if (!isFlowing || activeCount <= 0)
        {
            SetStreamVisible(false);
            return;
        }

        Vector3 holePosition = paintEmitter.GetHoleWorldPosition();
        float holeRadius = GetHoleRadius();
        float maxY = holePosition.y + holeRadius * 2f;

        // \u2500\u2500 gather anchors: the hole, then every in-flight particle below it \u2500\u2500
        streamPoints.Clear();
        streamPoints.Add(holePosition);
        for (int i = 0; i < activeCount; i++)
        {
            SPHParticle particle = solver.GetParticle(i);
            if (particle.position.y > maxY) continue;
            streamPoints.Add(particle.position);
        }
        if (streamPoints.Count < 2) { SetStreamVisible(false); return; }

        // top \u2192 bottom so the alpha gradient reads vertically (top = at the bucket)
        streamPoints.Sort((a, b) => b.y.CompareTo(a.y));
        int pointLimit = Mathf.Max(2, maxStreamPoints);
        if (streamPoints.Count > pointLimit)
        {
            for (int write = 1; write < pointLimit; write++)
            {
                int read = Mathf.RoundToInt(write * (streamPoints.Count - 1f) / (pointLimit - 1f));
                streamPoints[write] = streamPoints[read];
            }
            streamPoints.RemoveRange(pointLimit, streamPoints.Count - pointLimit);
        }

        // Add natural turbulence/wobble to the stream for realism
        if (streamTurbulence > 0f)
        {
            AddStreamTurbulence(streamPoints, bucketVelocity);
        }

        // Add stream curvature based on bucket movement
        if (useStreamCurvature && streamCurvatureAmount > 0f && bucketVelocity.magnitude > 0.1f)
        {
            AddStreamCurvature(streamPoints, bucketVelocity);
        }

        // \u2500\u2500 smooth: Catmull-Rom through the anchors, then a light temporal lerp \u2500\u2500
        BuildSmoothPath(streamPoints, targetPath, Mathf.Max(1, streamSubdivisions));
        TemporalSmooth(targetPath, streamSmoothing);

        // \u0627\u0644\u0646\u0642\u0637\u0629 \u0627\u0644\u0623\u0648\u0644\u0649 (\u0641\u062a\u062d\u0629 \u0627\u0644\u0633\u0644) \u0644\u0627\u0632\u0645 \u062a\u0643\u0648\u0646 \u062f\u0642\u064a\u0642\u0629 \u062f\u0627\u064a\u0645\u0627\u064b \u0642\u0628\u0644 \u0627\u0644\u062e\u0644\u0648\u062c
        if (smoothedPath.Count > 0 && targetPath.Count > 0)
            smoothedPath[0] = targetPath[0];

        // \u2500\u2500 \u0639\u0631\u0636 \u0627\u0644\u062e\u0637 \u064e\u064e\u064e\u0627\u062a\u064e\u064e\u064e \u0645\u0639 \u0646\u0633\u0628\u0629 \u0627\u0644\u0645\u0644\u0621 \u2500\u2500
        // \u0641\u0648\u0642 streamThinBelowFill: \u0627\u0644\u0639\u0631\u0636 \u0627\u0644\u0643\u0627\u0645\u0644
        // \u0628\u064e\u064e\u064e\u0646 streamStopBelowFill \u0648 streamThinBelowFill: \u064a\u062a\u0636\u0627\u0621\u0644 \u062d\u062a\u0649 streamMinWidthRatio
        float widthT = Mathf.InverseLerp(streamStopBelowFill, streamThinBelowFill, fillRatio);
        float widthScale = Mathf.Lerp(streamMinWidthRatio, 1f, widthT);

        float baseDiameter = holeRadius * 2f * Mathf.Max(0.01f, streamRadiusMultiplier);
        
        // Apply tapering: stream narrows as it falls
        if (streamTaperAmount > 0f && smoothedPath.Count > 1)
        {
            ApplyStreamTaper(smoothedPath, baseDiameter, widthScale);
        }
        else
        {
            float streamDiameter = baseDiameter * widthScale;
            streamRenderer.startWidth = streamDiameter;
            streamRenderer.endWidth = streamDiameter * 0.7f;
        }

        // \u0627\u0644\u0633\u0641\u0627\u0621\u064e\u064e\u064e\u0629 \u0623\u064e\u064e\u064e\u064a\u0636\u0627\u064b \u062a\u062a\u0636\u0627\u0621\u0644 \u0645\u0639 \u0646\u0642\u0635\u0627\u0646 \u0627\u0644\u0645\u0644\u0621
        float alphaTop = Mathf.Lerp(0.2f, 0.95f, widthT);
        float alphaBot = Mathf.Lerp(0.1f, 0.55f, widthT);

        Color streamColor = GetStreamColor();

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(streamColor, 0f),
                new GradientColorKey(streamColor, 1f)
            },
            new[]
            {
                new GradientAlphaKey(alphaTop, 0f),
                new GradientAlphaKey(alphaBot, 1f)
            });
        streamRenderer.colorGradient = gradient;
        streamRenderer.sharedMaterial.color = streamColor;

        streamRenderer.positionCount = smoothedPath.Count;
        streamRenderer.SetPositions(smoothedPath.ToArray());
        SetStreamVisible(true);
    }

    // Add natural turbulence/wobble to the stream
    private void AddStreamTurbulence(List<Vector3> points, Vector3 bucketVelocity)
    {
        if (points.Count < 2) return;
        
        float time = Time.time;
        float turbulenceScale = streamTurbulence * 0.02f;
        
        for (int i = 1; i < points.Count; i++)
        {
            // Calculate turbulence offset based on position and time
            float t = (float)i / (points.Count - 1);
            float noiseX = Mathf.PerlinNoise(time * 0.5f, i * 0.3f) * 2f - 1f;
            float noiseZ = Mathf.PerlinNoise(time * 0.5f + 100f, i * 0.3f) * 2f - 1f;
            
            // Apply turbulence perpendicular to stream direction
            Vector3 streamDir = (points[i] - points[i-1]).normalized;
            Vector3 perpendicular = new Vector3(-streamDir.z, 0, streamDir.x).normalized;
            Vector3 turbulenceOffset = (perpendicular * noiseX + Vector3.Cross(streamDir, perpendicular) * noiseZ) * turbulenceScale;
            
            points[i] += turbulenceOffset;
        }
    }

    // Add curvature to the stream based on bucket movement
    private void AddStreamCurvature(List<Vector3> points, Vector3 bucketVelocity)
    {
        if (points.Count < 2) return;
        
        // Project velocity to horizontal plane
        Vector3 horizontalVel = Vector3.ProjectOnPlane(bucketVelocity, Vector3.up);
        if (horizontalVel.magnitude < 0.1f) return;
        
        horizontalVel.Normalize();
        
        for (int i = 1; i < points.Count; i++)
        {
            float t = (float)i / (points.Count - 1);
            float curveAmount = streamCurvatureAmount * t * (1f - t) * 0.5f;
            
            // Curve the stream in the direction of bucket movement
            Vector3 curveOffset = horizontalVel * curveAmount * 0.1f;
            points[i] += curveOffset;
        }
    }

    // Apply tapering effect to the stream (narrows as it falls)
    private void ApplyStreamTaper(List<Vector3> path, float baseDiameter, float widthScale)
    {
        if (path.Count < 2) return;
        
        // Calculate widths for each segment
        float[] widths = new float[path.Count];
        
        for (int i = 0; i < path.Count; i++)
        {
            float t = (float)i / (path.Count - 1);
            // Taper: wider at top, narrower at bottom
            float taper = 1f - t * streamTaperAmount;
            widths[i] = baseDiameter * widthScale * taper;
        }
        
        // Apply widths to line renderer
        streamRenderer.widthMultiplier = 1f;
        streamRenderer.widthCurve = new AnimationCurve();
        
        for (int i = 0; i < path.Count; i++)
        {
            float normalizedTime = (float)i / (path.Count - 1);
            streamRenderer.widthCurve.AddKey(normalizedTime, widths[i] / baseDiameter);
        }
        
        // Set start and end widths (fallback for older Unity versions)
        streamRenderer.startWidth = widths[0];
        streamRenderer.endWidth = widths[path.Count - 1] * 0.7f;
    }

    // \u2500\u2500 \u062a\u0642\u0637\u0651\u0631 \u062a\u062f\u0631\u064e\u064e\u064e\u062c\u064e\u064e\u064e: \u064e\u064e\u064e\u064e\u064e\u064e\u0627\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u062f \u0645\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u062c\u0631 \u0645\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u0627\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u062f \u0637\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u064e
    private void UpdateDripEmission(float fillRatio)
    {
        // \u0641\u0648\u0642 \u0641\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u062f \u0627\u0644\u0645\u0644\u0621 \u0643\u0644\u0645\u0627 \u0643\u0644\u0645\u0627 \u0642\u0644\u0651 \u0627\u0644\u062d\u0642\u064e\u064e\u064e\u064e\u064e\u064e
        if (fillRatio > streamThinBelowFill || fillRatio <= 0f || paintEmitter == null)
            return;

        // \u0643\u0644\u0645\u0627 \u0642\u0644\u0651 \u0627\u0644\u0645\u0644\u0621 \u0643\u0644\u0645\u0627 \u0642\u0644\u0651 \u0645\u0639\u062f\u0644 \u0627\u0644\u0642\u0637\u0631\u0627\u062a
        float dripRate = Mathf.Lerp(0f, 1f / Mathf.Max(0.05f, dripInterval),
                                    Mathf.InverseLerp(0f, streamThinBelowFill, fillRatio));

        dripTimer += Time.deltaTime * dripRate;

        while (dripTimer >= 1f)
        {
            dripTimer -= 1f;
            SpawnDrip();
        }
    }

    private void SpawnDrip()
    {
        SplashDroplet d = AcquireDrip();
        if (d == null) return;

        float r = GetHoleRadius() * dripRadiusRatio;
        Color c = GetStreamColor();

        // \u0642\u0637\u0631\u0629 \u062a\u0633\u0642\u0637 \u0645\u0646 \u0627\u0644\u0641\u062a\u062d\u0629 \u0648\u0627\u0634\u0631\u0629 \u0645\u0639 \u0631\u062c\u0651\u0629 \u0635\u063e\u064e\u064e\u064e\u0629
        Vector3 pos = paintEmitter.GetHoleWorldPosition();
        Vector3 jitter = new Vector3(
            Random.Range(-r * 0.5f, r * 0.5f),
            0f,
            Random.Range(-r * 0.5f, r * 0.5f));

        d.color = c;
        d.velocity = Vector3.down * dripInitialSpeed + jitter;
        d.baseRadius = r;
        d.age = 0f;
        d.lifetime = dripLifetime * Random.Range(0.8f, 1.2f);
        d.active = true;

        d.gameObject.transform.position = pos + jitter;
        d.gameObject.transform.localScale = Vector3.one * (r * 2f);
        d.block.Clear();
        d.block.SetColor("_BaseColor", c);
        d.block.SetColor("_Color", c);
        d.renderer.SetPropertyBlock(d.block);
        d.gameObject.SetActive(true);
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
            2f * p1 +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    // Builds a C0-continuous Catmull-Rom spline through `anchors` (open ends clamp to the endpoints).
    private void BuildSmoothPath(List<Vector3> anchors, List<Vector3> output, int subdivs)
    {
        output.Clear();
        if (anchors.Count < 2) return;
        output.Add(anchors[0]);
        if (anchors.Count == 2) { output.Add(anchors[1]); return; }
        for (int i = 0; i < anchors.Count - 1; i++)
        {
            Vector3 p0 = anchors[Mathf.Max(0, i - 1)];
            Vector3 p1 = anchors[i];
            Vector3 p2 = anchors[i + 1];
            Vector3 p3 = anchors[Mathf.Min(anchors.Count - 1, i + 2)];
            for (int s = 1; s <= subdivs; s++)
                output.Add(CatmullRom(p0, p1, p2, p3, (float)s / subdivs));
        }
    }

    private void TemporalSmooth(List<Vector3> target, float lerpFactor)
    {
        if (smoothedPath.Count != target.Count)
        {
            smoothedPath.Clear();
            smoothedPath.AddRange(target); // size changed (particle count changed) \u2014 snap, don't smear
            return;
        }
        for (int i = 0; i < target.Count; i++)
            smoothedPath[i] = Vector3.Lerp(smoothedPath[i], target[i], lerpFactor);
    }

    private float GetHoleRadius()
    {
        // ponytail: single source of truth \u2014 solver first, paintEmitter as a fallback only
        if (solver != null)
            return solver.OrificeRadius;

        if (paintEmitter != null)
            return Mathf.Max(0.001f, paintEmitter.holeRadius);

        return Mathf.Max(0.001f, particleSize * 0.5f);
    }

    private Color GetStreamColor()
    {
        if (solver != null)
            return solver.currentPaintColor;
        if (paintEmitter != null)
            return paintEmitter.color;
        return Color.red;
    }

    // Simple hash function for deterministic randomness
    private static int frameCount => Time.frameCount;
    private static float Hash(int seed, int frame)
    {
        unchecked
        {
            int hash = seed * 1103515245 + frame * 12345;
            hash = (hash ^ (hash >> 16)) * 0x45d9f3b;
            hash = (hash ^ (hash >> 16)) * 0x45d9f3b;
            hash = hash ^ (hash >> 16);
            return (float)(hash % 10000) / 10000f;
        }
    }

    [ContextMenu("Verify SOT (color + radius)")]
    private void VerifySOT()
    {
        if (solver == null) { Debug.LogError("[VerifySOT] solver is null \u2014 cannot verify SOTs."); return; }

        float r = GetHoleRadius();
        bool radiusOk = Mathf.Approximately(r, solver.OrificeRadius);
        Debug.Log($"[VerifySOT] radius: renderer={r:F4} solver={solver.OrificeRadius:F4} match={radiusOk}");

        Color cc = GetStreamColor();
        bool colorOk = cc == solver.currentPaintColor;
        Debug.Log($"[VerifySOT] color: renderer=({cc.r:F2},{cc.g:F2},{cc.b:F2}) solver=({solver.currentPaintColor.r:F2},{solver.currentPaintColor.g:F2},{solver.currentPaintColor.b:F2}) match={colorOk}");

        if (radiusOk && colorOk)
            Debug.Log("[VerifySOT] OK \u2014 single source of truth consistent.");
        else
            Debug.LogWarning("[VerifySOT] MISMATCH \u2014 renderer is not reading the solver SOTs.");
    }

    private void EnsureStreamRenderer()
    {
        if (streamRenderer != null)
            return;

        GameObject go = new GameObject("PaintAirStream");
        go.transform.SetParent(transform, false);

        streamRenderer = go.AddComponent<LineRenderer>();
        streamRenderer.sharedMaterial = runtimeStreamMaterial;
        streamRenderer.useWorldSpace = true;
        streamRenderer.numCapVertices = 12;
        streamRenderer.numCornerVertices = 4;
        streamRenderer.alignment = LineAlignment.View;
        streamRenderer.textureMode = LineTextureMode.Stretch;
        streamRenderer.startColor = Color.red;
        streamRenderer.endColor = new Color(1f, 0f, 0f, 0.65f);
        SetStreamVisible(false);
    }

    private void SetStreamVisible(bool visible)
    {
        if (streamRenderer != null && streamRenderer.enabled != visible)
            streamRenderer.enabled = visible;
    }

    private void EnsurePool(int targetCount)
    {
        while (visuals.Count < targetCount)
        {
            GameObject go = new GameObject($"SPHParticle_{visuals.Count}");
            go.transform.SetParent(transform, false);

            MeshFilter meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = particleMesh;

            Renderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = runtimeMaterial;

            ParticleVisual visual = new ParticleVisual
            {
                gameObject = go,
                renderer = renderer,
                block = new MaterialPropertyBlock()
            };

            visuals.Add(visual);
        }
    }

    // \u0623\u0636\u0641 \u0647\u0627\u062f \u0627\u0644\u062f\u0627\u0644\u0629 \u0627\u0644\u062c\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u062f\u064e\u064e\u064e\u064e
    private Material CreateStreamMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Standard");

        Material material = new Material(shader);
        material.color = Color.white;
        return material;
    }

    private Material CreateDefaultMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");

        Material material = new Material(shader);
        material.enableInstancing = true;
        material.color = Color.white;
        return material;
    }

    private Mesh CreateSphereMesh(int latitudeSegments, int longitudeSegments)
    {
        latitudeSegments = Mathf.Max(3, latitudeSegments);
        longitudeSegments = Mathf.Max(3, longitudeSegments);

        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();

        for (int lat = 0; lat <= latitudeSegments; lat++)
        {
            float v = (float)lat / latitudeSegments;
            float theta = v * Mathf.PI;

            for (int lon = 0; lon <= longitudeSegments; lon++)
            {
                float u = (float)lon / longitudeSegments;
                float phi = u * Mathf.PI * 2f;

                float x = Mathf.Sin(theta) * Mathf.Cos(phi);
                float y = Mathf.Cos(theta);
                float z = Mathf.Sin(theta) * Mathf.Sin(phi);

                Vector3 normal = new Vector3(x, y, z);
                vertices.Add(normal * 0.5f);
                normals.Add(normal);
                uvs.Add(new Vector2(u, v));
            }
        }

        int stride = longitudeSegments + 1;
        for (int lat = 0; lat < latitudeSegments; lat++)
        {
            for (int lon = 0; lon < longitudeSegments; lon++)
            {
                int current = lat * stride + lon;
                int next = current + stride;

                triangles.Add(current);
                triangles.Add(next);
                triangles.Add(current + 1);

                triangles.Add(current + 1);
                triangles.Add(next);
                triangles.Add(next + 1);
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = "SPHParticleSphere";
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // \u2500\u2500 Canvas splash: a short ring of cosmetic droplets spawned on impact, decoupled
    //    from the persistent PaintCanvas texture-splat. Fades by shrinking (no transparency
    //    needed \u2014 reuses the existing opaque particle material). Recycled via a pool.
    private void EnsureSplashPool(int count)
    {
        while (splashPool.Count < count)
        {
            GameObject go = new GameObject($"SplashDroplet_{splashPool.Count}");
            go.transform.SetParent(transform, false);
            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = particleMesh; // reuse the same sphere mesh as SPH droplets
            Renderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = runtimeMaterial;
            go.SetActive(false);
            splashPool.Add(new SplashDroplet
            {
                gameObject = go,
                renderer = r,
                block = new MaterialPropertyBlock(),
                active = false
            });
        }
    }

    public void SpawnSplash(Vector3 hitPoint, Color color, Vector3 impactVelocity)
    {
        if (!showAirStream) return; // tie the cosmetic splash to the master "air visuals" toggle

        // count scales with downward impact speed (reuses PaintCanvas's 3 m/s eye-of-the-storm threshold)
        int count = Mathf.Clamp(6 + Mathf.FloorToInt(Mathf.Abs(impactVelocity.y) / 3f), 6, 16);
        float baseRadius = GetHoleRadius() * 0.4f; // ponytail: ~0.4\u00d7 the stream radius for splash droplets
        Color c = color;

        for (int i = 0; i < count; i++)
        {
            SplashDroplet d = AcquireSplashDroplet();
            if (d == null) break; // pool saturated this frame \u2014 spawn fewer, fine

            // ring in the canvas plane (XZ for the default horizontal canvas) + a slight upward bounce
            float angle = (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
            Vector3 ringDir = new Vector3(Mathf.Cos(angle), 0.3f, Mathf.Sin(angle));
            Vector3 vel = ringDir * splashInitialSpeed + Vector3.up * (splashInitialSpeed * 0.4f);

            d.color = c;
            d.velocity = vel;
            d.baseRadius = baseRadius;
            d.age = 0f;
            d.lifetime = splashDropletLifetime * Random.Range(0.8f, 1.2f);
            d.active = true;

            d.gameObject.transform.position = hitPoint;
            d.gameObject.transform.localScale = Vector3.one * (baseRadius * 2f);
            d.block.Clear();
            d.block.SetColor("_BaseColor", c);
            d.block.SetColor("_Color", c); // Standard vs URP shader name
            d.renderer.SetPropertyBlock(d.block);
            d.gameObject.SetActive(true);
        }
    }

    private SplashDroplet AcquireSplashDroplet()
    {
        for (int i = 0; i < splashPool.Count; i++)
        {
            int idx = (splashCursor + i) % splashPool.Count;
            if (!splashPool[idx].active)
            {
                splashCursor = idx + 1;
                return splashPool[idx];
            }
        }
        return null; // all active \u2014 caller skips (ring is just smaller this frame)
    }

    private void UpdateSplash()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        for (int i = 0; i < splashPool.Count; i++)
        {
            SplashDroplet d = splashPool[i];
            if (!d.active) continue;

            d.age += dt;
            if (d.age >= d.lifetime)
            {
                d.active = false;
                d.gameObject.SetActive(false);
                continue;
            }

            d.velocity.y -= splashGravity * dt;
            Vector3 pos = d.gameObject.transform.position + d.velocity * dt;
            d.gameObject.transform.position = pos;

            // fade by shrinking (opaque material \u2014 no blend mode required)
            float t = d.age / d.lifetime;
            float scale = d.baseRadius * 2f * (1f - t);
            d.gameObject.transform.localScale = Vector3.one * Mathf.Max(0f, scale);
        }
    }

    // \u2500\u2500 Drip pool: \u0642\u0637\u0631\u0627\u062a \u0645\u062a\u0642\u0637\u0639\u0629 \u062a\u0633\u0642\u0637 \u0645\u0646 \u0627\u0644\u0641\u062a\u062d\u0629 \u0639\u0646\u062f \u0644\u0646\u0641\u0627\u062f \u0627\u0644\u0637\u0644\u0627\u0621 \u062a\u0642\u0644\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u062c\u064e\u064e\u064e\u064e\u062f \u2500\u2500
    private void EnsureDripPool(int count)
    {
        while (dripPool.Count < count)
        {
            GameObject go = new GameObject($"DripDroplet_{dripPool.Count}");
            go.transform.SetParent(transform, false);
            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = particleMesh;
            Renderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = runtimeMaterial;
            go.SetActive(false);
            dripPool.Add(new SplashDroplet
            {
                gameObject = go,
                renderer = r,
                block = new MaterialPropertyBlock(),
                active = false
            });
        }
    }

    private SplashDroplet AcquireDrip()
    {
        for (int i = 0; i < dripPool.Count; i++)
        {
            int idx = (dripCursor + i) % dripPool.Count;
            if (!dripPool[idx].active)
            {
                dripCursor = idx + 1;
                return dripPool[idx];
            }
        }
        return null;
    }

    private void UpdateDrips()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        for (int i = 0; i < dripPool.Count; i++)
        {
            SplashDroplet d = dripPool[i];
            if (!d.active) continue;

            d.age += dt;
            if (d.age >= d.lifetime)
            {
                d.active = false;
                d.gameObject.SetActive(false);
                continue;
            }

            // \u062c\u0627\u0630\u0628\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u0629 \u0648\u0627\u0642\u0639\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u0629 \u0644\u0644\u0642\u0637\u0631\u0629 + \u062a\u0646\u0627\u0642\u0635 \u0627\u0644\u062d\u062c\u0645 \u0645\u0639 \u0627\u0644\u0648\u0642\u062a \u0644\u062a\u0648\u062d\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u062f\u064e\u064e\u064e\u064e\u062f\u064e\u064e\u064e\u062f\u062f
            d.velocity += Vector3.down * (splashGravity * 0.3f) * dt;
            d.gameObject.transform.position += d.velocity * dt;

            float t = d.age / d.lifetime;
            // \u0627\u0644\u0642\u0637\u0631\u0629 \u062a\u0643\u0628\u0631 \u0642\u0644\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u062d\u0629 (\u062a\u062c\u0645\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u0629) \u062b\u0645 \u062a\u062a\u0646\u0627\u0642\u0635 (\u062a\u0636\u0644\u0628 \u0627\u0644\u0633\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u064e\u0627\u064d)
            float sizeT = t < 0.3f ? Mathf.Lerp(0.6f, 1f, t / 0.3f) : Mathf.Lerp(1f, 0f, (t - 0.3f) / 0.7f);
            float scale = d.baseRadius * 2f * sizeT;
            d.gameObject.transform.localScale = Vector3.one * Mathf.Max(0f, scale);
        }
    }
}

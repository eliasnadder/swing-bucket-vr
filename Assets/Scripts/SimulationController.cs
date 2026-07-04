using System.Collections.Generic;
using UnityEngine;

public class SimulationController : MonoBehaviour
{
    [Header("References")]
    public PaintEmitter paintEmitter;
    public SPHFluidSolver fluidSolver;
    public CustomBoundary boundary;
    public PaintCanvas paintCanvas;
    public SPHRenderer sphRenderer;

    [Header("Simulation")]
    public bool autoRun = true;

    [Header("Multi-Bucket (Section 9 Extra #1)")]
    [Tooltip("Drop extra PaintEmitter GameObjects here for multi-bucket simulation. StepSimulation runs once per frame regardless. Per-emitter colors come from `extraEmitterColors` (paired 1-to-1 by index).")]
    public List<PaintEmitter> extraPaintEmitters = new List<PaintEmitter>();
    [Tooltip("Per-emitter paint color paired 1-to-1 with `extraPaintEmitters` (NOT with the legacy single `paintEmitter`). Each emitter emits particles with its colour every FixedUpdate.")]
    public List<Color> extraEmitterColors = new List<Color>();
    [Tooltip("Colour used for the legacy single `paintEmitter` (treated as element [0] of the unified emitter list). Defaults to red.")]
    public Color singleEmitterColor = Color.red;

    // ── Cached emitter list to avoid GC each frame ──
    private readonly List<PaintEmitter> emitterCache = new List<PaintEmitter>(8);

    private void Awake()
    {
        if (paintEmitter == null)
            paintEmitter = FindAnyObjectByType<PaintEmitter>();
        if (fluidSolver == null)
            fluidSolver = FindAnyObjectByType<SPHFluidSolver>();
        if (boundary == null)
            boundary = FindAnyObjectByType<CustomBoundary>();
        if (paintCanvas == null)
            paintCanvas = FindAnyObjectByType<PaintCanvas>();
        if (sphRenderer == null)
            sphRenderer = FindAnyObjectByType<SPHRenderer>();

        WireDependencies();
    }

    private void WireDependencies()
    {
        if (paintEmitter != null)
            paintEmitter.solver = fluidSolver;

        // Auto-wire every extra emitter to the shared solver (idempotent).
        if (extraPaintEmitters != null)
        {
            for (int i = 0; i < extraPaintEmitters.Count; i++)
            {
                PaintEmitter e = extraPaintEmitters[i];
                if (e != null) e.solver = fluidSolver;
            }
        }

        if (boundary != null)
        {
            boundary.solver = fluidSolver;
            boundary.paintCanvas = paintCanvas;
        }

        if (sphRenderer != null)
            sphRenderer.solver = fluidSolver;
    }

    private void FixedUpdate()
    {
        if (!autoRun) return;

        float dt = Time.fixedDeltaTime;

        BuildEmitterList();

        if (fluidSolver != null)
        {
            for (int i = 0; i < emitterCache.Count; i++)
            {
                PaintEmitter e = emitterCache[i];
                if (e == null) continue;

                // (a) auto-wire
                e.solver = fluidSolver;

                // (b) per-emitter colour → fluidSolver reads at AddParticle
                fluidSolver.ChangePaintColor(GetEmitterColor(i));

                // (c) per-bucket emission, using the per-bucket hole/flow config
                e.Emit(dt);
            }

            fluidSolver.StepSimulation(dt);
        }

        if (boundary != null)
            boundary.ResolveContacts(dt);
        if (Time.frameCount % 30 == 0)
            Debug.Log($"ParticleCount={fluidSolver?.ParticleCount ?? 0}");
    }

    private void BuildEmitterList()
    {
        emitterCache.Clear();
        if (paintEmitter != null) emitterCache.Add(paintEmitter);

        if (extraPaintEmitters != null)
        {
            for (int i = 0; i < extraPaintEmitters.Count; i++)
            {
                PaintEmitter e = extraPaintEmitters[i];
                if (e != null && !emitterCache.Contains(e)) emitterCache.Add(e);
            }
        }
    }

    private Color GetEmitterColor(int idx)
    {
        if (idx == 0) return singleEmitterColor;
        int listIdx = idx - 1;
        if (extraEmitterColors != null && listIdx >= 0 && listIdx < extraEmitterColors.Count)
            return extraEmitterColors[listIdx];
        return Color.red;
    }
}
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

    // ── Section 9 Extra #1 — Multi-Bucket orchestration ──
    // The legacy single `paintEmitter` field above is treated as ELEMENT [0]
    // in the unified emitter list. Drop any additional PaintEmitter GameObjects
    // into `extraPaintEmitters` for true multi-bucket simulation. Pair each
    // entry in `extraPaintEmitters` with a Color in `extraEmitterColors` (1-to-1
    // index alignment; index 0 of the extras pairs with index 0 of the colors,
    // NOT with the legacy `paintEmitter` — the legacy bucket uses
    // `singleEmitterColor` so the existing scene's `paintEmitter.Color` field
    // does not need to be touched).
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

        // Build the unified emitter list: legacy `paintEmitter` first (element [0]),
        // then `extraPaintEmitters` (skipping duplicates). This is the same loop we
        // walk in two places per frame — once here for the per-bucket emission pass
        // (multi-color support), once implicitly inside `WireDependencies` for the
        // static auto-wire.
        BuildEmitterList();

        if (fluidSolver != null)
        {
            // ---- Multi-color + per-bucket emission pass (Section 9 Extra #1) ----
            // For each emitter:
            //   (a) auto-wire `emitter.solver = fluidSolver` (idempotent — safe every frame),
            //   (b) call `solver.ChangePaintColor(getEmitterColor(i))` so that the
            //       very next `AddParticle(pos, vel, color)` call sees the
            //       per-emitter `currentPaintColor` (the SPHFluidSolver reads it at
            //       AddParticle time, as documented in PaintEmitter.Emit),
            //   (c) call `emitter.Emit(dt)` to spawn particles UNDER that colour,
            //       using the bucket's own hole position + velocity.
            // The result is a chain of `ChangePaintColor` → `Emit` so each emitter's
            // particles carry its own colour in the same frame.
            //
            // NOTE on double-emission: the SPHFluidSolver's internal
            // `EmitParticles()` (Torricelli + optional Bernoulli) ALSO runs inside
            // StepSimulation below, using the LAST `currentPaintColor` set above
            // and the SINGLE pendulum registered on the solver. So on the legacy
            // single-pendulum scene we DO double-emit (the per-bucket PaintEmitter
            // emits its share, then the solver-internal Torricelli emits again
            // using the same pendulum). This is the documented trade-off of
            // layering "Multiple Colors Simultaneously" on top of the existing
            // single-pendulum Torricelli path: we can't disable the solver's
            // internal Torricelli emit without modifying SPHFluidSolver.cs, which
            // is off-limits for Person 2. In true multi-bucket configurations
            // each EXTRA bucket's particles only come from its PaintEmitter (the
            // solver's Torricelli is driven by the first registered pendulum
            // only), so the double-emit cost stays roughly constant regardless
            // of how many extra buckets the user adds.
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

            // (d) ONE solver StepSimulation per frame — drives ALL particles
            // (from every emitter) through a single density/force/integrate pass.
            // Per the task spec: we never call StepSimulation per-emitter.
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
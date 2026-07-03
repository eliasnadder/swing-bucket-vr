# Design — Realistic Paint Stream from Bucket to Canvas

**Date:** 2026-07-03
**Branch:** `fixes`
**Goal:** Make the paint stream visibly flowing through air from the bucket's bottom orifice down to the canvas look realistic, match the bucket's paint color, and match the orifice radius.

## Problem Statement

The user added an "air stream" visual but reports three defects:

1. **Color mismatch** — the stream's color does not match the paint color visible inside the bucket.
2. **Radius mismatch** — the stream's thickness does not match the bucket's bottom orifice radius.
3. **Unrealistic** — the stream does not look like a continuous fluid flow.

## Root-Cause Analysis

Each defect maps to a divergent source-of-truth in the existing code:

### Color — two independent color sources
- `SPHRenderer.cs:141` reads `solver.GetParticle(0).color` for the stream color. This is the per-particle color set at emission (`SPHFluidSolver.EmitParticles:250` → `AddParticle(..., currentPaintColor)`), so particle 0's color *should* track `currentPaintColor` — **but** it is brittle (particle 0 can be coalesced/removed, leaving a stale or default color), and it is conceptually indirect.
- `BucketBuilder.cs:298` reads `fluidSystem.currentPaintColor` directly to tint the liquid volume mesh inside the bucket.
- When `ChangePaintColor(c)` is called, `currentPaintColor` updates, the bucket interior updates next frame, but particle 0 may be stale or absent → stream color drifts from bucket color.

**Fix:** Stream (and droplet) color sourced from a single property: `SPHFluidSolver.currentPaintColor` (or a new `CurrentPaintColor` accessor). This is the same value `BucketBuilder` already uses for the visible paint in the bucket, guaranteeing a match.

### Radius — two independent radius sources
- `SPHRenderer.cs:155–158` (`GetHoleRadius`) prefers `PaintEmitter.holeRadius` (default `0.02`), falling back to `solver.orificeDiameter * 0.5`.
- `BucketBuilder.cs:189–198` (`BuildOrifice`) draws the visible orifice cylinder scaled by `fluidSystem.orificeDiameter`.
- `PaintEmitter.holeRadius` is **never linked** to `orificeDiameter`. They are separate Inspector fields with separate defaults. The stream can be drawn at 2 cm radius while the visible hole is 5 cm — visibly disconnected.
- Additionally, `SPHFluidSolver.EmitParticles:244–247` scatters spawn positions in a fixed `±0.02` square (`Random.Range(-0.02f, 0.02f)`) regardless of orifice size — so the *physical* particle cloud radius also does not track the orifice.
- `SPHRenderer.cs:69` (`RenderParticles`) sizes droplet visuals from `GetHoleRadius()` too, so the per-droplet sphere diameter inherits the same wrong source.

**Fix:** Single source-of-truth radius = `orificeDiameter * 0.5`, exposed as `SPHFluidSolver.OrificeRadius`. All three consumers (stream width, droplet sphere diameter, emission scatter) read it.

### Unrealistic stream — polyline through particle positions
- `SPHRenderer.cs:128–138` builds the stream as up to 48 points sampled from live particle positions, subsampled to `maxStreamPoints`, sorted by descending Y, then fed straight to `LineRenderer.SetPositions`.
- Live SPH particles are discrete, unordered, and jitter from frame to frame (the `±0.02` scatter + wind + viscosity). Connecting their sorted positions with straight segments produces a jittery zig-zag, not a smooth column.
- `numCapVertices = 8` is set (`SPHRenderer.cs:174`) so the top/bottom are capped, but the lateral silhouette is still polygonal.

**Fix:** Smooth the stream with a Catmull-Rom spline interpolated between the sorted particle-anchor points before handing them to the `LineRenderer`. Cap vertices raised to 12 for a rounder cross-section. Increase the anchor count and apply a small per-frame temporal smoothing (lerp stored points toward target) to kill jitter.

## Affected Components

| File | Change |
|---|---|
| `SPHFluidSolver.cs` | Add `public float OrificeRadius => Mathf.Max(0.001f, orificeDiameter * 0.5f);` property. Replace the fixed `±0.02` scatter in `EmitParticles` with `Random.insideUnitCircle * OrificeRadius * 0.9f` so the physical particle cloud also matches the orifice. Change the default `orificeDiameter` from `2f` to `0.05f` (5 cm — the `2f` value is wider than the bucket itself and was clearly a stale unit mismatch). |
| `SPHRenderer.cs` | Rewrite `RenderAirStream`: source color from `solver.currentPaintColor` (fall back to `paintEmitter.color` then `GetParticle(0).color` if solver is null); source radius from `solver.OrificeRadius` (fall back through `paintEmitter.holeRadius` as today); build smooth Catmull-Rom spline through sorted anchors; temporal smoothing of positions; vertical alpha gradient via `colorGradient`; `numCapVertices = 12`. Update `GetHoleRadius` to prefer `solver.OrificeRadius`. Update `RenderParticles` droplet diameter to use the same `OrificeRadius`. Append a new **Canvas Splash** visual subsystem (see below). |

`PaintEmitter.cs` is untouched (inactive under `useInternalEmission = true`, Phase 5). Its `holeRadius`/`color` fields remain only as fallbacks for scenes without a solver.

## Canvas Splash Visuals (new)

The user chose "كلاهما: شريط + splash على Canvas" (both stream + canvas splash).

### Existing behavior (kept)
`CustomBoundary.ResolveContacts` already paints a textured splat + secondary droplets onto the `PaintCanvas` texture when a particle crosses the canvas plane (`CustomBoundary.cs:51–85`, `PaintCanvas.QueueSplat`/`ApplySplat`). This is the *persistent* paint deposition and is **not** changed.

### New: transient impact splash particles
A separate, short-lived visual effect fired at the moment of impact, purely cosmetic, decoupled from the texture-splat logic so the splash reads even before/without the texture update.

- Implemented in `SPHRenderer` (it already owns the particle visual pool and a child transform host). A small ring of splash droplets is spawned at each impact point recorded by `CustomBoundary`, expands outward and falls, fades over `~0.25s`, then is recycled.
- **Wiring:** `CustomBoundary.ResolveContacts` already computes `hitPoint` and has `particle.color` + `particle.velocity` in scope. Add a single optional callback `public System.Action<Vector3, Color, Vector3> OnCanvasImpact;` invoked right after `paintCanvas.QueueSplat(...)` at line ~78 (only when `painted` is true). `SPHRenderer.Awake` finds the boundary via `FindAnyObjectByType<CustomBoundary>()` (same pattern it already uses for `solver`/`paintEmitter`) and subscribes `OnCanvasImpact += SpawnSplash`. No change to the persistent splat path.
- Splash count and speed scale with `|impactVelocity.y|`: start at 6 droplets, +1 per `3 m/s` of impact speed (reusing the `impactSpeed > 3f` threshold already in `PaintCanvas.ApplySplat:374`), capped at 16.
- Splash droplets use the same `currentPaintColor` (color SOT) and `OrificeRadius` (radius SOT, scaled down ~0.4× for splash droplets).

This keeps the splash purely additive — the existing texture-splat pipeline is untouched, and the splash disables cleanly if `OnCanvasImpact` has no subscribers.

## Data Flow

```
SPHFluidSolver.currentPaintColor  ──┬──► BucketBuilder.UpdateLiquidVisual (bucket interior color)  [existing]
                                   ├──► SPHRenderer stream color                                [new wiring]
                                   └──► SPHRenderer splash droplet color                       [new]

SPHFluidSolver.orificeDiameter ──► OrificeRadius ──┬──► BucketBuilder.BuildOrifice (visible hole)  [existing]
                                                   ├──► SPHRenderer stream width                  [new wiring]
                                                   ├──► SPHRenderer droplet sphere diameter      [new wiring]
                                                   ├──► SPHFluidSolver.EmitParticles spawn scatter [new]
                                                   └──► SPHRenderer splash droplet radius         [new]

CustomBoundary.ResolveContacts ──► PaintCanvas.QueueSplat (persistent texture)   [existing]
                                └─► OnCanvasImpact callback ─► SPHRenderer.SpawnSplash [new]
```

## Testing / Verification

No test framework in this Unity project. Verification is visual + a self-check:

- **Visual:** Enter Play mode. Confirm (a) stream color matches the paint inside the bucket; (b) stream thickness matches the visible orifice cylinder; (c) stream is a smooth continuous column instead of a zig-zag; (d) on canvas impact, a small向外 splash ring appears and fades within ~0.25s, in addition to the permanent splat.
- **Color-change test:** call `solver.ChangePaintColor(green)` at runtime (via the existing UI color control). Both bucket interior and stream should turn green in the same frame; splash droplets too.
- **Orifice-change test:** change `orificeDiameter` in the Inspector at runtime. Visible hole, stream width, droplet sphere size, and emission spread should all scale together.
- **Self-check (ponytail):** add a tiny `[ContextMenu("VerifySOT")]` on `SPHRenderer` that asserts `Mathf.Approximately(GetHoleRadius(), solver.OrificeRadius)` and logs the color Triple — fails loudly if the SOTs drift apart. One method, no framework.

## Out of Scope (YAGNI)

- Per-particle trailing (TrailRenderer per droplet) — heavy and unrequested.
- Mesh-based fluid ribbon (TubeRenderer / custom mesh) — the smoothed LineRenderer is sufficient and far cheaper.
- Fixing the unit semantics of `orificeDiameter` beyond the default-value swap — the Inspector remains the source of truth for non-default values.
- Changing the persistent `PaintCanvas` splat pipeline (it already works).
- Splash physics (the splash droplets are cosmetic only; they do not re-enter the SPH simulation).

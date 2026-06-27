# Project Context

This file is the quick reference for the `swing-bucket-vr` project.
Read this first before making changes.

## Project Goal

Build a VR paint-bucket simulation with:

- A swinging bucket / pendulum system
- Physically driven paint flow (SPH + Bernoulli + surface tension + coalescence)
- Paint particles and surface interaction (cohesion/adhesion per surface type)
- Multi-bucket, multi-color support
- User controls for the experiment (20 sliders total)
- Output and reporting tools: JSON save, compare, report

## Main References

- Project brief: [مشروع حقائق افتراضية 2025-2026.pdf](./مشروع%20حقائق%20افتراضية%202025-2026.pdf)
- Physics reference: [Swinging Paint Bucket__Physic.pdf](./Swinging%20Paint%20Bucket__Physic.pdf)
- Task breakdown / notes: [VR.md](./VR.md)
- Change log: [LATEST_CHANGES.md](./LATEST_CHANGES.md)
- Paint behavior: [PAINT_BEHAVIOR.md](./PAINT_BEHAVIOR.md)

## Current Implementation

The scene in use is `Assets/Parthenon/Demo.unity` (cm-scale: `UNITS_TO_METERS = 100`).

### Active scripts (new modular system)

| Script | Role |
|--------|------|
| `SwingingCoupledSpringPendulum.cs` | Pendulum with spring-damper rope. Exposes `PivotX`, `PivotY`, `maxSwings`, `BucketVelocity`, `EffectiveGravity`. |
| `BucketBuilder.cs` | Procedural bucket mesh. All dims now in cm. |
| `SPHFluidSolver.cs` | SPH fluid: density, pressure, viscosity, gravity. **New:** Bernoulli flow rate, surface tension, particle coalescence. |
| `SpatialHashGrid.cs` | Neighbor search acceleration for SPH. |
| `SPHKernel.cs` | Poly6, SpikyGradient, ViscosityLaplacian kernels. |
| `PaintEmitter.cs` | Spawns SPH particles from bucket hole. |
| `CustomBoundary.cs` | Canvas collision detection. |
| `PaintCanvas.cs` | Texture-based canvas painting. **New:** cohesion/adhesion per-surface factors (Canvas/Wood/Metal/Paper). |
| `SPHRenderer.cs` | Particle mesh rendering. |
| `SimulationController.cs` | Orchestration. **New:** multi-bucket emitters, multi-color sequencing. |
| `SimulationUIManager.cs` | UI bindings. **New:** 6 sliders (pivot X/Y, bucket radius, swings, canvas W/H). 20 total. |
| `CanvasExporter.cs` | PNG + JSON export. **New:** `exportJsonButton` hook. |
| `ExperimentSaver.cs` | **NEW:** Saves experiment data as JSON (inputs, runtime, particles, spread). |
| `ExperimentComparer.cs` | **NEW:** Side-by-side comparison of two saved experiments. |
| `ReportGenerator.cs` | **NEW:** Markdown report from a saved experiment. |

### Deprecated but still present

| Script | Note |
|--------|------|
| `FluidSPHSystem.cs` | Old SPH — kept for scene compatibility |
| `PaintSurfaceCanvas.cs` | Old canvas — kept for scene compatibility |

## What Is Now Implemented (from plan.md)

- [x] Spring-damper rope behavior
- [x] Bernoulli flow rate (dynamic pressure-based drip)
- [x] Surface tension between particles
- [x] Particle-particle coalescence
- [x] Cohesion & adhesion on canvas (per-surface-type)
- [x] Multiple buckets + colors
- [x] 20 UI slider bindings (14 original + 6 new)
- [x] Save experiment data (JSON)
- [x] Compare experiments (side-by-side)
- [x] Report generation (Markdown)
- [x] Demo.unity unit-scale fixes (cm-scale consistent)

## What Still Needs User Action (Unity Editor)

1. **Wire 6 new sliders** in Inspector: drag Slider + TMPro Text GameObjects into `SimulationUIManager` fields (`xpivotSlider`, `ypivotSlider`, `bucketRadiusSlider`, `numberOfSwingsSlider`, `canvasWidthSlider`, `canvasHeightSlider`).
2. **Add 3 new MonoBehaviours** to scene GameObjects: `ExperimentSaver`, `ExperimentComparer`, `ReportGenerator` each need a Button reference + (for Comparer) an optional Camera.
3. **Multi-bucket setup**: add `PaintEmitter` references to `SimulationController.extraPaintEmitters` + set `extraEmitterColors`.
4. **Play-mode verification**: confirm paint follows bucket, Bernoulli affects drip, cohesion/adhesion visible, JSON export works.

## What Is Still Not Implemented

- Video export (Section 9 Extra #3)
- Swing-count-based damping (using `maxSwings` field — field exists, logic not wired)
- Reduce double-emission (SPHFluidSolver.EmitParticles + PaintEmitter.Emit both running)

## Notes For Future Edits

1. Check this file first.
2. Check `VR.md` for the task list.
3. Check the physics PDF if the change affects formulas or behavior.
4. Prefer updating the active scripts in `Assets/Scripts/` rather than keeping duplicate logic in unused files.
5. Keep `PaintCanvas.TryWorldToPixel` using `Quaternion.Inverse` — never `InverseTransform*` (see decisions.md 2026-06-22).
6. Scene is cm-scale (`UNITS_TO_METERS = 100`). All mesh dimensions in `BucketBuilder` are now in cm.

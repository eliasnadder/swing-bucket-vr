# Project Context

This file is the quick reference for the `swing-bucket-vr` project.
Read this first before making changes.

## Project Goal

Build a VR paint-bucket simulation with:

- A swinging bucket / pendulum system
- Physically driven paint flow
- Paint particles and surface interaction
- User controls for the experiment
- Output and reporting tools for the final submission

## Main References

- Project brief: [مشروع حقائق افتراضية 2025-2026.pdf](./مشروع%20حقائق%20افتراضية%202025-2026.pdf)
- Physics reference: [Swinging Paint Bucket__Physic.pdf](./Swinging%20Paint%20Bucket__Physic.pdf)
- Task breakdown / notes: [VR.md](./VR.md)

## Current Implementation

The scene in use is `Assets/Parthenon/Demo.unity`.

The active scripts currently wired in that scene are:

- `SwingingCoupledSpringPendulum`
- `BucketBuilder`
- `PaintSurfaceCanvas`
- `FluidSPHSystem`
- `SimulationUIManager`

## Important Scripts

- `Assets/Scripts/SwingingCoupledSpringPendulum.cs`
  - This is the main pendulum script currently used by the scene.
  - It includes the spring-damper rope behavior.
  - It exposes:
    - `BucketVelocity`
    - `EffectiveGravity`
    - `AngularAccelerationTheta`

- `Assets/Scripts/FluidSPHSystem.cs`
  - Handles paint flow and particle spawning.
  - Uses `pendulum.EffectiveGravity` for flow rate.
  - Updates bucket mass via `UpdateBucketMass(...)`.

- `Assets/Scripts/PaintSurfaceCanvas.cs`
  - Handles paint collision and texture painting.
  - Supports surface type, tilt angle, spread behavior, and humidity influence.

- `Assets/Scripts/SimulationUIManager.cs`
  - Binds UI sliders, dropdowns, and restart button.
  - Controls pendulum, fluid, environment, and canvas parameters.

- `Assets/Scripts/BucketBuilder.cs`
  - Builds the bucket visuals procedurally at runtime.
  - Syncs the paint surface height with the fluid system.

## What Is Already Present

- Spring-damper rope behavior is already implemented in the active pendulum script.
- Fluid flow and particle emission are implemented.
- Paint texture drawing is implemented.
- UI bindings for most main parameters are implemented.
- The scene already references the active scripts.

## What Is Still Missing Or Needs Verification

Based on `VR.md`, the following items still need attention:

- Live data overlay
- Save experiment data
- Compare experiments
- Final report generation
- Video export
- Possible multi-bucket support, if required by the brief

## UI/Input Ownership

The `UI / Inputs` track now includes support in code for:

- Pivot point position
- Bucket radius
- Number of swings
- Board dimensions

These inputs still need their UI controls wired in the scene if they are not already present.

## Notes For Future Edits

When changing the project:

1. Check this file first.
2. Check `VR.md` for the task list.
3. Check the physics PDF if the change affects formulas or behavior.
4. Prefer updating the active scripts in `Assets/Scripts/` rather than keeping duplicate logic in unused files.

## Current Recommendation

If you want the project to stay easy to maintain, keep this file updated whenever:

- A new system is added
- A script becomes the active implementation
- A requirement from the PDF is completed
- The scene wiring changes

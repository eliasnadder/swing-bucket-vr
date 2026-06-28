# Paint Behavior in the Current Build

This project currently uses a custom SPH-based paint simulation, not Unity Rigidbody physics and not ParticleSystem. The goal is to make the bucket paint feel like a real fluid while still keeping the system simple enough to explain in a university interview.

## What the paint does now

- Paint is emitted as small SPH particles from the bucket hole.
- Each particle has position, velocity, density, pressure, color, and alive state.
- The bucket swings using a custom pendulum script.
- The paint particles fall under gravity, move with fluid pressure and viscosity, and then hit the canvas.
- When a particle reaches the canvas, the system draws a circular paint splat on a `Texture2D`.
- The canvas texture is updated once per frame, not after every particle.

## What I changed

I added a new modular SPH pipeline:

- `SPHParticle.cs`
- `SPHKernel.cs`
- `SpatialHashGrid.cs`
- `SPHFluidSolver.cs`
- `CustomBoundary.cs`
- `PaintCanvas.cs`
- `SwingingCoupledSpringPendulum.cs` (active pendulum with spring-damper rope, RK4 integration)
- `BucketBuilder.cs` (procedural bucket mesh, `[RequireComponent]` of the active pendulum)
- `PaintEmitter.cs`
- `SPHRenderer.cs`
- `SimulationController.cs`
- `SimulationUIManager.cs` (20 slider bindings)
- `CanvasExporter.cs` (PNG + JSON export)

## How the simulation works

### 1. Bucket motion

`SwingingCoupledSpringPendulum` updates the bucket position using a coupled spring-pendulum with RK4 integration:

`angularAcceleration = -(gravity / ropeLength) * sin(angle) - damping * angularVelocity + spring_force + wind`

Then (RK4 integration):

- The solver takes 4 intermediate slope evaluations per timestep
- `angularVelocity` and `angle` are updated with the weighted average

This gives the swinging motion without Rigidbody.

### 2. Paint emission

`PaintEmitter` spawns SPH particles from the bucket hole.

The initial velocity is based on:

- bucket velocity
- downward velocity
- a small random spread

This makes the paint stream look less perfectly straight.

### 3. Fluid simulation

`SPHFluidSolver` computes:

- density
- pressure
- pressure force
- viscosity force
- gravity

It uses:

- Poly6 kernel for density
- Spiky gradient for pressure
- Viscosity laplacian for viscosity

### 4. Neighbor search

`SpatialHashGrid` speeds up the simulation by checking only nearby particles instead of every particle in the scene.

This is important because SPH becomes too slow if every particle checks every other particle.

### 5. Canvas painting

`CustomBoundary` detects when a particle reaches the canvas.

When that happens:

- the particle is clamped near the surface
- its velocity is damped
- a paint splat is queued
- the splat is blended into the texture

`PaintCanvas` converts the world position into texture coordinates and blends the color into the texture buffer.

`Texture2D.Apply()` is called once per frame so the GPU upload stays efficient.

### 6. Particle rendering

`SPHRenderer` displays the particles as small pooled meshes.

It is not using the Unity ParticleSystem.

## Why the paint may still look simple

The current version is educational, not industrial CFD.

It is still a simplified fluid:

- 2.5D by default
- limited particle count
- simple boundary response
- texture-based painting instead of full wet-surface simulation

This is enough to show realistic behavior for a student project, but it is not a full production fluid solver.

## Active system vs legacy system

### Active system (wired in Demo.unity)

- `SPHFluidSolver` — SPH fluid + Bernoulli flow + surface tension + coalescence
- `PaintCanvas` — texture-based canvas painting + cohesion/adhesion per-surface
- `SwingingCoupledSpringPendulum` — spring-damper rope, RK4 integration, wind
- `BucketBuilder` — procedural bucket mesh (`[RequireComponent]` of the pendulum)
- `PaintEmitter` — spawns SPH particles from bucket hole
- `CustomBoundary` — canvas collision detection
- `SPHRenderer` — particle mesh rendering
- `SimulationController` — orchestration + multi-bucket + multi-color
- `SimulationUIManager` — 20 slider bindings

### Legacy (still present, NOT wired in active scene)

- `FluidSPHSystem` — original monolithic SPH fluid (pre-modular)
- `PaintSurfaceCanvas` — old canvas (references FluidSPHSystem.Instance)
- `BucketPendulum` — orphaned simple pendulum (not referenced by any scene object)

The legacy scripts still exist so the project does not break, but the active system is the one used in Demo.unity.

## How to make it look more like real paint

If you want a more realistic look, tune these values:

- Increase `viscosity` for thicker paint.
- Increase `smoothingRadius` for smoother fluid motion.
- Increase `flowRate` to make the bucket pour more paint.
- Decrease `particleInitialSpeed` for a heavier paint stream.
- Increase `paintOpacity` and splat radius slightly for richer canvas coverage.
- Add more particles per second for a denser stream.

## Summary

The current build simulates paint as a custom SPH fluid, emits it from a swinging bucket, and paints it onto a texture canvas. The system is modular, readable, and easy to explain:

- pendulum drives the bucket
- emitter creates paint particles
- SPH solver handles fluid behavior
- boundary detects canvas contact
- canvas stores the final image
- renderer shows the particles


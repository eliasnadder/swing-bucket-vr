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
- `BucketPendulum.cs`
- `PaintEmitter.cs`
- `SPHRenderer.cs`
- `SimulationController.cs`

I also updated:

- `CanvasExporter.cs` so it can export the new `PaintCanvas`
- `FluidSPHSystem.cs` only for compatibility with the old scene
- `BucketBuilder.cs` and older scripts are still present, but they are part of the previous system

## How the simulation works

### 1. Bucket motion

`BucketPendulum` updates the bucket position using a pendulum equation:

`angularAcceleration = -(gravity / ropeLength) * sin(angle) - damping * angularVelocity`

Then:

- `angularVelocity += angularAcceleration * dt`
- `angle += angularVelocity * dt`

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

## Old system vs new system

### Old system

- `FluidSPHSystem`
- `PaintSurfaceCanvas`
- `SwingingCoupledSpringPendulum`

### New system

- `SPHFluidSolver`
- `PaintCanvas`
- `BucketPendulum`
- `PaintEmitter`
- `CustomBoundary`
- `SPHRenderer`
- `SimulationController`

The old scripts still exist so the project does not break immediately, but the new system is the one you should use going forward.

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


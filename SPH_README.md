# SPH Paint Simulation

This project uses Smoothed Particle Hydrodynamics, or SPH, to simulate viscous paint with custom C# code in Unity.

## What SPH is

SPH is a particle-based fluid method. Instead of simulating a fluid on a grid, the fluid is represented by particles. Each particle carries position, velocity, density, pressure, and color. The fluid behavior comes from kernels that estimate how nearby particles influence one another.

## Why spatial hashing is needed

Naive SPH checks every particle against every other particle, which becomes too slow very quickly. Spatial hashing groups particles into grid cells the size of the smoothing radius. Each particle only checks the current cell and its neighboring cells, which reduces neighbor search cost a lot.

## Scripts

### Active (wired in Demo.unity)

- `SPHParticle.cs`: particle data structure
- `SPHKernel.cs`: Poly6, Spiky gradient, and viscosity kernels
- `SpatialHashGrid.cs`: neighbor lookup grid
- `SPHFluidSolver.cs`: density, pressure, force, Bernoulli flow, surface tension, coalescence
- `CustomBoundary.cs`: manual floor/canvas collision and box bounds
- `PaintCanvas.cs`: Texture2D paint rendering, cohesion/adhesion per-surface
- `SwingingCoupledSpringPendulum.cs`: coupled spring-pendulum with RK4 integration, wind
- `BucketBuilder.cs`: procedural bucket mesh (`[RequireComponent]` of pendulum)
- `PaintEmitter.cs`: emits paint particles from the bucket hole
- `SPHRenderer.cs`: renders particles as pooled spheres
- `SimulationController.cs`: connects and steps everything, multi-bucket/multi-color
- `SimulationUIManager.cs`: 20 slider bindings (14 original + 6 new)

### Legacy (present but NOT wired in active scene)

- `BucketPendulum.cs`: orphaned simple pendulum (not referenced by any scene object)
- `FluidSPHSystem.cs`: original monolithic SPH fluid (pre-modular)
- `PaintSurfaceCanvas.cs`: old canvas (references FluidSPHSystem.Instance)

## Formulas used

### Density

`density_i = sum_j mass * W_poly6(r_ij, h)`

### Pressure

`pressure_i = gasConstant * (density_i - restDensity)`

### Pressure force

`F_pressure = -sum_j mass * (p_i + p_j) / (2 * density_j) * grad W_spiky`

### Viscosity force

`F_viscosity = viscosity * sum_j mass * (v_j - v_i) / density_j * laplacian W_viscosity`

### Gravity

Gravity is added as a force proportional to density so the final acceleration becomes gravity after dividing by density.

### Integration

Semi-implicit Euler:

`velocity += acceleration * dt`

`position += velocity * dt`

## Tuning tips

- `viscosity`: higher values make the paint thicker and slower.
- `smoothingRadius`: larger values make the fluid smoother but more expensive.
- `restDensity`: target density of the fluid; higher values make compression harder.
- `flowRate`: controls how quickly paint leaves the bucket.

## Canvas painting

The canvas uses a `Texture2D`. Particles that hit the canvas are converted into texture pixel coordinates and blended into the texture buffer. The texture is uploaded with `Apply()` once per frame, because repeated `Apply()` calls are expensive.

## Default mode

`useFull3D = false` by default. That keeps the motion mostly 2D/2.5D and makes the simulation easier to understand and faster to run.

---

## Air Stream System (updated 2026-07-04)

### SPHRenderer — Paint Level Feedback fields

Added to the `SPHRenderer` Inspector under **Paint Level Feedback**:

```
streamThinBelowFill   (default 0.15)  — fill ratio where stream starts thinning
streamStopBelowFill   (default 0.03)  — fill ratio where stream stops completely
streamMinWidthRatio   (default 0.15)  — minimum width as fraction of full width
dripRadiusRatio       (default 0.60)  — drip size relative to orifice radius
dripInterval          (default 0.35)  — seconds between drips in the drip zone
dripLifetime          (default 1.20)  — drip droplet lifespan in seconds
dripInitialSpeed      (default 1.50)  — initial downward speed of each drip
```

### BucketBuilder — GetBucketWorldBottomCenter()

New private method. Collects world-space `Renderer.bounds` from all renderers under `BucketModel` and returns the world-space bottom-center of the visible mesh. Used to place `PaintSpawnPoint` correctly regardless of FBX pivot or import rotation.

### Stream visibility gate

`RenderAirStream` now requires:
- `isFlowing = (CurrentFlowRate > 0 || BucketVelocity.magnitude > 0.5) && fillRatio > streamStopBelowFill`
- `activeCount > 0`

This causes the stream line to vanish as soon as the bucket stops swinging, while in-flight particles continue their trajectory unaffected.

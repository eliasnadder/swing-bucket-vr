# SPH Paint Simulation

This project uses Smoothed Particle Hydrodynamics, or SPH, to simulate viscous paint with custom C# code in Unity.

## What SPH is

SPH is a particle-based fluid method. Instead of simulating a fluid on a grid, the fluid is represented by particles. Each particle carries position, velocity, density, pressure, and color. The fluid behavior comes from kernels that estimate how nearby particles influence one another.

## Why spatial hashing is needed

Naive SPH checks every particle against every other particle, which becomes too slow very quickly. Spatial hashing groups particles into grid cells the size of the smoothing radius. Each particle only checks the current cell and its neighboring cells, which reduces neighbor search cost a lot.

## Scripts

- `SPHParticle.cs`: particle data structure
- `SPHKernel.cs`: Poly6, Spiky gradient, and viscosity kernels
- `SpatialHashGrid.cs`: neighbor lookup grid
- `SPHFluidSolver.cs`: density, pressure, force, and integration
- `CustomBoundary.cs`: manual floor/canvas collision and box bounds
- `PaintCanvas.cs`: Texture2D paint rendering and batching
- `BucketPendulum.cs`: custom swinging bucket motion
- `PaintEmitter.cs`: emits paint particles from the bucket hole
- `SPHRenderer.cs`: renders particles as pooled spheres
- `SimulationController.cs`: connects and steps everything

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

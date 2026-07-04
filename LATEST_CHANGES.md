# Latest Changes — Paint Stream Visual System

> Date: 2026-07-04 | Branch: fixes

---

## Summary

Three interconnected improvements to the paint stream (air stream) visual that connects the bucket to the canvas: correct spawn position, realistic flow-level feedback, and automatic stream hiding when the bucket stops swinging.

---

## 1. Paint Stream Spawn Position Fix (`BucketBuilder.cs`)

### Problem
The `PaintSpawnPoint` was placed using local-space bounds from `BucketModel`, but the imported FBX had a non-centered pivot and a non-trivial rotation (X: 270°). This caused the stream origin to appear offset — above or to the side of the bucket instead of directly below the hole.

### Root Cause
`CaptureBucketVisualBounds` computes bounds in `BucketModel` local space. The old code then placed `PaintSpawnPoint` as a child of the root `Bucket` GameObject using those local-space values directly, without accounting for the model's rotation or scale mismatch.

### Fix — `GetBucketWorldBottomCenter()` (new method)
```csharp
private Vector3 GetBucketWorldBottomCenter()
{
    // Collects all Renderer.bounds (world space) under BucketModel
    // Returns: center X/Z of the visual mesh, minimum Y + wallThickness
}
```
`PaintSpawnPoint` is now:
- a child of `BucketModel` (inherits its transform hierarchy)
- positioned via `spawn.transform.position = worldBottomCenter` (world-space assignment bypasses local coordinate confusion)
- destroyed and recreated in `BuildImportedBucket()` to prevent stale positions from previous runs

### Result
Stream origin is always directly under the visible bucket hole regardless of import rotation or pivot offset.

---

## 2. Paint Level Feedback (`SPHRenderer.cs`)

### Feature
The stream now reflects how much paint remains in the bucket.

### New Inspector Section: "Paint Level Feedback"

| Field | Default | Description |
|---|---|---|
| `streamThinBelowFill` | 0.15 | Fill ratio below which stream starts to thin |
| `streamStopBelowFill` | 0.03 | Fill ratio below which stream disappears entirely |
| `streamMinWidthRatio` | 0.15 | Minimum width as fraction of full width |
| `dripRadiusRatio` | 0.6 | Drip droplet size relative to orifice radius |
| `dripInterval` | 0.35 s | Time between drips in the drip zone |
| `dripLifetime` | 1.2 s | How long each drip droplet lives |
| `dripInitialSpeed` | 1.5 | Initial downward speed of drip |

### Behavior — Three Phases

| Fill Ratio | Behavior |
|---|---|
| > 15% | Full-width stream, full opacity |
| 3%–15% | Stream thins and fades; intermittent drip droplets appear |
| < 3% | Stream hidden; only isolated drip droplets fall from the hole |

### Implementation Details

**Stream width** scales with `fillRatio`:
```csharp
float widthT = Mathf.InverseLerp(streamStopBelowFill, streamThinBelowFill, fillRatio);
float widthScale = Mathf.Lerp(streamMinWidthRatio, 1f, widthT);
streamDiameter = baseDiameter * widthScale;
```

**Drip droplets** use a recycled pool (`dripPool`, size 16). Each drip:
- spawns at `paintEmitter.GetHoleWorldPosition()` with a small random jitter
- grows slightly at first (simulating paint accumulation), then shrinks to zero
- uses `solver.currentPaintColor` as single source of truth for color

**Startup guard (`paintSystemReady` flag):**  
The system waits until `fillRatio > streamThinBelowFill` before allowing drips. This prevents false drip spawning during the first frames before `SPHFluidSolver.Start()` has initialized `h_paint`.

---

## 3. Stream Hides When Bucket Stops Swinging (`SPHRenderer.cs`)

### Problem
After the bucket's oscillation damped out, the stream LineRenderer stayed visible as long as any SPH particles were still in the air — sometimes for several seconds after the bucket had come to rest.

### Root Cause
`RenderAirStream` only checked `activeCount > 0` (particles still in the air). In-flight particles from the last emission cycle kept the stream visible even though the bucket was no longer flowing.

### Fix — Dual-criterion visibility gate

```csharp
float flowRate = solver != null ? solver.CurrentFlowRate : 0f;

bool bucketMoving = true;
if (paintEmitter != null && paintEmitter.bucket != null)
{
    float bucketSpeed = paintEmitter.bucket.BucketVelocity.magnitude;
    bucketMoving = bucketSpeed > 0.5f;  // threshold: 0.5 world-units/s
}

bool isFlowing = (flowRate > 0.0001f || bucketMoving) && fillRatio > streamStopBelowFill;

if (!isFlowing || activeCount <= 0)
{
    SetStreamVisible(false);
    return;
}
```

**Why two criteria:**
- `CurrentFlowRate` reflects the Torricelli solver's emission rate — but even a stationary bucket produces a nonzero theoretical flow rate from hydrostatic pressure alone, so it never reaches exactly zero.
- `BucketVelocity.magnitude` is the more reliable real-time indicator: when the pendulum has damped out, the velocity drops well below 0.5 and the stream is hidden immediately.

### Result
Stream disappears within one or two frames of the bucket coming to rest. In-flight particles continue to fall and paint normally — only the connecting stream line is hidden.

---

## Files Changed

| File | Changes |
|---|---|
| `Assets/Scripts/BucketBuilder.cs` | `GetBucketWorldBottomCenter()`, world-space spawn, stale-point cleanup in `BuildImportedBucket()` |
| `Assets/Scripts/SPHRenderer.cs` | Paint Level Feedback section, `UpdateDripEmission()`, `SpawnDrip()`, `UpdateDrips()`, drip pool, `paintSystemReady` flag, dual-criterion `isFlowing` gate |

---

## Inspector Quick-Setup (ParticlesRenderer)

After these changes, the following fields are available on the **ParticlesRenderer** GameObject:

```
[Paint Level Feedback]
Stream Thin Below Fill   = 0.15
Stream Stop Below Fill   = 0.03
Stream Min Width Ratio   = 0.15
Drip Radius Ratio        = 0.60
Drip Interval            = 0.35
Drip Lifetime            = 1.20
Drip Initial Speed       = 1.50
```

Raise `streamThinBelowFill` to start thinning earlier. Raise `dripInterval` for fewer, bigger drips near empty.

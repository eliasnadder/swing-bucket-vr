# Realistic Paint Stream (color + radius single-source-of-truth, Catmull-Rom column, canvas splash) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the airborne paint stream flow realistically from the bucket's bottom orifice to the canvas — color matching the paint inside the bucket, thickness matching the visible orifice, as a smooth continuous column instead of a jittery zig-zag — and add a transient canvas-impact splash.

**Architecture:** Collapse the three divergent sources of truth (stream color vs. bucket color; stream radius vs. orifice; polyline vs. fluid) into single sources: `SPHFluidSolver.currentPaintColor` (color SOT) and a new `SPHFluidSolver.OrificeRadius` property (radius SOT, derived from `orificeDiameter`). Smooth the stream with a Catmull-Rom spline through sorted particle anchors + light temporal smoothing. Add canvas splash droplets as a cosmetic, additive subsystem wired through a single `CustomBoundary.OnCanvasImpact` callback (the persistent texture-splat pipeline is untouched). One `[ContextMenu]` self-check guards the SOTs against drift.

**Tech Stack:** Unity 6, C# MonoBehaviours, `LineRenderer`, `MaterialPropertyBlock`, SPH solver (`SPHFluidSolver`/`SPHParticle`/`CustomBoundary`), unity MCP server (`mcp__mcp-unity__*`) for live scene field edits + recompile + console reads.

## Global Constraints

- **No test framework in this project.** Verification per task = `mcp__mcp-unity__recompile_scripts` returns **0 errors** + (for the final task) a `[ContextMenu]` self-check logged to the Console + Play-mode visual checklist. There is no `pytest`/Unity Test Runner step anywhere in this plan.
- **Units are Unity world units** (gravity `y=-981`, `orificeDiameter` historical default `2` was a stale value wider than the bucket itself). The sane proportional value is `0.05` (this is the existing fallback in `BucketBuilder.BuildOrifice`). Set it in both the C# default and the scene instance.
- **Scene edits go through unity MCP** (`update_component` for serialized field values, `save_scene` to persist) — never hand-edit `Demo.unity` YAML directly (it is fragile; a prior phase broke it that way and needed a `SerializedObject` Editor recovery script).
- **Splat size must not regress.** `CustomBoundary.splatRadiusMultiplier` is currently *dead* code (line 70 ignores it). Wiring it + setting the scene's multiplier `10→40` makes the new `orificeDiameter=0.05` produce the same `radiusWorld ≈ 2` as the old `orificeDiameter=2` did (`0.05 × 40 = 2`). Exactly preserves current splat pixel size.
- **Identifier consistency (do not rename across tasks):** radius property = `OrificeRadius`; boundary callback = `OnCanvasImpact` of type `System.Action<Vector3, Color, Vector3>` (hitPoint, color, impactVelocity); splash method = `SpawnSplash`; self-check = `VerifySOT` under `[ContextMenu("Verify SOT (color + radius)")]`.
- **`PaintEmitter.cs`, `PaintCanvas.cs`, `BucketBuilder.cs` are NOT modified.** `paintEmitter`/`PaintEmitter.holeRadius`/`PaintEmitter.color` remain only as fallbacks for scenes without a solver. The persistent `PaintCanvas` splat/`ApplySplat` pipeline is untouched.
- Every commit ends with a clean recompile before the `git commit`.

---

## File Structure

| File | Responsibility | Touched by |
|---|---|---|
| `Assets/Scripts/SPHFluidSolver.cs` | Add `OrificeRadius` SOT property; fix emission scatter to track orifice; change default `orificeDiameter` 2→0.05 | Task 1 |
| `Assets/Scripts/CustomBoundary.cs` | Wire the existing `splatRadiusWorld`/`splatRadiusMultiplier` fields into `radiusWorld`; add `OnCanvasImpact` callback after `QueueSplat` | Task 2 |
| `Assets/Scripts/SPHRenderer.cs` | Rewrite `RenderAirStream` (Catmull-Rom + gradient + temporal smoothing), repoint `GetHoleRadius`/`GetStreamColor` to solver SOTs, raise `numCapVertices`, add `SpawnSplash` subsystem + splash pool + `VerifySOT` | Tasks 3, 4, 5 |
| `Assets/Parthenon/Demo.unity` | Set `SPHFluidSolver.orificeDiameter=0.05`; set `CustomBoundary.splatRadiusMultiplier=40` (via unity MCP, then `save_scene`) | Tasks 1, 2 |

---

### Task 1: SPHFluidSolver — OrificeRadius SOT + emission scatter + default value

**Files:**
- Modify: `Assets/Scripts/SPHFluidSolver.cs:33` (default), `:108-114` (property block), `:242-246` (scatter)
- Scene: `Assets/Parthenon/Demo.unity` — set `SPHFluidSolver.orificeDiameter = 0.05` (via unity MCP)

**Interfaces:**
- Consumes: `orificeDiameter` (existing float field).
- Produces: `public float OrificeRadius => Mathf.Max(0.001f, orificeDiameter * 0.5f);` — read by `SPHRenderer.GetHoleRadius` (Task 3), `SPHRenderer.SpawnSplash` via `GetHoleRadius` (Task 4), and used internally by `EmitParticles` scatter (this task). Also consumed unchanged by `BucketBuilder.BuildOrifice` (visible hole) and the flow-rate math (unchanged).

- [ ] **Step 1: Change the default `orificeDiameter`**

Replace:
```csharp
    [Tooltip("قطر فتحة السطل")]
    public float orificeDiameter = 2f;
```
With:
```csharp
    [Tooltip("قطر فتحة السطل (بوحدات Unity world units) — 0.05 ≈ فتحة متناسبة مع السطل")]
    public float orificeDiameter = 0.05f;
```

- [ ] **Step 2: Add the `OrificeRadius` SOT property**

Replace:
```csharp
    // ── Public read-only properties for UI/BucketBuilder ──
    public float   CurrentVolume        => currentVolume;
    public float   CurrentFlowRate      { get; private set; }
    public float   PaintHeight          => h_paint;
    public float   EffectiveViscosity   => viscosity * Mathf.Exp(K_TEMP * (T_REF - temperature));
    public float   HumiditySpreadFactor => 1f + BETA_H * humidity;
    public int     ActiveParticleCount  => particles.Count;
```
With:
```csharp
    // ── Public read-only properties for UI/BucketBuilder ──
    public float   CurrentVolume        => currentVolume;
    public float   CurrentFlowRate      { get; private set; }
    public float   PaintHeight          => h_paint;
    public float   EffectiveViscosity   => viscosity * Mathf.Exp(K_TEMP * (T_REF - temperature));
    public float   HumiditySpreadFactor => 1f + BETA_H * humidity;
    public int     ActiveParticleCount  => particles.Count;

    /// <summary>نصف قطر الفتحة — مصدر واحد لكل البصريات (الفجوة + التيار + القطرات + الانتشار).
    /// ponytail: one derived property instead of three independent radius fields.</summary>
    public float OrificeRadius => Mathf.Max(0.001f, orificeDiameter * 0.5f);
```

- [ ] **Step 3: Make the emission scatter track the orifice**

In `EmitParticles`, replace the per-particle scatter block:
```csharp
        for (int i = 0; i < spawnCount; i++)
        {
            Vector3 spawnPos = spawnBase + new Vector3(
                Random.Range(-0.02f, 0.02f), 0f,
                Random.Range(-0.02f, 0.02f));
```
With:
```csharp
        for (int i = 0; i < spawnCount; i++)
        {
            // ponytail: scatter scales with the same SOT as the visual hole/stream
            Vector2 jitter = Random.insideUnitCircle * OrificeRadius * 0.9f;
            Vector3 spawnPos = spawnBase + new Vector3(jitter.x, 0f, jitter.y);
```

- [ ] **Step 4: Recompile and confirm clean**

Call `mcp__mcp-unity__recompile_scripts` with `returnWithLogs: true`.
Expected: 0 errors, 0 warnings of the form `CS...`. (Warnings unrelated to these three files are fine.)

- [ ] **Step 5: Set the scene's serialized `orificeDiameter` to 0.05**

Use `mcp__mcp-unity__update_component`:
- `objectPath`: the GameObject named `_SimulationManager` whose component is `SPHFluidSolver` (identified by `m_EditorClassIdentifier: Assembly-CSharp::SPHFluidSolver` in `Demo.unity`).
- `componentName`: `"SPHFluidSolver"`
- `componentData`: `{ "orificeDiameter": 0.05 }`

Then call `mcp__mcp-unity__save_scene` (no args) to persist.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/SPHFluidSolver.cs Assets/Parthenon/Demo.unity
git commit -m "feat(sph): add OrificeRadius SOT, track emission scatter, default orificeDiameter 0.05

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: CustomBoundary — wire splat multiplier to preserve size + add OnCanvasImpact callback

**Files:**
- Modify: `Assets/Scripts/CustomBoundary.cs:7` (new field region), `:67-80` (radiusWorld + callback)
- Scene: `Assets/Parthenon/Demo.unity` — set `CustomBoundary.splatRadiusMultiplier = 40`

**Interfaces:**
- Consumes: existing `splatRadiusWorld` (float, default 0) and `splatRadiusMultiplier` (float) fields; `solver.orificeDiameter`.
- Produces: `public System.Action<Vector3, Color, Vector3> OnCanvasImpact;` — invoked as `OnCanvasImpact.Invoke(hitPoint, particle.color, particle.velocity)` immediately after the persistent `paintCanvas.QueueSplat(...)` call, only when the particle painted. Subscribed by `SPHRenderer.SpawnSplash` in Task 4.

- [ ] **Step 1: Add the `OnCanvasImpact` field**

Replace:
```csharp
    [Header("References")]
    public SPHFluidSolver solver;
    public PaintCanvas paintCanvas;
```
With:
```csharp
    [Header("References")]
    public SPHFluidSolver solver;
    public PaintCanvas paintCanvas;

    [Tooltip("يُستدعى فور اصطدام جسيم بالـ Canvas (نقطة الاصطدام، لون الجسيم، سرعة الجسيم). مشتركون اختياريون مثل SPHRenderer.SpawnSplash. لا شيء بشكل افتراضي.")]
    public System.Action<Vector3, Color, Vector3> OnCanvasImpact;
```

- [ ] **Step 2: Wire the multiplier + invoke the callback**

Replace:
```csharp
                    // radiusWorld = orificeDiameter مباشرة
                    // مطابق index.html: size = holeSize * random(0.5..1)
                    // نمرر قيمة عشوائية بنفس النطاق
                    float orificeD    = solver != null ? solver.orificeDiameter : 0.3f;
                    float radiusWorld = orificeD * (0.5f + Random.value * 0.5f);

                    paintCanvas.QueueSplat(
                        hitPoint,
                        particle.color,
                        radiusWorld,
                        solver != null ? solver.viscosity : 0f,
                        particle.velocity);

                    painted = true;
```
With:
```csharp
                    // radiusWorld: إمّا قيمة ثابتة من splatRadiusWorld، أو orificeDiameter × splatRadiusMultiplier.
                    // ponytail: the multiplier field already existed (default 10) but was dead code;
                    //   wiring it lets orificeDiameter drop to a proportional 0.05 while splats keep size
                    //   (splatRadiusMultiplier=40 ⇒ 0.05×40 = 2 = the old orificeDiameter value).
                    float orificeD = solver != null ? solver.orificeDiameter : 0.3f;
                    float radiusWorld = splatRadiusWorld > 0f
                        ? splatRadiusWorld
                        : orificeD * splatRadiusMultiplier * (0.5f + Random.value * 0.5f);

                    paintCanvas.QueueSplat(
                        hitPoint,
                        particle.color,
                        radiusWorld,
                        solver != null ? solver.viscosity : 0f,
                        particle.velocity);

                    // splash بصري momentarily — منفصل عن البقعة الدائمة أعلاه (لا يمسّ pipeline الـ texture)
                    OnCanvasImpact?.Invoke(hitPoint, particle.color, particle.velocity);

                    painted = true;
```

- [ ] **Step 3: Recompile and confirm clean**

Call `mcp__mcp-unity__recompile_scripts` (`returnWithLogs: true`).
Expected: 0 errors.

- [ ] **Step 4: Set the scene's `splatRadiusMultiplier` to 40**

Use `mcp__mcp-unity__update_component`:
- `objectPath`: the GameObject carrying `CustomBoundary` (the one with `m_EditorClassIdentifier: Assembly-CSharp::CustomBoundary`). If unsure of the path, first call `mcp__mcp-unity__get_scene_info` and locate the boundary object, or target the `_SimulationManager` hierarchy.
- `componentName`: `"CustomBoundary"`
- `componentData`: `{ "splatRadiusMultiplier": 40 }`

Then `mcp__mcp-unity__save_scene`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/CustomBoundary.cs Assets/Parthenon/Demo.unity
git commit -m "feat(boundary): wire splatRadiusMultiplier (preserve splat size) + add OnCanvasImpact callback

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: SPHRenderer — smooth Catmull-Rom stream + solver SOTs for color/radius

**Files:**
- Modify: `Assets/Scripts/SPHRenderer.cs` — fields (`:16-28`), `Awake` unchanged, `LateUpdate` unchanged for this task, `RenderAirStream` (`:98-150`), `GetHoleRadius` (`:152-161`), `EnsureStreamRenderer` `numCapVertices` (`:174`); add helpers `GetStreamColor`, `CatmullRom`, `BuildSmoothPath`, `TemporalSmooth`.

**Interfaces:**
- Consumes: `solver.currentPaintColor` (Color; existing, also used by `BucketBuilder` — this is the color SOT), `solver.OrificeRadius` (from Task 1), `paintEmitter.GetHoleWorldPosition()`, `paintEmitter.color`/`paintEmitter.holeRadius` (fallbacks only).
- Produces: nothing new externally in this task. (`SpawnSplash` and `VerifySOT` are added in Tasks 4–5.)

- [ ] **Step 1: Add smoothing + color fields**

Replace:
```csharp
    [Header("Air Stream Visuals")]
    public bool showAirStream = true;
    public float streamRadiusMultiplier = 1f;
    public float dropletRadiusMultiplier = 1f;
    public int maxStreamPoints = 48;
    public Material streamMaterial;

    private readonly List<ParticleVisual> visuals = new List<ParticleVisual>();
    private readonly List<Vector3> streamPoints = new List<Vector3>(64);
    private Mesh particleMesh;
    private Material runtimeMaterial;
    private Material runtimeStreamMaterial;
    private LineRenderer streamRenderer;
```
With:
```csharp
    [Header("Air Stream Visuals")]
    public bool showAirStream = true;
    public float streamRadiusMultiplier = 1f;
    public float dropletRadiusMultiplier = 1f;
    public int maxStreamPoints = 32;
    public Material streamMaterial;

    [Header("Stream Smoothing")]
    [Tooltip("نقاط Catmull-Rom بين كل زوج من الركائز — كلما زاد زاد النعومة")]
    public int streamSubdivisions = 6;
    [Range(0f, 1f), Tooltip("0 = لا تنعيم زماني (يتبع الجسيمات فوراً)، 1 = تجميد. 0.45 يقتل التذبذب بدون أن يتأخر كثيراً")]
    public float streamSmoothing = 0.45f;

    private readonly List<ParticleVisual> visuals = new List<ParticleVisual>();
    private readonly List<Vector3> streamPoints = new List<Vector3>(64);
    private readonly List<Vector3> targetPath = new List<Vector3>(128);
    private readonly List<Vector3> smoothedPath = new List<Vector3>(128);
    private Mesh particleMesh;
    private Material runtimeMaterial;
    private Material runtimeStreamMaterial;
    private LineRenderer streamRenderer;
```

- [ ] **Step 2: Repoint `GetHoleRadius` to the solver SOT; add `GetStreamColor`**

Replace:
```csharp
    private float GetHoleRadius()
    {
        if (paintEmitter != null)
            return Mathf.Max(0.001f, paintEmitter.holeRadius);

        if (solver != null)
            return Mathf.Max(0.001f, solver.orificeDiameter * 0.5f);

        return Mathf.Max(0.001f, particleSize * 0.5f);
    }
```
With:
```csharp
    private float GetHoleRadius()
    {
        // ponytail: single source of truth — solver first, paintEmitter as a fallback only
        if (solver != null)
            return solver.OrificeRadius;

        if (paintEmitter != null)
            return Mathf.Max(0.001f, paintEmitter.holeRadius);

        return Mathf.Max(0.001f, particleSize * 0.5f);
    }

    private Color GetStreamColor()
    {
        if (solver != null)
            return solver.currentPaintColor;
        if (paintEmitter != null)
            return paintEmitter.color;
        return Color.red;
    }
```

- [ ] **Step 3: Rewrite `RenderAirStream` with a Catmull-Rom column, alpha gradient, temporal smoothing**

Replace the whole method:
```csharp
    private void RenderAirStream(int activeCount)
    {
        if (!showAirStream || streamRenderer == null || paintEmitter == null || activeCount <= 0)
        {
            SetStreamVisible(false);
            return;
        }

        Vector3 holePosition = paintEmitter.GetHoleWorldPosition();
        float holeRadius = GetHoleRadius();
        float maxY = holePosition.y + holeRadius * 2f;

        streamPoints.Clear();
        streamPoints.Add(holePosition);

        for (int i = 0; i < activeCount; i++)
        {
            SPHParticle particle = solver.GetParticle(i);
            if (particle.position.y > maxY)
                continue;

            streamPoints.Add(particle.position);
        }

        if (streamPoints.Count < 2)
        {
            SetStreamVisible(false);
            return;
        }

        streamPoints.Sort((a, b) => b.y.CompareTo(a.y));
        int pointLimit = Mathf.Max(2, maxStreamPoints);
        if (streamPoints.Count > pointLimit)
        {
            for (int write = 1; write < pointLimit; write++)
            {
                int read = Mathf.RoundToInt(write * (streamPoints.Count - 1f) / (pointLimit - 1f));
                streamPoints[write] = streamPoints[read];
            }
            streamPoints.RemoveRange(pointLimit, streamPoints.Count - pointLimit);
        }

        float streamDiameter = holeRadius * 2f * Mathf.Max(0.01f, streamRadiusMultiplier);
        Color streamColor = solver.GetParticle(0).color;
        streamColor.a = 0.95f;
        streamRenderer.startWidth = streamDiameter;
        streamRenderer.endWidth = streamDiameter * 0.65f;
        streamRenderer.startColor = streamColor;
        streamRenderer.endColor = new Color(streamColor.r, streamColor.g, streamColor.b, 0.65f);
        streamRenderer.positionCount = streamPoints.Count;
        streamRenderer.SetPositions(streamPoints.ToArray());
        SetStreamVisible(true);
    }
```
With:
```csharp
    private void RenderAirStream(int activeCount)
    {
        if (!showAirStream || streamRenderer == null || paintEmitter == null || activeCount <= 0)
        {
            SetStreamVisible(false);
            return;
        }

        Vector3 holePosition = paintEmitter.GetHoleWorldPosition();
        float holeRadius = GetHoleRadius();
        float maxY = holePosition.y + holeRadius * 2f;

        // ── gather anchors: the hole, then every in-flight particle below it ──
        streamPoints.Clear();
        streamPoints.Add(holePosition);
        for (int i = 0; i < activeCount; i++)
        {
            SPHParticle particle = solver.GetParticle(i);
            if (particle.position.y > maxY) continue;
            streamPoints.Add(particle.position);
        }
        if (streamPoints.Count < 2) { SetStreamVisible(false); return; }

        // top → bottom so the alpha gradient reads vertically (top = at the bucket)
        streamPoints.Sort((a, b) => b.y.CompareTo(a.y));
        int pointLimit = Mathf.Max(2, maxStreamPoints);
        if (streamPoints.Count > pointLimit)
        {
            for (int write = 1; write < pointLimit; write++)
            {
                int read = Mathf.RoundToInt(write * (streamPoints.Count - 1f) / (pointLimit - 1f));
                streamPoints[write] = streamPoints[read];
            }
            streamPoints.RemoveRange(pointLimit, streamPoints.Count - pointLimit);
        }

        // ── smooth: Catmull-Rom through the anchors, then a light temporal lerp ──
        BuildSmoothPath(streamPoints, targetPath, Mathf.Max(1, streamSubdivisions));
        TemporalSmooth(targetPath, streamSmoothing);

        float streamDiameter = holeRadius * 2f * Mathf.Max(0.01f, streamRadiusMultiplier);
        Color streamColor = GetStreamColor();

        streamRenderer.startWidth = streamDiameter;
        streamRenderer.endWidth   = streamDiameter * 0.7f;

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(streamColor, 0f), // top — full bucket color
                new GradientColorKey(streamColor, 1f)  // bottom — same RGB, lower alpha (below)
            },
            new[]
            {
                new GradientAlphaKey(0.95f, 0f),
                new GradientAlphaKey(0.55f, 1f)
            });
        streamRenderer.colorGradient = gradient;

        streamRenderer.positionCount = smoothedPath.Count;
        streamRenderer.SetPositions(smoothedPath.ToArray());
        SetStreamVisible(true);
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
            2f * p1 +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    // Builds a C0-continuous Catmull-Rom spline through `anchors` (open ends clamp to the endpoints).
    private void BuildSmoothPath(List<Vector3> anchors, List<Vector3> output, int subdivs)
    {
        output.Clear();
        if (anchors.Count < 2) return;
        output.Add(anchors[0]);
        if (anchors.Count == 2) { output.Add(anchors[1]); return; }
        for (int i = 0; i < anchors.Count - 1; i++)
        {
            Vector3 p0 = anchors[Mathf.Max(0, i - 1)];
            Vector3 p1 = anchors[i];
            Vector3 p2 = anchors[i + 1];
            Vector3 p3 = anchors[Mathf.Min(anchors.Count - 1, i + 2)];
            for (int s = 1; s <= subdivs; s++)
                output.Add(CatmullRom(p0, p1, p2, p3, (float)s / subdivs));
        }
    }

    private void TemporalSmooth(List<Vector3> target, float lerpFactor)
    {
        if (smoothedPath.Count != target.Count)
        {
            smoothedPath.Clear();
            smoothedPath.AddRange(target); // size changed (particle count changed) — snap, don't smear
            return;
        }
        for (int i = 0; i < target.Count; i++)
            smoothedPath[i] = Vector3.Lerp(smoothedPath[i], target[i], lerpFactor);
    }
```

- [ ] **Step 4: Raise `numCapVertices` to 12 for a rounder cross-section**

Replace (in `EnsureStreamRenderer`):
```csharp
        streamRenderer.numCapVertices = 8;
        streamRenderer.numCornerVertices = 4;
```
With:
```csharp
        streamRenderer.numCapVertices = 12;
        streamRenderer.numCornerVertices = 4;
```

- [ ] **Step 5: Recompile and confirm clean**

Call `mcp__mcp-unity__recompile_scripts` (`returnWithLogs: true`).
Expected: 0 errors. (`RenderParticles` at `:69` already routes its droplet diameter through `GetHoleRadius()` — no change needed there; it inherits the new solver-first radius automatically.)

- [ ] **Step 6: (Optional early visual check) commit**

Enter Play mode briefly (or have the user) and confirm the stream is a smooth column whose color matches the bucket and whose width matches the visible orifice. Then:

```bash
git add Assets/Scripts/SPHRenderer.cs
git commit -m "feat(renderer): smooth Catmull-Rom stream, color/radius from solver SOTs, alpha gradient, cap 12

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: SPHRenderer — transient canvas splash subsystem (subscribed to OnCanvasImpact)

**Files:**
- Modify: `Assets/Scripts/SPHRenderer.cs` — add splash fields + `SplashDroplet` class + splash pool, subscribe in `Awake`, unsubscribe in `OnDestroy`, `UpdateSplash` call in `LateUpdate`, methods `SpawnSplash`/`AcquireSplashDroplet`/`EnsureSplashPool`/`UpdateSplash`.

**Interfaces:**
- Consumes: `CustomBoundary.OnCanvasImpact` (Task 2), `solver.currentPaintColor` (color SOT, via the callback's `color` argument which equals it at emission), `GetHoleRadius()` (radius SOT, scaled ×0.4 for splash droplets), `particleMesh`/`runtimeMaterial` (existing pool resources).
- Produces: `public void SpawnSplash(Vector3 hitPoint, Color color, Vector3 impactVelocity)` — the handler subscribed to `OnCanvasImpact`.

- [ ] **Step 1: Add splash fields + the `SplashDroplet` class**

Replace:
```csharp
    private readonly List<ParticleVisual> visuals = new List<ParticleVisual>();
    private readonly List<Vector3> streamPoints = new List<Vector3>(64);
    private readonly List<Vector3> targetPath = new List<Vector3>(128);
    private readonly List<Vector3> smoothedPath = new List<Vector3>(128);
    private Mesh particleMesh;
    private Material runtimeMaterial;
    private Material runtimeStreamMaterial;
    private LineRenderer streamRenderer;

    private class ParticleVisual
    {
        public GameObject gameObject;
        public Renderer renderer;
        public MaterialPropertyBlock block;
    }
```
With:
```csharp
    private readonly List<ParticleVisual> visuals = new List<ParticleVisual>();
    private readonly List<Vector3> streamPoints = new List<Vector3>(64);
    private readonly List<Vector3> targetPath = new List<Vector3>(128);
    private readonly List<Vector3> smoothedPath = new List<Vector3>(128);
    private Mesh particleMesh;
    private Material runtimeMaterial;
    private Material runtimeStreamMaterial;
    private LineRenderer streamRenderer;

    [Header("Canvas Splash (transient, cosmetic)")]
    public int   splashPoolSize       = 48;
    public float splashDropletLifetime = 0.25f;
    public float splashInitialSpeed    = 20f;   // world units/s — cosmetic
    public float splashGravity         = 300f;  // cosmetic, snappier than world g
    private readonly List<SplashDroplet> splashPool = new List<SplashDroplet>();
    private int splashCursor;

    private class ParticleVisual
    {
        public GameObject gameObject;
        public Renderer renderer;
        public MaterialPropertyBlock block;
    }

    private class SplashDroplet
    {
        public GameObject gameObject;
        public Renderer renderer;
        public MaterialPropertyBlock block;
        public Color    color;
        public Vector3  velocity;
        public float    baseRadius;
        public float    age;
        public float    lifetime;
        public bool     active;
    }
```

- [ ] **Step 2: Subscribe in `Awake`, unsubscribe in `OnDestroy`**

Replace:
```csharp
        runtimeMaterial = particleMaterial;
        runtimeStreamMaterial = streamMaterial != null ? streamMaterial : CreateDefaultMaterial();
        EnsurePool(initialPoolSize);
        EnsureStreamRenderer();
    }

    private void LateUpdate()
    {
        RenderParticles();
    }
```
With:
```csharp
        runtimeMaterial = particleMaterial;
        runtimeStreamMaterial = streamMaterial != null ? streamMaterial : CreateDefaultMaterial();
        EnsurePool(initialPoolSize);
        EnsureStreamRenderer();
        EnsureSplashPool(Mathf.Max(1, splashPoolSize));
        SubscribeSplash();
    }

    private void OnDestroy()
    {
        CustomBoundary boundary = FindAnyObjectByType<CustomBoundary>();
        if (boundary != null)
            boundary.OnCanvasImpact -= SpawnSplash;
    }

    private void SubscribeSplash()
    {
        CustomBoundary boundary = FindAnyObjectByType<CustomBoundary>();
        if (boundary != null)
            boundary.OnCanvasImpact += SpawnSplash;
    }

    private void LateUpdate()
    {
        RenderParticles();
        UpdateSplash();
    }
```

- [ ] **Step 3: Add the splash pool, spawner, and update**

Append these methods at the end of the class (before the final closing brace, after `CreateSphereMesh`):

```csharp
    // ── Canvas splash: a short ring of cosmetic droplets spawned on impact, decoupled
    //    from the persistent PaintCanvas texture-splat. Fades by shrinking (no transparency
    //    needed — reuses the existing opaque particle material). Recycled via a pool.
    private void EnsureSplashPool(int count)
    {
        while (splashPool.Count < count)
        {
            GameObject go = new GameObject($"SplashDroplet_{splashPool.Count}");
            go.transform.SetParent(transform, false);
            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = particleMesh; // reuse the same sphere mesh as SPH droplets
            Renderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = runtimeMaterial;
            go.SetActive(false);
            splashPool.Add(new SplashDroplet
            {
                gameObject = go,
                renderer   = r,
                block      = new MaterialPropertyBlock(),
                active     = false
            });
        }
    }

    public void SpawnSplash(Vector3 hitPoint, Color color, Vector3 impactVelocity)
    {
        if (!showAirStream) return; // tie the cosmetic splash to the master "air visuals" toggle

        // count scales with downward impact speed (reuses PaintCanvas's 3 m/s eye-of-the-storm threshold)
        int count = Mathf.Clamp(6 + Mathf.FloorToInt(Mathf.Abs(impactVelocity.y) / 3f), 6, 16);
        float baseRadius = GetHoleRadius() * 0.4f; // ponytail: ~0.4× the stream radius for splash droplets
        Color c = color;

        for (int i = 0; i < count; i++)
        {
            SplashDroplet d = AcquireSplashDroplet();
            if (d == null) break; // pool saturated this frame — spawn fewer, fine

            // ring in the canvas plane (XZ for the default horizontal canvas) + a slight upward bounce
            float angle = (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
            Vector3 ringDir = new Vector3(Mathf.Cos(angle), 0.3f, Mathf.Sin(angle));
            Vector3 vel = ringDir * splashInitialSpeed + Vector3.up * (splashInitialSpeed * 0.4f);

            d.color      = c;
            d.velocity   = vel;
            d.baseRadius = baseRadius;
            d.age        = 0f;
            d.lifetime   = splashDropletLifetime * Random.Range(0.8f, 1.2f);
            d.active     = true;

            d.gameObject.transform.position = hitPoint;
            d.gameObject.transform.localScale = Vector3.one * (baseRadius * 2f);
            d.block.Clear();
            d.block.SetColor("_BaseColor", c);
            d.block.SetColor("_Color", c); // Standard vs URP shader name
            d.renderer.SetPropertyBlock(d.block);
            d.gameObject.SetActive(true);
        }
    }

    private SplashDroplet AcquireSplashDroplet()
    {
        for (int i = 0; i < splashPool.Count; i++)
        {
            int idx = (splashCursor + i) % splashPool.Count;
            if (!splashPool[idx].active)
            {
                splashCursor = idx + 1;
                return splashPool[idx];
            }
        }
        return null; // all active — caller skips (ring is just smaller this frame)
    }

    private void UpdateSplash()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        for (int i = 0; i < splashPool.Count; i++)
        {
            SplashDroplet d = splashPool[i];
            if (!d.active) continue;

            d.age += dt;
            if (d.age >= d.lifetime)
            {
                d.active = false;
                d.gameObject.SetActive(false);
                continue;
            }

            d.velocity.y -= splashGravity * dt;
            Vector3 pos = d.gameObject.transform.position + d.velocity * dt;
            d.gameObject.transform.position = pos;

            // fade by shrinking (opaque material — no blend mode required)
            float t = d.age / d.lifetime;
            float scale = d.baseRadius * 2f * (1f - t);
            d.gameObject.transform.localScale = Vector3.one * Mathf.Max(0f, scale);
        }
    }
```

- [ ] **Step 4: Recompile and confirm clean**

Call `mcp__mcp-unity__recompile_scripts` (`returnWithLogs: true`).
Expected: 0 errors. Confirm `showAirStream` now also gates splash spawning (no new toggle field — YAGNI).

- [ ] **Step 5: (Optional early visual check) commit**

Enter Play mode and let particles hit the canvas: a small ring of droplets should burst outward at each impact and shrink away within ~0.25s, in the bucket's paint color, on top of the persistent splat. Then:

```bash
git add Assets/Scripts/SPHRenderer.cs
git commit -m "feat(renderer): transient canvas splash droplets subscribed to CustomBoundary.OnCanvasImpact

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: SPHRenderer — VerifySOT self-check + full Play-mode verification

This is the single runnable check the whole change leaves behind (ponytail rule: one check that fails if the SOTs drift). It must live in `SPHRenderer` because that's where both SOTs are consumed.

**Files:**
- Modify: `Assets/Scripts/SPHRenderer.cs` — append `VerifySOT` method (with `[ContextMenu]`).

**Interfaces:**
- Consumes: `GetHoleRadius()`, `GetStreamColor()`, `solver.OrificeRadius`, `solver.currentPaintColor` (the two SOTs).

- [ ] **Step 1: Add the `VerifySOT` self-check**

Append (inside the class, e.g. right after `GetStreamColor`):

```csharp
    [ContextMenu("Verify SOT (color + radius)")]
    private void VerifySOT()
    {
        if (solver == null) { Debug.LogError("[VerifySOT] solver is null — cannot verify SOTs."); return; }

        float r = GetHoleRadius();
        bool radiusOk = Mathf.Approximately(r, solver.OrificeRadius);
        Debug.Log($"[VerifySOT] radius: renderer={r:F4} solver={solver.OrificeRadius:F4} match={radiusOk}");

        Color cc = GetStreamColor();
        bool colorOk = cc == solver.currentPaintColor;
        Debug.Log($"[VerifySOT] color: renderer=({cc.r:F2},{cc.g:F2},{cc.b:F2}) solver=({solver.currentPaintColor.r:F2},{solver.currentPaintColor.g:F2},{solver.currentPaintColor.b:F2}) match={colorOk}");

        if (radiusOk && colorOk)
            Debug.Log("[VerifySOT] OK — single source of truth consistent.");
        else
            Debug.LogWarning("[VerifySOT] MISMATCH — renderer is not reading the solver SOTs.");
    }
```

- [ ] **Step 2: Recompile and confirm clean**

Call `mcp__mcp-unity__recompile_scripts` (`returnWithLogs: true`).
Expected: 0 errors.

- [ ] **Step 3: Run the self-check and read the Console**

In the Unity Editor (Play mode or Edit mode, with the `SPHRenderer` object selected), right-click the `SPHRenderer` component header in the Inspector → choose **"Verify SOT (color + radius)"**. Then call `mcp__mcp-unity__get_console_logs` (`logType: "info"`, `includeStackTrace: false`).
Expected: a log line `[VerifySOT] OK — single source of truth consistent.` (radius match `true`, color match `true`). If the color-match shows `false` right after a runtime `ChangePaintColor`, the bucket should have updated the same frame — re-run; it must now match.

- [ ] **Step 4: Run the Play-mode visual checklist**

Enter Play mode. Confirm each item (the user is the visual oracle here — no automated assertion):

1. **Stream color** matches the paint visible inside the bucket (both read `currentPaintColor`).
2. **Stream thickness** matches the visible orifice cylinder (`BucketBuilder.BuildOrifice` scales by `orificeDiameter`; the stream reads `OrificeRadius = orificeDiameter/2`).
3. **Stream shape** is a smooth continuous column, not a jittery zig-zag (Catmull-Rom + temporal smoothing).
4. **Canvas impact** shows a small outward splash ring that shrinks away within ~0.25s, in the paint color, *in addition to* the permanent splat.
5. **Color-change test:** use the runtime color control to call `solver.ChangePaintColor(green)` — bucket interior, stream, and splash droplets all turn green in the same frame.
6. **Orifice-change test:** change `orificeDiameter` via the orifice slider at runtime — the visible hole, stream width, droplet sphere size, emission spread, and splash-droplet size all scale together.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/SPHRenderer.cs
git commit -m "feat(renderer): VerifySOT ContextMenu self-check guards color/radius single-source-of-truth

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Self-Review (run before handoff)

**Spec coverage:**
- Color SOT (`currentPaintColor`) → Task 3 (`GetStreamColor`) + Task 4 (splash uses the callback's color = emission `currentPaintColor`). ✓
- Radius SOT (`OrificeRadius`) → Task 1 (property) → consumed by Task 3 (`GetHoleRadius`, `RenderAirStream` width, `RenderParticles` droplet dia via existing call) + Task 4 (`SpawnSplash` ×0.4) + Task 1 scatter. ✓
- Unrealistic zig-zag → Task 3 (Catmull-Rom + temporal smoothing + cap 12). ✓
- Canvas splash (transient, additive) → Task 4 (`SpawnSplash` + pool + `UpdateSplash`) wired via Task 2 (`OnCanvasImpact`). ✓
- `OnCanvasImpact` callback in `CustomBoundary` after `QueueSplat` → Task 2. ✓
- Self-check `[ContextMenu("VerifySOT")]` asserting the SOTs → Task 5. ✓
- Default `orificeDiameter` 2→0.05 → Task 1. ✓
- Out-of-scope items untouched: `PaintEmitter.cs`, `PaintCanvas.cs` `ApplySplat` pipeline, `BucketBuilder.cs` (all referenced, none modified). ✓

**Added beyond the spec (justified, minimal):**
- Wiring `CustomBoundary.splatRadiusMultiplier` (+scene 10→40). **Necessary to avoid a 40× splat-size regression** when `orificeDiameter` drops 2→0.05; the field already existed and was dead code (ladder rung 2, not a new abstraction). Documented inline with a `ponytail:` comment.
- `GetStreamColor()` helper — collapses the brittle `solver.GetParticle(0).color` read into the color SOT. Part of the spec's stated fix.

**Placeholder scan:** none — every step has exact code or an exact unity-MCP call. ✓

**Type consistency:** `OrificeRadius` (float get-only) used consistently in T1/T3; `OnCanvasImpact` is `System.Action<Vector3, Color, Vector3>` in T2 and `SpawnSplash`'s signature matches in T4; `GetStreamColor`/`GetHoleRadius`/`BuildSmoothPath`/`TemporalSmooth`/`CatmullRom` named the same across T3/T5. ✓

---

## Verification strategy recap (no test framework)

- **Per task:** `mcp__mcp-unity__recompile_scripts` → 0 errors, then commit.
- **Whole change:** `VerifySOT` ContextMenu (Task 5 Step 3) is the one automated assertion; it fails loudly if `GetHoleRadius()`/`GetStreamColor()` ever drift from `solver.OrificeRadius`/`solver.currentPaintColor`.
- **Visual correctness:** the Task 5 Step 4 Play-mode checklist, with the user as oracle.

## Out of Scope (YAGNI — not in this plan)

- Per-particle `TrailRenderer` (heavy, unrequested).
- Mesh-based fluid ribbon / `TubeRenderer` (the smoothed `LineRenderer` column is sufficient and cheaper).
- A separate `showCanvasSplash` toggle (splash is gated by the existing `showAirStream` master toggle).
- Re-tuning `particlesPerVolumeUnit` / `maxSpawnPerFrame` to offset the ~35% emission drop from the smaller orifice (the smooth ribbon masks it; `v_out` is unchanged so the stream still reaches the canvas at the same speed). User can dial the slider if a denser column is wanted.
- Touching the persistent `PaintCanvas` splat/`ApplySplat` internals (it works).
- Splash re-entering the SPH simulation (splash droplets are cosmetic only).

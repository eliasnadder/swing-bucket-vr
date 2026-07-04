using System.Collections.Generic;
using UnityEngine;

public class SPHRenderer : MonoBehaviour
{
    [Header("References")]
    public SPHFluidSolver solver;
    public PaintEmitter paintEmitter;

    [Header("Visuals")]
    public int initialPoolSize = 512;
    public float particleSize = 0.03f;
    public Material particleMaterial;
    public bool createMaterialIfMissing = true;

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

    [Header("Paint Level Feedback")]
    [Tooltip("نسبة الملء (0–1) دون هذا الحد يبدأ الخط بالتضاؤل")]
    [Range(0f, 1f)] public float streamThinBelowFill = 0.15f;
    [Tooltip("نسبة الملء (0–1) دون هذا الحد يتوقف الخط المستمر ويتحول لقطرات")]
    [Range(0f, 1f)] public float streamStopBelowFill = 0.03f;
    [Tooltip("أدنى عرض للخط كنسبة من العرض الكامل (عند حافة التحول للقطرات)")]
    [Range(0f, 1f)] public float streamMinWidthRatio = 0.15f;
    [Tooltip("حجم قطرة التقطر كنسبة من نصف قطر الفتحة")]
    [Range(0.1f, 3f)] public float dripRadiusRatio = 0.6f;
    [Tooltip("الفترة الزمنية بين كل قطرة (ثانية) عند حد التقطر")]
    public float dripInterval = 0.35f;
    [Tooltip("حياة القطرة (ثانية)")]
    public float dripLifetime = 1.2f;
    [Tooltip("سرعة القطرة الأولية للأسفل")]
    public float dripInitialSpeed = 1.5f;

    // فلاج يُفعَّل بعد أول فريم فيه solver.PaintHeight > 0 — يمنع القطرات من الظهور قبل أن يبدأ النظام
    private bool paintSystemReady;

    private float dripTimer;
    private readonly List<SplashDroplet> dripPool = new List<SplashDroplet>(16);
    private int dripCursor;

    [Header("Canvas Splash (transient, cosmetic)")]
    public int splashPoolSize = 48;
    public float splashDropletLifetime = 0.25f;
    public float splashInitialSpeed = 20f;   // world units/s — cosmetic
    public float splashGravity = 300f;  // cosmetic, snappier than world g
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
        public Color color;
        public Vector3 velocity;
        public float baseRadius;
        public float age;
        public float lifetime;
        public bool active;
    }

    private void Awake()
    {
        if (solver == null)
            solver = FindAnyObjectByType<SPHFluidSolver>();
        if (paintEmitter == null)
            paintEmitter = FindAnyObjectByType<PaintEmitter>();

        if (particleMesh == null)
            particleMesh = CreateSphereMesh(8, 12);

        if (particleMaterial == null && createMaterialIfMissing)
            particleMaterial = CreateDefaultMaterial();

        runtimeMaterial = particleMaterial;
        runtimeStreamMaterial = streamMaterial != null ? streamMaterial : CreateStreamMaterial();
        EnsurePool(initialPoolSize);
        EnsureStreamRenderer();
        EnsureSplashPool(Mathf.Max(1, splashPoolSize));
        EnsureDripPool(16);
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
        UpdateDrips();

        // تقطّر مستقل عن وجود الجسيمات — يعمل حتى لو توقفت الجسيمات عن الخروج
        if (showAirStream && solver != null && paintEmitter != null)
        {
            float fillRatio = solver.maxPaintHeight > 0f
                ? Mathf.Clamp01(solver.PaintHeight / solver.maxPaintHeight)
                : 0f;

            // انتظر حتى يُسجّل النظام ملءً حقيقياً قبل تفعيل القطرات
            // هذا يمنع القطرات من الظهور في أول فريم قبل أن يبدأ SPHFluidSolver.Start()
            if (!paintSystemReady)
            {
                if (fillRatio > streamThinBelowFill)
                    paintSystemReady = true;
                return; // لا قطرات حتى يثبت النظام على قيمة ملء معقولة
            }

            UpdateDripEmission(fillRatio);
        }
    }

    public void RenderParticles()
    {
        if (solver == null)
            return;

        EnsurePool(solver.ParticleCount);

        int activeCount = solver.ParticleCount;
        float dropletDiameter = GetHoleRadius() * 2f * Mathf.Max(0.01f, dropletRadiusMultiplier);
        float visualDiameter = Mathf.Max(particleSize, dropletDiameter);

        for (int i = 0; i < activeCount; i++)
        {
            SPHParticle particle = solver.GetParticle(i);
            ParticleVisual visual = visuals[i];

            if (!visual.gameObject.activeSelf)
                visual.gameObject.SetActive(true);

            visual.gameObject.transform.position = particle.position;
            visual.gameObject.transform.localScale = Vector3.one * visualDiameter;

            visual.block.Clear();
            visual.block.SetColor("_BaseColor", particle.color);
            visual.block.SetColor("_Color", particle.color);
            visual.renderer.SetPropertyBlock(visual.block);
        }

        for (int i = activeCount; i < visuals.Count; i++)
        {
            if (visuals[i].gameObject.activeSelf)
                visuals[i].gameObject.SetActive(false);
        }

        RenderAirStream(activeCount);
    }

    private void RenderAirStream(int activeCount)
    {
        if (!showAirStream || streamRenderer == null || paintEmitter == null)
        {
            SetStreamVisible(false);
            return;
        }

        // ── نسبة الملء: مصدر الحقيقة هو solver.PaintHeight / maxPaintHeight ──
        float fillRatio = 1f;
        if (solver != null && solver.maxPaintHeight > 0f)
            fillRatio = Mathf.Clamp01(solver.PaintHeight / solver.maxPaintHeight);

        // إذا لم يُهيّأ النظام بعد (أول فريمات)، افترض ملء كامل لتجنب ظهور قطرات خاطئ
        if (!paintSystemReady)
            fillRatio = 1f;

        // ── الخيط يظهر فقط إذا كان الدلو يُصدر طلاءً فعلاً ──
        // نتحقق من معدل التدفق: إذا كان صفراً (دلو متوقف أو فارغ) نخفي الخيط
        // حتى لو ما زالت توجد جسيمات في الهواء من الإصدار السابق
        float flowRate = solver != null ? solver.CurrentFlowRate : 0f;

        // معيار احتياطي: سرعة الدلو نفسه — عندما يتخامد ويتوقف تصبح السرعة ≈ 0
        // هذا يعمل حتى مع PaintEmitter الخارجي الذي لا يحدّث CurrentFlowRate
        bool bucketMoving = true;
        if (paintEmitter != null && paintEmitter.bucket != null)
        {
            float bucketSpeed = paintEmitter.bucket.BucketVelocity.magnitude;
            // عتبة منخفضة جداً (0.5 cm/s بوحدات Unity) — الدلو الساكن تماماً أقل من هذا
            bucketMoving = bucketSpeed > 0.5f;
        }

        bool isFlowing = (flowRate > 0.0001f || bucketMoving) && fillRatio > streamStopBelowFill;

        if (!isFlowing || activeCount <= 0)
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

        // النقطة الأولى (فتحة السطل) لازم تكون دقيقة دايماً، بدون lag
        if (smoothedPath.Count > 0 && targetPath.Count > 0)
            smoothedPath[0] = targetPath[0];

        // ── عرض الخط يتناسب مع نسبة الملء ──
        // فوق streamThinBelowFill: العرض الكامل
        // بين streamStopBelowFill و streamThinBelowFill: يتضاءل حتى streamMinWidthRatio
        float widthT = Mathf.InverseLerp(streamStopBelowFill, streamThinBelowFill, fillRatio);
        float widthScale = Mathf.Lerp(streamMinWidthRatio, 1f, widthT);

        float baseDiameter = holeRadius * 2f * Mathf.Max(0.01f, streamRadiusMultiplier);
        float streamDiameter = baseDiameter * widthScale;

        // الشفافية أيضاً تتضاؤل مع نقصان الملء
        float alphaTop = Mathf.Lerp(0.2f, 0.95f, widthT);
        float alphaBot = Mathf.Lerp(0.1f, 0.55f, widthT);

        Color streamColor = GetStreamColor();

        streamRenderer.startWidth = streamDiameter;
        streamRenderer.endWidth = streamDiameter * 0.7f;

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(streamColor, 0f),
                new GradientColorKey(streamColor, 1f)
            },
            new[]
            {
                new GradientAlphaKey(alphaTop, 0f),
                new GradientAlphaKey(alphaBot, 1f)
            });
        streamRenderer.colorGradient = gradient;
        streamRenderer.sharedMaterial.color = streamColor;

        streamRenderer.positionCount = smoothedPath.Count;
        streamRenderer.SetPositions(smoothedPath.ToArray());
        SetStreamVisible(true);
    }

    // ── تقطّر تدريجي: يُصدر قطرات متقطعة بدل الخط عند الحد الأدنى ──
    private void UpdateDripEmission(float fillRatio)
    {
        // فقط في المنطقة بين الإيقاف الكلي وحد التضاؤل
        if (fillRatio > streamThinBelowFill || fillRatio <= 0f || paintEmitter == null)
            return;

        // كلما قلّ الملء كلما قلّ معدل القطرات
        float dripRate = Mathf.Lerp(0f, 1f / Mathf.Max(0.05f, dripInterval),
                                    Mathf.InverseLerp(0f, streamThinBelowFill, fillRatio));

        dripTimer += Time.deltaTime * dripRate;

        while (dripTimer >= 1f)
        {
            dripTimer -= 1f;
            SpawnDrip();
        }
    }

    private void SpawnDrip()
    {
        SplashDroplet d = AcquireDrip();
        if (d == null) return;

        float r = GetHoleRadius() * dripRadiusRatio;
        Color c = GetStreamColor();

        // قطرة تسقط من الفتحة مباشرة مع رجّة صغيرة
        Vector3 pos = paintEmitter.GetHoleWorldPosition();
        Vector3 jitter = new Vector3(
            Random.Range(-r * 0.5f, r * 0.5f),
            0f,
            Random.Range(-r * 0.5f, r * 0.5f));

        d.color = c;
        d.velocity = Vector3.down * dripInitialSpeed + jitter;
        d.baseRadius = r;
        d.age = 0f;
        d.lifetime = dripLifetime * Random.Range(0.8f, 1.2f);
        d.active = true;

        d.gameObject.transform.position = pos + jitter;
        d.gameObject.transform.localScale = Vector3.one * (r * 2f);
        d.block.Clear();
        d.block.SetColor("_BaseColor", c);
        d.block.SetColor("_Color", c);
        d.renderer.SetPropertyBlock(d.block);
        d.gameObject.SetActive(true);
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

    private void EnsureStreamRenderer()
    {
        if (streamRenderer != null)
            return;

        GameObject go = new GameObject("PaintAirStream");
        go.transform.SetParent(transform, false);

        streamRenderer = go.AddComponent<LineRenderer>();
        streamRenderer.sharedMaterial = runtimeStreamMaterial;
        streamRenderer.useWorldSpace = true;
        streamRenderer.numCapVertices = 12;
        streamRenderer.numCornerVertices = 4;
        streamRenderer.alignment = LineAlignment.View;
        streamRenderer.textureMode = LineTextureMode.Stretch;
        streamRenderer.startColor = Color.red;
        streamRenderer.endColor = new Color(1f, 0f, 0f, 0.65f);
        SetStreamVisible(false);
    }

    private void SetStreamVisible(bool visible)
    {
        if (streamRenderer != null && streamRenderer.enabled != visible)
            streamRenderer.enabled = visible;
    }

    private void EnsurePool(int targetCount)
    {
        while (visuals.Count < targetCount)
        {
            GameObject go = new GameObject($"SPHParticle_{visuals.Count}");
            go.transform.SetParent(transform, false);

            MeshFilter meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = particleMesh;

            Renderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = runtimeMaterial;

            ParticleVisual visual = new ParticleVisual
            {
                gameObject = go,
                renderer = renderer,
                block = new MaterialPropertyBlock()
            };

            visuals.Add(visual);
        }
    }

    // أضف هاد الدالة الجديدة
    private Material CreateStreamMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Standard");

        Material material = new Material(shader);
        material.color = Color.white;
        return material;
    }

    private Material CreateDefaultMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");

        Material material = new Material(shader);
        material.enableInstancing = true;
        material.color = Color.white;
        return material;
    }

    private Mesh CreateSphereMesh(int latitudeSegments, int longitudeSegments)
    {
        latitudeSegments = Mathf.Max(3, latitudeSegments);
        longitudeSegments = Mathf.Max(3, longitudeSegments);

        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();

        for (int lat = 0; lat <= latitudeSegments; lat++)
        {
            float v = (float)lat / latitudeSegments;
            float theta = v * Mathf.PI;

            for (int lon = 0; lon <= longitudeSegments; lon++)
            {
                float u = (float)lon / longitudeSegments;
                float phi = u * Mathf.PI * 2f;

                float x = Mathf.Sin(theta) * Mathf.Cos(phi);
                float y = Mathf.Cos(theta);
                float z = Mathf.Sin(theta) * Mathf.Sin(phi);

                Vector3 normal = new Vector3(x, y, z);
                vertices.Add(normal * 0.5f);
                normals.Add(normal);
                uvs.Add(new Vector2(u, v));
            }
        }

        int stride = longitudeSegments + 1;
        for (int lat = 0; lat < latitudeSegments; lat++)
        {
            for (int lon = 0; lon < longitudeSegments; lon++)
            {
                int current = lat * stride + lon;
                int next = current + stride;

                triangles.Add(current);
                triangles.Add(next);
                triangles.Add(current + 1);

                triangles.Add(current + 1);
                triangles.Add(next);
                triangles.Add(next + 1);
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = "SPHParticleSphere";
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

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
                renderer = r,
                block = new MaterialPropertyBlock(),
                active = false
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

            d.color = c;
            d.velocity = vel;
            d.baseRadius = baseRadius;
            d.age = 0f;
            d.lifetime = splashDropletLifetime * Random.Range(0.8f, 1.2f);
            d.active = true;

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

    // ── Drip pool: قطرات متقطعة تسقط من الفتحة عند نفاد الطلاء تقريباً ──
    private void EnsureDripPool(int count)
    {
        while (dripPool.Count < count)
        {
            GameObject go = new GameObject($"DripDroplet_{dripPool.Count}");
            go.transform.SetParent(transform, false);
            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = particleMesh;
            Renderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = runtimeMaterial;
            go.SetActive(false);
            dripPool.Add(new SplashDroplet
            {
                gameObject = go,
                renderer = r,
                block = new MaterialPropertyBlock(),
                active = false
            });
        }
    }

    private SplashDroplet AcquireDrip()
    {
        for (int i = 0; i < dripPool.Count; i++)
        {
            int idx = (dripCursor + i) % dripPool.Count;
            if (!dripPool[idx].active)
            {
                dripCursor = idx + 1;
                return dripPool[idx];
            }
        }
        return null;
    }

    private void UpdateDrips()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        for (int i = 0; i < dripPool.Count; i++)
        {
            SplashDroplet d = dripPool[i];
            if (!d.active) continue;

            d.age += dt;
            if (d.age >= d.lifetime)
            {
                d.active = false;
                d.gameObject.SetActive(false);
                continue;
            }

            // جاذبية واقعية للقطرة + تناقص الحجم مع الوقت لتوحي بالتبخر/الاندماج
            d.velocity += Vector3.down * (splashGravity * 0.3f) * dt;
            d.gameObject.transform.position += d.velocity * dt;

            float t = d.age / d.lifetime;
            // القطرة تكبر قليلاً في البداية (تجمّع) ثم تتناقص (تضرب السطح أو تتبخر)
            float sizeT = t < 0.3f ? Mathf.Lerp(0.6f, 1f, t / 0.3f) : Mathf.Lerp(1f, 0f, (t - 0.3f) / 0.7f);
            float scale = d.baseRadius * 2f * sizeT;
            d.gameObject.transform.localScale = Vector3.one * Mathf.Max(0f, scale);
        }
    }
}

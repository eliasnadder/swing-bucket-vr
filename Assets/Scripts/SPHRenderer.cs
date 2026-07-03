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

    private class ParticleVisual
    {
        public GameObject gameObject;
        public Renderer renderer;
        public MaterialPropertyBlock block;
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
        runtimeStreamMaterial = streamMaterial != null ? streamMaterial : CreateDefaultMaterial();
        EnsurePool(initialPoolSize);
        EnsureStreamRenderer();
    }

    private void LateUpdate()
    {
        RenderParticles();
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
}

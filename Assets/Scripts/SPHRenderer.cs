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
    public int maxStreamPoints = 48;
    public Material streamMaterial;

    private readonly List<ParticleVisual> visuals = new List<ParticleVisual>();
    private readonly List<Vector3> streamPoints = new List<Vector3>(64);
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

    private float GetHoleRadius()
    {
        if (paintEmitter != null)
            return Mathf.Max(0.001f, paintEmitter.holeRadius);

        if (solver != null)
            return Mathf.Max(0.001f, solver.orificeDiameter * 0.5f);

        return Mathf.Max(0.001f, particleSize * 0.5f);
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
        streamRenderer.numCapVertices = 8;
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

using System.Collections.Generic;
using UnityEngine;

public class SPHRenderer : MonoBehaviour
{
    [Header("References")]
    public SPHFluidSolver solver;

    [Header("Visuals")]
    public int initialPoolSize = 512;
    public float particleSize = 0.03f;
    public Material particleMaterial;
    public bool createMaterialIfMissing = true;

    private readonly List<ParticleVisual> visuals = new List<ParticleVisual>();
    private Mesh particleMesh;
    private Material runtimeMaterial;

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

        if (particleMesh == null)
            particleMesh = CreateSphereMesh(8, 12);

        if (particleMaterial == null && createMaterialIfMissing)
            particleMaterial = CreateDefaultMaterial();

        runtimeMaterial = particleMaterial;
        EnsurePool(initialPoolSize);
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
        for (int i = 0; i < activeCount; i++)
        {
            SPHParticle particle = solver.GetParticle(i);
            ParticleVisual visual = visuals[i];

            if (!visual.gameObject.activeSelf)
                visual.gameObject.SetActive(true);

            visual.gameObject.transform.position = particle.position;
            visual.gameObject.transform.localScale = Vector3.one * particleSize;

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

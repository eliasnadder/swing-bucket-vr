using System.Collections.Generic;
using UnityEngine;

public class SPHFluidSolver : MonoBehaviour
{
    [Header("SPH Parameters")]
    public float smoothingRadius = 0.08f;
    public float particleMass = 0.02f;
    public float restDensity = 1000f;
    public float gasConstant = 2000f;
    public float viscosity = 0.25f;
    public Vector3 gravity = new Vector3(0f, -9.81f, 0f);
    public float damping = 0.02f;
    public float timeStep = 0.0166667f;
    public bool useFull3D = false;

    [Header("2.5D Depth Control")]
    public float depthPlane = 0f;
    public float depthDamping = 0.65f;

    [Header("Debug")]
    public bool drawGizmos = false;

    private readonly List<SPHParticle> particles = new List<SPHParticle>(1024);
    private readonly List<int> neighborIndices = new List<int>(128);
    private SpatialHashGrid spatialHash;

    public IReadOnlyList<SPHParticle> Particles => particles;
    public int ParticleCount => particles.Count;

    private void Awake()
    {
        spatialHash = new SpatialHashGrid(smoothingRadius, useFull3D);
    }

    private void OnValidate()
    {
        smoothingRadius = Mathf.Max(0.001f, smoothingRadius);
        particleMass = Mathf.Max(0.000001f, particleMass);
        restDensity = Mathf.Max(0.0001f, restDensity);
        gasConstant = Mathf.Max(0f, gasConstant);
        viscosity = Mathf.Max(0f, viscosity);
        damping = Mathf.Max(0f, damping);
        depthDamping = Mathf.Clamp01(depthDamping);
        if (spatialHash != null)
            spatialHash = new SpatialHashGrid(smoothingRadius, useFull3D);
    }

    public void ClearParticles()
    {
        particles.Clear();
    }

    public void AddParticle(Vector3 position, Vector3 velocity, Color color)
    {
        SPHParticle particle = new SPHParticle
        {
            position = position,
            velocity = velocity,
            force = Vector3.zero,
            density = restDensity,
            pressure = 0f,
            color = color,
            alive = true
        };

        if (!useFull3D)
            particle.position.z = depthPlane;

        particles.Add(particle);
    }

    public SPHParticle GetParticle(int index) => particles[index];

    public void SetParticle(int index, SPHParticle particle)
    {
        particles[index] = particle;
    }

    public void RemoveParticleAt(int index)
    {
        particles.RemoveAt(index);
    }

    public void StepSimulation(float dt)
    {
        if (dt <= 0f || particles.Count == 0)
            return;

        if (spatialHash == null)
            spatialHash = new SpatialHashGrid(smoothingRadius, useFull3D);

        spatialHash.Rebuild(particles);
        ComputeDensities();
        ComputeForces();
        Integrate(dt);
    }

    public void ComputeDensities()
    {
        for (int i = 0; i < particles.Count; i++)
        {
            SPHParticle particle = particles[i];
            float density = 0f;

            spatialHash.GetNeighbors(particle.position, neighborIndices);
            for (int n = 0; n < neighborIndices.Count; n++)
            {
                SPHParticle neighbor = particles[neighborIndices[n]];
                float distance = Vector3.Distance(particle.position, neighbor.position);
                density += particleMass * SPHKernel.Poly6(distance, smoothingRadius);
            }

            particle.density = Mathf.Max(restDensity * 0.1f, density);
            particle.pressure = gasConstant * (particle.density - restDensity);
            particles[i] = particle;
        }
    }

    public void ComputeForces()
    {
        for (int i = 0; i < particles.Count; i++)
        {
            SPHParticle particle = particles[i];
            Vector3 force = Vector3.zero;

            spatialHash.GetNeighbors(particle.position, neighborIndices);
            for (int n = 0; n < neighborIndices.Count; n++)
            {
                int j = neighborIndices[n];
                if (j == i)
                    continue;

                SPHParticle neighbor = particles[j];
                Vector3 r = particle.position - neighbor.position;
                float distance = r.magnitude;
                if (distance <= 0f || distance >= smoothingRadius)
                    continue;

                float neighborDensity = Mathf.Max(0.0001f, neighbor.density);
                Vector3 pressureGradient = SPHKernel.SpikyGradient(r, smoothingRadius);

                float pressureTerm = (particle.pressure + neighbor.pressure) / (2f * neighborDensity);
                force += -particleMass * pressureTerm * pressureGradient;

                float viscosityTerm = SPHKernel.ViscosityLaplacian(distance, smoothingRadius);
                force += viscosity * particleMass * (neighbor.velocity - particle.velocity) / neighborDensity * viscosityTerm;
            }

            force += particle.density * gravity;
            force += -damping * particle.velocity * particle.density;

            particle.force = force;
            particles[i] = particle;
        }
    }

    public void Integrate()
    {
        Integrate(timeStep);
    }

    public void Integrate(float dt)
    {
        for (int i = 0; i < particles.Count; i++)
        {
            SPHParticle particle = particles[i];
            float density = Mathf.Max(restDensity * 0.1f, particle.density);
            Vector3 acceleration = particle.force / density;

            particle.velocity += acceleration * dt;
            particle.position += particle.velocity * dt;

            if (!useFull3D)
            {
                particle.position.z = Mathf.Lerp(particle.position.z, depthPlane, 0.35f);
                particle.velocity.z *= depthDamping;
            }

            particles[i] = particle;
        }
    }
}

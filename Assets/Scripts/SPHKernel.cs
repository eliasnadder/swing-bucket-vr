using UnityEngine;

public static class SPHKernel
{
    public static float Poly6(float distance, float h)
    {
        if (distance < 0f || distance >= h)
            return 0f;

        float h2 = h * h;
        float term = h2 - distance * distance;
        float coeff = 315f / (64f * Mathf.PI * Mathf.Pow(h, 9f));
        return coeff * term * term * term;
    }

    public static Vector3 SpikyGradient(Vector3 r, float h)
    {
        float distance = r.magnitude;
        if (distance <= 0f || distance >= h)
            return Vector3.zero;

        float coeff = -45f / (Mathf.PI * Mathf.Pow(h, 6f));
        float term = (h - distance) * (h - distance);
        return coeff * term * (r / distance);
    }

    public static float ViscosityLaplacian(float distance, float h)
    {
        if (distance < 0f || distance >= h)
            return 0f;

        float coeff = 45f / (Mathf.PI * Mathf.Pow(h, 6f));
        return coeff * (h - distance);
    }
}

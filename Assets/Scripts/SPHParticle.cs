using UnityEngine;

[System.Serializable]
public struct SPHParticle
{
    public Vector3 position;
    public Vector3 velocity;
    public Vector3 force;
    public float density;
    public float pressure;
    public Color color;
    public bool alive;
}

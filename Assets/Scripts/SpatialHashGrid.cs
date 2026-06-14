using System.Collections.Generic;
using UnityEngine;

public class SpatialHashGrid
{
    private readonly Dictionary<Vector3Int, List<int>> grid = new Dictionary<Vector3Int, List<int>>();
    private readonly float cellSize;
    private readonly bool useFull3D;

    public SpatialHashGrid(float cellSize, bool useFull3D)
    {
        this.cellSize = Mathf.Max(0.0001f, cellSize);
        this.useFull3D = useFull3D;
    }

    public void Clear()
    {
        grid.Clear();
    }

    public void Rebuild(IReadOnlyList<SPHParticle> particles)
    {
        Clear();

        for (int i = 0; i < particles.Count; i++)
        {
            Vector3Int key = GetCell(particles[i].position);
            if (!grid.TryGetValue(key, out List<int> list))
            {
                list = new List<int>(8);
                grid.Add(key, list);
            }
            list.Add(i);
        }
    }

    public void GetNeighbors(Vector3 position, List<int> results)
    {
        results.Clear();

        Vector3Int center = GetCell(position);
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                if (useFull3D)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        Vector3Int key = new Vector3Int(center.x + x, center.y + y, center.z + z);
                        if (grid.TryGetValue(key, out List<int> list))
                            results.AddRange(list);
                    }
                }
                else
                {
                    Vector3Int key = new Vector3Int(center.x + x, center.y + y, center.z);
                    if (grid.TryGetValue(key, out List<int> list))
                        results.AddRange(list);
                }
            }
        }
    }

    private Vector3Int GetCell(Vector3 position)
    {
        int x = Mathf.FloorToInt(position.x / cellSize);
        int y = Mathf.FloorToInt(position.y / cellSize);
        int z = useFull3D ? Mathf.FloorToInt(position.z / cellSize) : 0;
        return new Vector3Int(x, y, z);
    }
}

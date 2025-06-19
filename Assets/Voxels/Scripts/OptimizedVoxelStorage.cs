using UnityEngine;
using System.Collections.Generic;
using Unity.Collections;

public class OptimizedVoxelStorage
{
    private Dictionary<int, Voxel> voxelMap;
    private int chunkSize;
    private int chunkHeight;

    public int Length => chunkSize;
    public int Height => chunkHeight;

    public OptimizedVoxelStorage(int size, int height)
    {
        chunkSize = size;
        chunkHeight = height;
        voxelMap = new Dictionary<int, Voxel>();
    }

    private int GetIndex(int x, int y, int z)
    {
        return x + (y * chunkSize) + (z * chunkSize * chunkHeight);
    }

    public Voxel GetVoxel(int x, int y, int z)
    {
        int index = GetIndex(x, y, z);
        if (voxelMap.TryGetValue(index, out Voxel voxel))
        {
            return voxel;
        }
        return new Voxel(Voxel.VoxelType.Air);
    }

    public void SetVoxel(int x, int y, int z, Voxel voxel)
    {
        int index = GetIndex(x, y, z);
        if (voxel.type == Voxel.VoxelType.Air)
        {
            voxelMap.Remove(index);
        }
        else
        {
            voxelMap[index] = voxel;
        }
    }

    public bool IsVoxelAir(int x, int y, int z)
    {
        return !voxelMap.ContainsKey(GetIndex(x, y, z));
    }

    public void Clear()
    {
        voxelMap.Clear();
    }

    public int GetVoxelCount()
    {
        return voxelMap.Count;
    }

    public Dictionary<int, Voxel>.Enumerator GetEnumerator()
    {
        return voxelMap.GetEnumerator();
    }

    public bool IsEmpty()
    {
        return voxelMap.Count == 0;
    }

    // Add indexer for compatibility with array-like access
    public Voxel this[int x, int y, int z]
    {
        get => GetVoxel(x, y, z);
        set => SetVoxel(x, y, z, value);
    }

    // Deep clone for deterministic pipeline snapshots
    public OptimizedVoxelStorage Clone()
    {
        var copy = new OptimizedVoxelStorage(chunkSize, chunkHeight);
        foreach (var kvp in voxelMap)
        {
            copy.voxelMap[kvp.Key] = kvp.Value;
        }
        return copy;
    }
} 
using UnityEngine;
using System.Collections.Generic;
using Unity.Mathematics;

public struct Voxel
{
    public enum VoxelType { Air, Stone, Dirt, Grass, Deepslate, Sand } // Add more types as needed
    public Vector3 position;
    public VoxelType type;
    public bool isActive;
    public float lightLevel; // 0.0 to 1.0, where 1.0 is full brightness

    public static Voxel CreateDefault()
    {
        return new Voxel
        {
            type = VoxelType.Air,
            position = Vector3.zero,
            isActive = false,
            lightLevel = 1.0f
        };
    }

    public static Voxel Create(VoxelType type, Vector3 position)
    {
        return new Voxel
        {
            type = type,
            position = position,
            isActive = type != VoxelType.Air,
            lightLevel = 1.0f
        };
    }
}

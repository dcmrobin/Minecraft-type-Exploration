using UnityEngine;
using System.Collections.Generic;
using Unity.Mathematics;

public struct Voxel
{
    public enum VoxelType { Air, Stone, Dirt, Grass, Deepslate, Sand } // Add more types as needed
    public Vector3 position;
    public VoxelType type;
    public bool isActive;
    public float skyLight; // 0-1 value for sky light
    public float blockLight; // 0-1 value for block light
    public float transparency; // 0-1 value, 0 is fully opaque, 1 is fully transparent

    public Voxel(Vector3 position, VoxelType type, bool isActive)
    {
        this.position = position;
        this.type = type;
        this.isActive = isActive;
        this.skyLight = 0f;
        this.blockLight = 0f;
        this.transparency = type == VoxelType.Air ? 1f : 0f;
    }
}

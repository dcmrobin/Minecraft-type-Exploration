using UnityEngine;
using System.Collections.Generic;
using Unity.Mathematics;

public struct Voxel
{
    public enum VoxelType { Air, Stone, Dirt, Grass, Deepslate, Sand } // Add more types as needed
    public Vector3 position;
    public VoxelType type;
    public bool isActive;

    public Voxel(Vector3 position, VoxelType type, bool isActive)
    {
        this.position = position;
        this.type = type;
        this.isActive = isActive;
    }
}

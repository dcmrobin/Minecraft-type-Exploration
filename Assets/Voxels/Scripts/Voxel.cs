using UnityEngine;
using System.Collections.Generic;
using Unity.Mathematics;

public struct Voxel
{
    public enum VoxelType
    {
        Air,
        Dirt,
        Grass,
        Stone,
        Sand,
        Water,
        Deepslate
    }

    public VoxelType type;
    public float transparency;
    public byte lightLevel; // 0-15 light level like Minecraft
    public bool isActive;

    public Voxel(VoxelType type, bool isActive = true, float transparency = 1f)
    {
        this.type = type;
        this.isActive = isActive;
        this.transparency = transparency;
        this.lightLevel = 0; // Start with no light
    }
}

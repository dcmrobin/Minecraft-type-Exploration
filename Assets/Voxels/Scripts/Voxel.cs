using UnityEngine;

public class Voxel
{
    public enum VoxelType { Air, Stone, Dirt, Grass } // Add more types as needed
    public Vector3 position;
    public VoxelType type;
    public bool isActive;
    public float globalLightPercentage;
    public float transparency;

    public Voxel() : this(Vector3.zero, VoxelType.Air, false) { }

    public Voxel(Vector3 position, VoxelType type, bool isActive)
    {
        this.position = position;
        this.type = type;
        this.isActive = isActive;
        this.globalLightPercentage = 0f;
        this.transparency = type == VoxelType.Air ? 1 : 0;
    }

    public static VoxelType DetermineVoxelType(Vector3 voxelWorldPos, float calculatedHeight, float caveNoiseValue)
    {
        VoxelType type = voxelWorldPos.y <= calculatedHeight ? VoxelType.Stone : VoxelType.Air;

        if (type != VoxelType.Air && voxelWorldPos.y < calculatedHeight && voxelWorldPos.y >= calculatedHeight - 3)
            type = VoxelType.Dirt;

        if (type == VoxelType.Dirt && voxelWorldPos.y <= calculatedHeight && voxelWorldPos.y > calculatedHeight - 1)
            type = VoxelType.Grass;

        if (caveNoiseValue > 0.45f && voxelWorldPos.y <= 100 + (caveNoiseValue * 20) || caveNoiseValue > 0.8f && voxelWorldPos.y > 100 + (caveNoiseValue * 20))
            type = VoxelType.Air;

        return type;
    }

    public static float CalculateHeight(int x, int z, int y, float[,] mountainCurveValues, float[,,] simplexMap, float[,] lod1Map, float maxHeight)
    {
        float normalizedNoiseValue = (mountainCurveValues[x, z] - simplexMap[x, y, z] + lod1Map[x, z]) * 400;
        float calculatedHeight = normalizedNoiseValue * maxHeight * mountainCurveValues[x, z];
        return calculatedHeight + 150;
    }

    public static Vector2 GetTileOffset(VoxelType type, int faceIndex)
    {
        switch (type)
        {
            case VoxelType.Grass:
                if (faceIndex == 0) // Top face
                    return new Vector2(0, 0.75f);
                if (faceIndex == 1) // Bottom face
                    return new Vector2(0.25f, 0.75f);
                return new Vector2(0, 0.5f); // Side faces

            case VoxelType.Dirt:
                return new Vector2(0.25f, 0.75f);

            case VoxelType.Stone:
                return new Vector2(0.25f, 0.5f);

            // Add more cases for other types...

            default:
                return Vector2.zero;
        }
    }

    public static Vector3Int GetNeighbor(Vector3Int v, int direction)
    {
        return direction switch
        {
            0 => new Vector3Int(v.x, v.y + 1, v.z),
            1 => new Vector3Int(v.x, v.y - 1, v.z),
            2 => new Vector3Int(v.x - 1, v.y, v.z),
            3 => new Vector3Int(v.x + 1, v.y, v.z),
            4 => new Vector3Int(v.x, v.y, v.z + 1),
            5 => new Vector3Int(v.x, v.y, v.z - 1),
            _ => v
        };
    }
}

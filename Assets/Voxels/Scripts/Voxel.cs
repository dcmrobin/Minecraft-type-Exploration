using UnityEngine;
using System.Collections.Generic;

public struct Voxel
{
    public enum VoxelType { Air, Stone, Dirt, Grass } // Add more types as needed
    public Vector3 position;
    public VoxelType type;
    public bool isActive;
    public float globalLightPercentage;
    public float transparency;

    public Voxel(Vector3 position, VoxelType type, bool isActive, float globalLightPercentage)
    {
        this.position = position;
        this.type = type;
        this.isActive = isActive;
        this.globalLightPercentage = globalLightPercentage;
        this.transparency = type == VoxelType.Air ? 1 : 0;
    }

    public static VoxelType DetermineVoxelType(Vector3 voxelChunkPos, float calculatedHeight, Vector3 chunkPos, bool useVerticalChunks)
    {
        Vector3 voxelWorldPos = useVerticalChunks ? voxelChunkPos + chunkPos : voxelChunkPos;

        // Calculate the 3D Perlin noise for caves
        float caveNoiseFrequency = 0.07f;  // Adjust frequency to control cave density
        float caveThreshold = -0.3f;       // Threshold to determine if it's a cave
        float caveNoise = Mathf.PerlinNoise(voxelWorldPos.x * caveNoiseFrequency, voxelWorldPos.z * caveNoiseFrequency) * 2f - 1f 
                        + Mathf.PerlinNoise(voxelWorldPos.y * caveNoiseFrequency, voxelWorldPos.x * caveNoiseFrequency) * 2f - 1f // *2-1 to make it between -1 and 1
                        + Mathf.PerlinNoise(voxelWorldPos.z * caveNoiseFrequency, voxelWorldPos.y * caveNoiseFrequency) * 2f - 1f;// instead of between 0 and 1

        float remappedCaveNoise = caveNoise;

        // Normalize the noise value
        remappedCaveNoise /= 3f;

        // If the noise value is below the threshold, make it a cave (Air)
        if (remappedCaveNoise < caveThreshold)
        {
            return VoxelType.Air;
        }

        // Normal terrain height-based voxel type determination
        VoxelType type = voxelWorldPos.y <= calculatedHeight ? VoxelType.Stone : VoxelType.Air;

        if (type != VoxelType.Air && voxelWorldPos.y < calculatedHeight && voxelWorldPos.y >= calculatedHeight - 3)
            type = VoxelType.Dirt;

        if (type == VoxelType.Dirt && voxelWorldPos.y <= calculatedHeight && voxelWorldPos.y > calculatedHeight - 1)
            type = VoxelType.Grass;

        return type;
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

    public static Vector2[] GetFaceUVs(VoxelType type, int faceIndex)
    {
        float tileSize = 0.25f; // Assuming a 4x4 texture atlas (1/4 = 0.25)
        Vector2[] uvs = new Vector2[4];

        Vector2 tileOffset = GetTileOffset(type, faceIndex);

        uvs[0] = new Vector2(tileOffset.x, tileOffset.y);
        uvs[1] = new Vector2(tileOffset.x + tileSize, tileOffset.y);
        uvs[2] = new Vector2(tileOffset.x + tileSize, tileOffset.y + tileSize);
        uvs[3] = new Vector2(tileOffset.x, tileOffset.y + tileSize);

        return uvs;
    }

    public void AddFaceData(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs, List<Color> colors, int faceIndex, Voxel neighborVoxel)
    {
        Vector2[] faceUVs = Voxel.GetFaceUVs(this.type, faceIndex);
        float lightLevel = neighborVoxel.globalLightPercentage;

        switch (faceIndex)
        {
            case 0: // Top Face
                vertices.Add(new Vector3(position.x, position.y + 1, position.z));
                vertices.Add(new Vector3(position.x, position.y + 1, position.z + 1));
                vertices.Add(new Vector3(position.x + 1, position.y + 1, position.z + 1));
                vertices.Add(new Vector3(position.x + 1, position.y + 1, position.z));
                break;
            case 1: // Bottom Face
                vertices.Add(new Vector3(position.x, position.y, position.z));
                vertices.Add(new Vector3(position.x + 1, position.y, position.z));
                vertices.Add(new Vector3(position.x + 1, position.y, position.z + 1));
                vertices.Add(new Vector3(position.x, position.y, position.z + 1));
                break;
            case 2: // Left Face
                vertices.Add(new Vector3(position.x, position.y, position.z));
                vertices.Add(new Vector3(position.x, position.y, position.z + 1));
                vertices.Add(new Vector3(position.x, position.y + 1, position.z + 1));
                vertices.Add(new Vector3(position.x, position.y + 1, position.z));
                break;
            case 3: // Right Face
                vertices.Add(new Vector3(position.x + 1, position.y, position.z + 1));
                vertices.Add(new Vector3(position.x + 1, position.y, position.z));
                vertices.Add(new Vector3(position.x + 1, position.y + 1, position.z));
                vertices.Add(new Vector3(position.x + 1, position.y + 1, position.z + 1));
                break;
            case 4: // Front Face
                vertices.Add(new Vector3(position.x, position.y, position.z + 1));
                vertices.Add(new Vector3(position.x + 1, position.y, position.z + 1));
                vertices.Add(new Vector3(position.x + 1, position.y + 1, position.z + 1));
                vertices.Add(new Vector3(position.x, position.y + 1, position.z + 1));
                break;
            case 5: // Back Face
                vertices.Add(new Vector3(position.x + 1, position.y, position.z));
                vertices.Add(new Vector3(position.x, position.y, position.z));
                vertices.Add(new Vector3(position.x, position.y + 1, position.z));
                vertices.Add(new Vector3(position.x + 1, position.y + 1, position.z));
                break;
        }

        for (int i = 0; i < 4; i++)
        {
            colors.Add(new Color(0, 0, 0, lightLevel));
        }
        uvs.AddRange(faceUVs);

        // Adding triangle indices
        int vertCount = vertices.Count;
        triangles.Add(vertCount - 4);
        triangles.Add(vertCount - 3);
        triangles.Add(vertCount - 2);
        triangles.Add(vertCount - 4);
        triangles.Add(vertCount - 2);
        triangles.Add(vertCount - 1);
    }
}

using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;

[BurstCompile]
public struct GenerateJob : IJob
{
    public bool useVerticalChunks;
    public int chunkHeight;
    public int chunkSize;
    public float frequency;
    public float amplitude;
    //public float lightFalloff;
    public Vector3 chunkWorldPosition;
    public NativeArray<Voxel> voxels;
    public NativeArray<float> heightCurveSamples;
    public int randInt;
    public int worldSeed;
    //public NativeQueue<Vector3Int> litVoxels;

    public void Execute()
    {
        // Voxel position calculation
        for (int index = 0; index < voxels.Length; index++)
        {
            int x = index % chunkSize;
            int y = (index % (chunkSize * chunkHeight)) / chunkSize;
            int z = index / (chunkSize * chunkHeight);

            Vector3 voxelChunkPos = new Vector3(x, y, z);
            float perlinHeight = Mathf.PerlinNoise(
                ((chunkWorldPosition.x + x) + worldSeed) / frequency,
                ((chunkWorldPosition.z + z) + worldSeed) / frequency
            );

            int sampleIndex = Mathf.Clamp(Mathf.RoundToInt(perlinHeight * (heightCurveSamples.Length - 1)), 0, heightCurveSamples.Length - 1);
            float calculatedHeight = heightCurveSamples[sampleIndex] * amplitude;

            Voxel.VoxelType type = Voxel.DetermineVoxelType(voxelChunkPos, calculatedHeight, chunkWorldPosition, useVerticalChunks, randInt, worldSeed);
            voxels[index] = new Voxel(new Vector3(x, y, z), type, type != Voxel.VoxelType.Air);
        }
    }

    private readonly bool IsInsideChunk(Vector3Int position)
    {
        return position.x >= 0 && position.x < chunkSize &&
               position.y >= 0 && position.y < chunkHeight &&
               position.z >= 0 && position.z < chunkSize;
    }

    private readonly Vector3Int GetNeighbor(Vector3Int current, int direction)
    {
        return direction switch
        {
            0 => new Vector3Int(current.x, current.y + 1, current.z), // Top
            1 => new Vector3Int(current.x, current.y - 1, current.z), // Bottom
            2 => new Vector3Int(current.x - 1, current.y, current.z), // Left
            3 => new Vector3Int(current.x + 1, current.y, current.z), // Right
            4 => new Vector3Int(current.x, current.y, current.z + 1), // Front
            5 => new Vector3Int(current.x, current.y, current.z - 1), // Back
            _ => current
        };
    }
}
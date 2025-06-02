using Unity.Mathematics;
using Unity.Jobs;
using Unity.Collections;
using UnityEngine;

public struct GenerateJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float> heightCurveSamples;
    [ReadOnly] public bool useVerticalChunks;
    [ReadOnly] public int chunkHeight;
    [ReadOnly] public int chunkSize;
    [ReadOnly] public float frequency;
    [ReadOnly] public float amplitude;
    [ReadOnly] public Vector3 chunkWorldPosition;
    [ReadOnly] public int randInt;
    [ReadOnly] public int worldSeed;

    [WriteOnly] public NativeArray<Voxel> voxels;

    public void Execute(int index)
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

        Voxel.VoxelType type = DetermineVoxelType(voxelChunkPos, calculatedHeight, chunkWorldPosition, useVerticalChunks, randInt, worldSeed);
        voxels[index] = new Voxel(type, type != Voxel.VoxelType.Air);
    }

    public static Voxel.VoxelType DetermineVoxelType(Vector3 voxelChunkPos, float calculatedHeight, Vector3 chunkPos, bool useVerticalChunks, int randInt, int seed)
    {
        Vector3 voxelWorldPos = useVerticalChunks ? voxelChunkPos + chunkPos : voxelChunkPos;

        // Calculate the 3D Perlin noise for caves
        float wormCaveNoiseFrequency = 0.02f;  // Adjust frequency to control cave density
        float wormCaveSizeMultiplier = 1.15f;
        float wormBias = -0.43f;
        float wormCaveNoise = Mathf.Abs(Mathf.PerlinNoise((voxelWorldPos.x + seed) * wormCaveNoiseFrequency / wormCaveSizeMultiplier, (voxelWorldPos.z + seed) * wormCaveNoiseFrequency / wormCaveSizeMultiplier) * 2f - 1f) - wormBias
                        + Mathf.Abs(Mathf.PerlinNoise((voxelWorldPos.y + seed) * wormCaveNoiseFrequency / wormCaveSizeMultiplier, (voxelWorldPos.x + seed) * wormCaveNoiseFrequency / wormCaveSizeMultiplier) * 2f - 1f) - wormBias
                        + Mathf.Abs(Mathf.PerlinNoise((voxelWorldPos.z + seed) * wormCaveNoiseFrequency / wormCaveSizeMultiplier, (voxelWorldPos.y + seed) * wormCaveNoiseFrequency / wormCaveSizeMultiplier) * 2f - 1f) - wormBias;
        float wormCaveNoise2 = Mathf.Abs(Mathf.PerlinNoise((voxelWorldPos.x + (seed*2)) * wormCaveNoiseFrequency / (wormCaveSizeMultiplier / 2), (voxelWorldPos.z + (seed*2)) * wormCaveNoiseFrequency / (wormCaveSizeMultiplier / 2)) * 2f - 1f) - wormBias
                        + Mathf.Abs(Mathf.PerlinNoise((voxelWorldPos.y + (seed*2)) * wormCaveNoiseFrequency / (wormCaveSizeMultiplier / 2), (voxelWorldPos.x + (seed*2)) * wormCaveNoiseFrequency / (wormCaveSizeMultiplier / 2)) * 2f - 1f) - wormBias
                        + Mathf.Abs(Mathf.PerlinNoise((voxelWorldPos.z + (seed*2)) * wormCaveNoiseFrequency / (wormCaveSizeMultiplier / 2), (voxelWorldPos.y + (seed*2)) * wormCaveNoiseFrequency / (wormCaveSizeMultiplier / 2)) * 2f - 1f) - wormBias;
        float remappedWormCaveNoise = wormCaveNoise / 3;
        float remappedWormCaveNoise2 = wormCaveNoise2 / 3;

        float biomeNoise = Mathf.PerlinNoise(voxelWorldPos.x + seed, voxelWorldPos.z + seed);

        if (remappedWormCaveNoise <= 0.5 || remappedWormCaveNoise2 <= 0.5)
            return Voxel.VoxelType.Air;

        // Normal terrain height-based voxel type determination
        Voxel.VoxelType type = voxelWorldPos.y <= calculatedHeight ? Voxel.VoxelType.Stone : Voxel.VoxelType.Air;

        if (biomeNoise < 0.5)
        {
            if (type != Voxel.VoxelType.Air && voxelWorldPos.y < calculatedHeight && voxelWorldPos.y >= calculatedHeight - 3)
                type = Voxel.VoxelType.Dirt;
    
            if (type == Voxel.VoxelType.Dirt && voxelWorldPos.y <= calculatedHeight && voxelWorldPos.y > calculatedHeight - 1)
                type = Voxel.VoxelType.Grass;
        }
        else
        {
            if (type != Voxel.VoxelType.Air && voxelWorldPos.y < calculatedHeight && voxelWorldPos.y >= calculatedHeight - 7)
                type = Voxel.VoxelType.Sand;
        }
        
        if (voxelWorldPos.y <= -230 - randInt && type != Voxel.VoxelType.Air)
            type = Voxel.VoxelType.Deepslate;

        return type;
    }
}
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Collections;
using UnityEngine;
using Unity.Burst;

[BurstCompile]
public struct LightingJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<Voxel> voxels;
    [ReadOnly] public int chunkSize;
    [ReadOnly] public int chunkHeight;
    [ReadOnly] public Vector3 chunkWorldPosition;
    [ReadOnly] public int worldSeed;
    [ReadOnly] public float minTerrainHeight;

    [WriteOnly] public NativeArray<byte> lightLevels;

    public void Execute(int index)
    {
        int x = index % chunkSize;
        int y = (index % (chunkSize * chunkHeight)) / chunkSize;
        int z = index / (chunkSize * chunkHeight);

        Voxel voxel = voxels[index];
        if (voxel.type == Voxel.VoxelType.Air)
        {
            lightLevels[index] = 0;
            return;
        }

        // Calculate world position of the voxel
        float worldY = chunkWorldPosition.y + y;

        // Check if block is exposed to sky
        bool isExposedToSky = worldY >= minTerrainHeight;
        int checkY = y + 1;

        // Only check blocks above if we're at or above the minimum terrain height
        if (isExposedToSky)
        {
            // Check blocks in current chunk
            while (checkY < chunkHeight)
            {
                int checkIndex = x + (checkY * chunkSize) + (z * chunkSize * chunkHeight);
                if (voxels[checkIndex].type != Voxel.VoxelType.Air)
                {
                    isExposedToSky = false;
                    break;
                }
                checkY++;
            }
        }

        // If exposed to sky, set initial light level
        if (isExposedToSky)
        {
            lightLevels[index] = 15;
        }
        else
        {
            lightLevels[index] = 0;
        }
    }
}

[BurstCompile]
public struct LightPropagationJob : IJob
{
    [ReadOnly] public NativeArray<Voxel> voxels;
    [ReadOnly] public int chunkSize;
    [ReadOnly] public int chunkHeight;
    public NativeArray<byte> lightLevels;

    // Fixed-size array for neighbor offsets
    private static readonly int3[] neighborOffsets = new int3[]
    {
        new int3(-1, 0, 0),  // left
        new int3(1, 0, 0),   // right
        new int3(0, -1, 0),  // bottom
        new int3(0, 1, 0),   // top
        new int3(0, 0, -1),  // back
        new int3(0, 0, 1)    // front
    };

    public void Execute()
    {
        // Create a queue for blocks that need light propagation
        NativeList<int> propagationQueue = new NativeList<int>(Allocator.Temp);
        
        // First pass: Add all sky-exposed blocks to the queue
        for (int x = 0; x < chunkSize; x++)
        {
            for (int z = 0; z < chunkSize; z++)
            {
                for (int y = chunkHeight - 1; y >= 0; y--)
                {
                    int index = x + (y * chunkSize) + (z * chunkSize * chunkHeight);
                    if (lightLevels[index] == 15)
                    {
                        propagationQueue.Add(index);
                    }
                }
            }
        }

        // Process the queue
        while (propagationQueue.Length > 0)
        {
            int currentIndex = propagationQueue[0];
            propagationQueue.RemoveAt(0);

            int x = currentIndex % chunkSize;
            int y = (currentIndex % (chunkSize * chunkHeight)) / chunkSize;
            int z = currentIndex / (chunkSize * chunkHeight);

            byte currentLight = lightLevels[currentIndex];
            if (currentLight <= 1) continue;

            byte newLight = (byte)(currentLight - 2);

            // Check all 6 neighbors using the fixed-size array
            for (int i = 0; i < 6; i++)
            {
                int3 offset = neighborOffsets[i];
                int nx = x + offset.x;
                int ny = y + offset.y;
                int nz = z + offset.z;

                if (nx < 0 || nx >= chunkSize || ny < 0 || ny >= chunkHeight || nz < 0 || nz >= chunkSize)
                    continue;

                int neighborIndex = nx + (ny * chunkSize) + (nz * chunkSize * chunkHeight);
                if (voxels[neighborIndex].type != Voxel.VoxelType.Air && lightLevels[neighborIndex] < newLight)
                {
                    lightLevels[neighborIndex] = newLight;
                    propagationQueue.Add(neighborIndex);
                }
            }
        }

        propagationQueue.Dispose();
    }
}

[BurstCompile]
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
    [WriteOnly] public NativeArray<float> minTerrainHeight;
    [WriteOnly] public NativeArray<float> allHeights; // New array to store all calculated heights

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

        // Store the calculated height in the array
        allHeights[index] = calculatedHeight;

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

[BurstCompile]
public struct FindMinHeightJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float> allHeights;
    [WriteOnly] public NativeArray<float> minHeights; // Array to store intermediate minimums

    public void Execute(int index)
    {
        int startIndex = index * 64; // Process 64 elements per thread
        int endIndex = math.min(startIndex + 64, allHeights.Length);
        float localMin = float.MaxValue;

        for (int i = startIndex; i < endIndex; i++)
        {
            localMin = math.min(localMin, allHeights[i]);
        }

        minHeights[index] = localMin;
    }
}

[BurstCompile]
public struct FinalizeMinHeightJob : IJob
{
    [ReadOnly] public NativeArray<float> minHeights;
    [WriteOnly] public NativeArray<float> finalMinHeight;

    public void Execute()
    {
        float minHeight = float.MaxValue;
        for (int i = 0; i < minHeights.Length; i++)
        {
            minHeight = math.min(minHeight, minHeights[i]);
        }
        finalMinHeight[0] = minHeight - 1; // Subtract 1 as per your previous change
    }
}
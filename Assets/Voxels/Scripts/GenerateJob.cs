using Unity.Mathematics;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using UnityEngine;

[BurstCompile]
public struct GenerateJob : IJob
{
    public bool useVerticalChunks;
    public int chunkHeight;
    public int chunkSize;
    public float frequency;
    public float amplitude;
    public Vector3 chunkWorldPosition;
    public NativeArray<Voxel> voxels;

    // Worm parameters
    public int wormCount; // Number of worms per chunk
    public float wormLength; // Length of each worm
    public float wormRadius; // Radius of the worm tunnel

    public uint randomSeed; // Random seed for thread-safe random numbers

    public void Execute()
    {
        // Initialize thread-safe random generator
        var random = new Unity.Mathematics.Random(randomSeed);

        GenerateTerrain();
        GeneratePerlinWorms(ref random);
    }

    private void GenerateTerrain()
    {
        // Terrain generation logic, similar to what you already have
        for (int index = 0; index < voxels.Length; index++)
        {
            int x = index % chunkSize;
            int y = (index % (chunkSize * chunkHeight)) / chunkSize;
            int z = index / (chunkSize * chunkHeight);

            Vector3 voxelChunkPos = new Vector3(x, y, z);
            float calculatedHeight = Mathf.PerlinNoise((chunkWorldPosition.x + x) / frequency, (chunkWorldPosition.z + z) / frequency) * amplitude;
            calculatedHeight += useVerticalChunks ? 150 : 0;

            Voxel.VoxelType type = Voxel.DetermineVoxelType(voxelChunkPos, calculatedHeight, chunkWorldPosition, useVerticalChunks);
            voxels[index] = new Voxel(new Vector3(x, y, z), type, type != Voxel.VoxelType.Air, 0);
        }
    }

    private void GeneratePerlinWorms(ref Unity.Mathematics.Random random)
    {
        // Loop to create multiple worms
        for (int i = 0; i < wormCount; i++)
        {
            // Generate random starting position influenced by Perlin noise
            int startX = random.NextInt(0, chunkSize);
            int startY = random.NextInt(0, chunkHeight);
            int startZ = random.NextInt(0, chunkSize);

            float noiseValue = Mathf.PerlinNoise((chunkWorldPosition.x + startX) / frequency, (chunkWorldPosition.z + startZ) / frequency);
            Vector3 wormPosition = new Vector3(startX, Mathf.Floor(noiseValue * amplitude), startZ);

            // Generate a random direction vector using the thread-safe random
            Vector3 wormDirection = random.NextFloat3Direction() * wormRadius; // Same as insideUnitSphere but thread-safe

            for (int step = 0; step < wormLength; step++)
            {
                // Carve a spherical tunnel at the current worm position
                CarveTunnel(wormPosition, wormRadius);

                // Update worm's position by moving in the direction
                wormPosition += wormDirection;

                // Use Perlin noise to adjust the direction smoothly
                wormDirection.x += Mathf.PerlinNoise(wormPosition.x * frequency, wormPosition.z * frequency) - 0.5f;
                wormDirection.y += Mathf.PerlinNoise(wormPosition.y * frequency, wormPosition.z * frequency) - 0.5f;
                wormDirection.z += Mathf.PerlinNoise(wormPosition.x * frequency, wormPosition.y * frequency) - 0.5f;

                wormDirection = wormDirection.normalized; // Keep the movement smooth
            }
        }
    }

    private void CarveTunnel(Vector3 position, float radius)
    {
        // Carve out voxels within a spherical radius of the worm's current position
        for (int x = (int)(position.x - radius); x <= (int)(position.x + radius); x++)
        {
            for (int y = (int)(position.y - radius); y <= (int)(position.y + radius); y++)
            {
                for (int z = (int)(position.z - radius); z <= (int)(position.z + radius); z++)
                {
                    float dist = Vector3.Distance(new Vector3(x, y, z), position);
                    if (dist <= radius && IsInsideChunk(x, y, z))
                    {
                        int voxelIndex = x + y * chunkSize + z * chunkSize * chunkHeight;
                        voxels[voxelIndex] = new Voxel(new Vector3(x, y, z), Voxel.VoxelType.Air, false, 0);
                    }
                }
            }
        }
    }

    private bool IsInsideChunk(int x, int y, int z)
    {
        return x >= 0 && x < chunkSize && y >= 0 && y < chunkHeight && z >= 0 && z < chunkSize;
    }
}

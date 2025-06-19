using System.Collections.Generic;
using UnityEngine;

public interface IChunkGenerationStage
{
    int BorderSize { get; }
    void Generate(
        Vector3Int chunkPos,
        OptimizedVoxelStorage target,
        Dictionary<Vector3Int, OptimizedVoxelStorage> neighborSnapshots,
        World world
    );
}

public static class ChunkGenerationPipeline
{
    // Use only the terrain shaping stage for now
    private static List<IChunkGenerationStage> stages = new List<IChunkGenerationStage> {
        new TerrainShapingStage()
    };

    public static OptimizedVoxelStorage GenerateChunk(
        Vector3Int chunkPos, int chunkSize, int chunkHeight, World world)
    {
        OptimizedVoxelStorage current = new OptimizedVoxelStorage(chunkSize, chunkHeight);
        foreach (var stage in stages)
        {
            Dictionary<Vector3Int, OptimizedVoxelStorage> neighborSnapshots = new();
            stage.Generate(chunkPos, current, neighborSnapshots, world);
        }
        return current;
    }
}

// --- Terrain Shaping Stage ---
public class TerrainShapingStage : IChunkGenerationStage
{
    public int BorderSize => 0;

    // Parameters for tuning
    private const float seaLevel = 0.0f; // World Y=0 is sea level
    private const float continentalScale = 256f; // Large scale for continents
    private const float continentalContrast = 2.5f;
    private const float regionalScale = 32f; // Medium scale for hills/cliffs
    private const float cliffBlendScale = 32f;
    private const float cliffContrast = 2.5f;
    private const float verticalFalloff = 0.025f; // How quickly density falls off with height
    private const float seaCompressionStrength = 6.0f; // Higher = wider beaches
    private const float stoneThreshold = 0.0f; // Density threshold for stone
    private const float worldHeight = 128f; // Used for vertical normalization

    public void Generate(
        Vector3Int chunkPos,
        OptimizedVoxelStorage target,
        Dictionary<Vector3Int, OptimizedVoxelStorage> neighborSnapshots,
        World world)
    {
        int chunkSize = world.chunkSize;
        int chunkHeight = world.chunkHeight;
        int baseX = chunkPos.x * chunkSize;
        int baseY = chunkPos.y * chunkHeight;
        int baseZ = chunkPos.z * chunkSize;

        for (int x = 0; x < chunkSize; x++)
        for (int y = 0; y < chunkHeight; y++)
        for (int z = 0; z < chunkSize; z++)
        {
            float wx = baseX + x;
            float wy = baseY + y;
            float wz = baseZ + z;

            // --- Continental noise (2D) ---
            float contNoise = Mathf.PerlinNoise(wx / continentalScale, wz / continentalScale);
            contNoise = SoftContrast(contNoise, continentalContrast);
            float continentHeight = Mathf.Lerp(-32f, 64f, contNoise); // Controls ocean/land height

            // --- Sea compression (for beaches) ---
            float compressedY = SeaCompression(wy, seaLevel, seaCompressionStrength);

            // --- Regional noise (3D) ---
            float densityA = PerlinNoise3D(wx / regionalScale, compressedY / regionalScale, wz / regionalScale, 0);
            float densityB = PerlinNoise3D(wx / regionalScale, compressedY / regionalScale, wz / regionalScale, 1000);
            float cliffMask = Mathf.PerlinNoise(wx / cliffBlendScale, wz / cliffBlendScale);
            cliffMask = SoftContrast(cliffMask, cliffContrast);
            float density = Mathf.Lerp(densityA, densityB, cliffMask);

            // --- Vertical falloff ---
            density -= (compressedY - continentHeight) * verticalFalloff;

            // --- Place block ---
            if (density > stoneThreshold)
                target.SetVoxel(x, y, z, new Voxel(Voxel.VoxelType.Stone, true));
            else
                target.SetVoxel(x, y, z, new Voxel(Voxel.VoxelType.Air, false));
        }
    }

    // --- Helpers ---
    // Simple 3D Perlin noise using 2D slices
    private float PerlinNoise3D(float x, float y, float z, float seed)
    {
        float xy = Mathf.PerlinNoise(x + seed, y + seed);
        float yz = Mathf.PerlinNoise(y + seed, z + seed);
        float zx = Mathf.PerlinNoise(z + seed, x + seed);
        return (xy + yz + zx) / 3f * 2f - 1f; // Range [-1, 1]
    }

    // Soft contrast for noise (sigmoid-like)
    private float SoftContrast(float value, float contrast)
    {
        value = Mathf.Clamp01(value);
        float mid = 0.5f;
        return Mathf.Clamp01((value - mid) * contrast + mid);
    }

    // Sea compression for wide beaches
    private float SeaCompression(float y, float seaLevel, float strength)
    {
        float d = y - seaLevel;
        if (Mathf.Abs(d) < 16f)
        {
            float sign = Mathf.Sign(d);
            float amt = Mathf.Pow(Mathf.Abs(d) / 16f, 1f / strength) * 16f;
            return seaLevel + sign * amt;
        }
        return y;
    }
} 
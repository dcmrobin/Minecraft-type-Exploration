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
    // Only the flat terrain stage for now
    private static List<IChunkGenerationStage> stages = new List<IChunkGenerationStage> {
        new FlatTerrainStage()
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

// Flat terrain: stone at y=0, air above
public class FlatTerrainStage : IChunkGenerationStage
{
    public int BorderSize => 0;

    public void Generate(
        Vector3Int chunkPos,
        OptimizedVoxelStorage target,
        Dictionary<Vector3Int, OptimizedVoxelStorage> neighborSnapshots,
        World world)
    {
        int chunkSize = world.chunkSize;
        int chunkHeight = world.chunkHeight;
        int worldY = chunkPos.y * chunkHeight;
        for (int x = 0; x < chunkSize; x++)
        for (int y = 0; y < chunkHeight; y++)
        for (int z = 0; z < chunkSize; z++)
        {
            if (worldY + y == 0)
                target.SetVoxel(x, y, z, new Voxel(Voxel.VoxelType.Stone, true));
            else
                target.SetVoxel(x, y, z, new Voxel(Voxel.VoxelType.Air, false));
        }
    }
} 
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
    private static List<IChunkGenerationStage> stages = new List<IChunkGenerationStage> {
        new TerrainShapingStage(),
        new TopsoilStage(),
        new LightingStage()
    };

    // In-memory snapshot cache for a single pipeline run
    private class SnapshotCache
    {
        // [stageIndex][chunkPos] = snapshot
        public List<Dictionary<Vector3Int, OptimizedVoxelStorage>> stageSnapshots = new();
    }

    public static OptimizedVoxelStorage GenerateChunk(
        Vector3Int chunkPos, int chunkSize, int chunkHeight, World world)
    {
        var cache = new SnapshotCache();
        for (int i = 0; i < stages.Count; i++)
            cache.stageSnapshots.Add(new Dictionary<Vector3Int, OptimizedVoxelStorage>());
        return GenerateStage(chunkPos, stages.Count - 1, chunkSize, chunkHeight, world, cache);
    }

    // Recursively generate a chunk up to the given stage, using the cache
    private static OptimizedVoxelStorage GenerateStage(
        Vector3Int chunkPos, int stageIndex, int chunkSize, int chunkHeight, World world, SnapshotCache cache)
    {
        if (cache.stageSnapshots[stageIndex].TryGetValue(chunkPos, out var snapshot))
            return snapshot;

        OptimizedVoxelStorage input;
        if (stageIndex == 0)
        {
            input = new OptimizedVoxelStorage(chunkSize, chunkHeight);
        }
        else
        {
            input = GenerateStage(chunkPos, stageIndex - 1, chunkSize, chunkHeight, world, cache).Clone();
        }

        // Gather neighbor snapshots for this stage
        var stage = stages[stageIndex];
        var neighborSnapshots = new Dictionary<Vector3Int, OptimizedVoxelStorage>();
        int border = stage.BorderSize;
        if (border > 0)
        {
            // Add the current chunk's original terrain snapshot (for reading unmodified terrain)
            if (stageIndex > 0)
            {
                neighborSnapshots[Vector3Int.zero] = GenerateStage(chunkPos, 0, chunkSize, chunkHeight, world, cache);
            }
            
            // Only generate essential neighbors to avoid exponential growth
            // For topsoiling, we mainly need the chunk above for surface detection
            Vector3Int[] essentialNeighbors = {
                new Vector3Int(0, 1, 0),   // Above (most important for topsoiling)
                new Vector3Int(0, -1, 0),  // Below
                new Vector3Int(1, 0, 0),   // Right
                new Vector3Int(-1, 0, 0),  // Left
                new Vector3Int(0, 0, 1),   // Front
                new Vector3Int(0, 0, -1)   // Back
            };
            
            foreach (var offset in essentialNeighbors)
            {
                Vector3Int neighborPos = chunkPos + offset;
                if (stageIndex == 0)
                {
                    // For the first stage, neighbors are always empty (all air)
                    neighborSnapshots[offset] = new OptimizedVoxelStorage(chunkSize, chunkHeight);
                }
                else
                {
                    neighborSnapshots[offset] = GenerateStage(neighborPos, stageIndex - 1, chunkSize, chunkHeight, world, cache);
                }
            }
        }

        // Run the stage
        stage.Generate(chunkPos, input, neighborSnapshots, world);
        cache.stageSnapshots[stageIndex][chunkPos] = input;
        return input;
    }

    // --- Border Light Fix-Up Pass ---
    // Call this after chunk generation, and after generating any neighbor
    public static void FixupChunkBorderLighting(
        Vector3Int chunkPos,
        OptimizedVoxelStorage chunkData,
        int chunkSize,
        int chunkHeight,
        Dictionary<Vector3Int, OptimizedVoxelStorage> neighborSnapshots)
    {
        // We'll use the same gather logic as in GatherLightFromNeighbors, but track which voxels are updated
        Queue<Vector3Int> lightSources = new Queue<Vector3Int>();
        
        Vector3Int[] neighborOffsets = {
            new Vector3Int(0, 1, 0),   // Above
            new Vector3Int(0, -1, 0),  // Below
            new Vector3Int(-1, 0, 0),  // Left
            new Vector3Int(1, 0, 0),   // Right
            new Vector3Int(0, 0, -1),  // Back
            new Vector3Int(0, 0, 1)    // Front
        };
        
        foreach (Vector3Int offset in neighborOffsets)
        {
            if (neighborSnapshots.TryGetValue(offset, out var neighborChunk))
            {
                // Check the face of the neighbor chunk that borders this chunk
                FixupNeighborFaceForLight(chunkData, lightSources, chunkSize, chunkHeight, neighborChunk, offset);
            }
        }
        // Propagate any newly gathered light
        if (lightSources.Count > 0)
        {
            // Use the same propagation as before, but only within this chunk
            PropagateLightStatic(chunkData, lightSources, chunkSize, chunkHeight);
        }
    }

    // Helper for fix-up: gather light from neighbor face and enqueue updates
    private static void FixupNeighborFaceForLight(
        OptimizedVoxelStorage target,
        Queue<Vector3Int> lightSources,
        int chunkSize,
        int chunkHeight,
        OptimizedVoxelStorage neighborChunk,
        Vector3Int offset)
    {
        int neighborX, neighborY, neighborZ;
        if (offset.y != 0)
        {
            neighborY = offset.y > 0 ? 0 : chunkHeight - 1;
            for (int x = 0; x < chunkSize; x++)
            for (int z = 0; z < chunkSize; z++)
            {
                Voxel neighborVoxel = neighborChunk.GetVoxel(x, neighborY, z);
                if (neighborVoxel.lightLevel > 0)
                {
                    int localY = offset.y > 0 ? chunkHeight - 1 : 0;
                    Voxel localVoxel = target.GetVoxel(x, localY, z);
                    if (localVoxel.type == Voxel.VoxelType.Air && localVoxel.lightLevel < neighborVoxel.lightLevel)
                    {
                        byte newLight = (byte)Mathf.Max(0, neighborVoxel.lightLevel - LightingStage.lightAttenuation);
                        if (newLight > localVoxel.lightLevel)
                        {
                            localVoxel.lightLevel = newLight;
                            target.SetVoxel(x, localY, z, localVoxel);
                            lightSources.Enqueue(new Vector3Int(x, localY, z));
                        }
                    }
                }
            }
        }
        else if (offset.x != 0)
        {
            neighborX = offset.x > 0 ? 0 : chunkSize - 1;
            for (int y = 0; y < chunkHeight; y++)
            for (int z = 0; z < chunkSize; z++)
            {
                Voxel neighborVoxel = neighborChunk.GetVoxel(neighborX, y, z);
                if (neighborVoxel.lightLevel > 0)
                {
                    int localX = offset.x > 0 ? chunkSize - 1 : 0;
                    Voxel localVoxel = target.GetVoxel(localX, y, z);
                    if (localVoxel.type == Voxel.VoxelType.Air && localVoxel.lightLevel < neighborVoxel.lightLevel)
                    {
                        byte newLight = (byte)Mathf.Max(0, neighborVoxel.lightLevel - LightingStage.lightAttenuation);
                        if (newLight > localVoxel.lightLevel)
                        {
                            localVoxel.lightLevel = newLight;
                            target.SetVoxel(localX, y, z, localVoxel);
                            lightSources.Enqueue(new Vector3Int(localX, y, z));
                        }
                    }
                }
            }
        }
        else if (offset.z != 0)
        {
            neighborZ = offset.z > 0 ? 0 : chunkSize - 1;
            for (int x = 0; x < chunkSize; x++)
            for (int y = 0; y < chunkHeight; y++)
            {
                Voxel neighborVoxel = neighborChunk.GetVoxel(x, y, neighborZ);
                if (neighborVoxel.lightLevel > 0)
                {
                    int localZ = offset.z > 0 ? chunkSize - 1 : 0;
                    Voxel localVoxel = target.GetVoxel(x, y, localZ);
                    if (localVoxel.type == Voxel.VoxelType.Air && localVoxel.lightLevel < neighborVoxel.lightLevel)
                    {
                        byte newLight = (byte)Mathf.Max(0, neighborVoxel.lightLevel - LightingStage.lightAttenuation);
                        if (newLight > localVoxel.lightLevel)
                        {
                            localVoxel.lightLevel = newLight;
                            target.SetVoxel(x, y, localZ, localVoxel);
                            lightSources.Enqueue(new Vector3Int(x, y, localZ));
                        }
                    }
                }
            }
        }
    }

    // Static version of PropagateLight for fix-up
    // Only propagates within the chunk
    // (This is a copy of the instance method, but static and public)
    public static void PropagateLightStatic(
        OptimizedVoxelStorage target,
        Queue<Vector3Int> lightSources,
        int chunkSize,
        int chunkHeight)
    {
        Vector3Int[] directions = {
            new Vector3Int(0, 1, 0),
            new Vector3Int(0, -1, 0),
            new Vector3Int(-1, 0, 0),
            new Vector3Int(1, 0, 0),
            new Vector3Int(0, 0, -1),
            new Vector3Int(0, 0, 1)
        };
        while (lightSources.Count > 0)
        {
            Vector3Int current = lightSources.Dequeue();
            byte currentLight = target.GetVoxel(current.x, current.y, current.z).lightLevel;
            if (currentLight == 0) continue;
            byte newLight = (byte)Mathf.Max(0, currentLight - LightingStage.lightAttenuation);
            if (newLight == 0) continue;
            foreach (Vector3Int dir in directions)
            {
                Vector3Int neighbor = current + dir;
                if (neighbor.x >= 0 && neighbor.x < chunkSize &&
                    neighbor.y >= 0 && neighbor.y < chunkHeight &&
                    neighbor.z >= 0 && neighbor.z < chunkSize)
                {
                    var neighborVoxel = target.GetVoxel(neighbor.x, neighbor.y, neighbor.z);
                    if (neighborVoxel.type == Voxel.VoxelType.Air || neighborVoxel.transparency > 0.5f)
                    {
                        if (newLight > neighborVoxel.lightLevel)
                        {
                            neighborVoxel.lightLevel = newLight;
                            target.SetVoxel(neighbor.x, neighbor.y, neighbor.z, neighborVoxel);
                            if (newLight > 0)
                            {
                                lightSources.Enqueue(neighbor);
                            }
                        }
                    }
                }
            }
        }
    }
}

// --- Terrain Shaping Stage with Upsampling ---
public class TerrainShapingStage : IChunkGenerationStage
{
    public int BorderSize => 0; // No neighbors needed for basic terrain

    // Parameters for tuning
    private const float seaLevel = 0.0f;
    private const float continentalScale = 256f;
    private const float continentalContrast = 2.5f;
    private const float regionalScale = 32f;
    private const float cliffBlendScale = 32f;
    private const float cliffContrast = 2.5f;
    private const float verticalFalloff = 0.025f;
    private const float seaCompressionStrength = 6.0f;
    private const float stoneThreshold = 0.0f;
    private const int upsample = 4; // 4x4x4 upsampling

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

        int gridX = (chunkSize + upsample - 1) / upsample + 1;
        int gridY = (chunkHeight + upsample - 1) / upsample + 1;
        int gridZ = (chunkSize + upsample - 1) / upsample + 1;
        float[,,] densityGrid = new float[gridX, gridY, gridZ];

        // Sample density at grid points
        for (int gx = 0; gx < gridX; gx++)
        for (int gy = 0; gy < gridY; gy++)
        for (int gz = 0; gz < gridZ; gz++)
        {
            float wx = baseX + gx * upsample;
            float wy = baseY + gy * upsample;
            float wz = baseZ + gz * upsample;

            // --- Continental noise (2D) ---
            float contNoise = Mathf.PerlinNoise(wx / continentalScale, wz / continentalScale);
            contNoise = SoftContrast(contNoise, continentalContrast);
            float continentHeight = Mathf.Lerp(-32f, 64f, contNoise);

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

            densityGrid[gx, gy, gz] = density;
        }

        // Trilinear interpolation for each voxel
        for (int x = 0; x < chunkSize; x++)
        for (int y = 0; y < chunkHeight; y++)
        for (int z = 0; z < chunkSize; z++)
        {
            float fx = (float)x / upsample;
            float fy = (float)y / upsample;
            float fz = (float)z / upsample;
            int x0 = Mathf.FloorToInt(fx);
            int y0 = Mathf.FloorToInt(fy);
            int z0 = Mathf.FloorToInt(fz);
            int x1 = Mathf.Min(x0 + 1, gridX - 1);
            int y1 = Mathf.Min(y0 + 1, gridY - 1);
            int z1 = Mathf.Min(z0 + 1, gridZ - 1);
            float tx = fx - x0;
            float ty = fy - y0;
            float tz = fz - z0;

            float c000 = densityGrid[x0, y0, z0];
            float c100 = densityGrid[x1, y0, z0];
            float c010 = densityGrid[x0, y1, z0];
            float c110 = densityGrid[x1, y1, z0];
            float c001 = densityGrid[x0, y0, z1];
            float c101 = densityGrid[x1, y0, z1];
            float c011 = densityGrid[x0, y1, z1];
            float c111 = densityGrid[x1, y1, z1];

            float c00 = Mathf.Lerp(c000, c100, tx);
            float c01 = Mathf.Lerp(c001, c101, tx);
            float c10 = Mathf.Lerp(c010, c110, tx);
            float c11 = Mathf.Lerp(c011, c111, tx);
            float c0 = Mathf.Lerp(c00, c10, ty);
            float c1 = Mathf.Lerp(c01, c11, ty);
            float density = Mathf.Lerp(c0, c1, tz);

            if (density > stoneThreshold)
                target.SetVoxel(x, y, z, new Voxel(Voxel.VoxelType.Stone, true));
            else
                target.SetVoxel(x, y, z, new Voxel(Voxel.VoxelType.Air, false));
        }
    }

    // --- Helpers ---
    private float PerlinNoise3D(float x, float y, float z, float seed)
    {
        float xy = Mathf.PerlinNoise(x + seed, y + seed);
        float yz = Mathf.PerlinNoise(y + seed, z + seed);
        float zx = Mathf.PerlinNoise(z + seed, x + seed);
        return (xy + yz + zx) / 3f * 2f - 1f;
    }
    private float SoftContrast(float value, float contrast)
    {
        value = Mathf.Clamp01(value);
        float mid = 0.5f;
        return Mathf.Clamp01((value - mid) * contrast + mid);
    }
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

// --- Topsoil Stage with cross-chunk neighbor reads ---
public class TopsoilStage : IChunkGenerationStage
{
    public int BorderSize => 1; // Need neighbors for proper surface detection
    private const int maxSoilDepth = 8; // Increased from 5 to 8 for more visible soil layers
    private const int maxSurfaceSearch = 12;
    private const float sandHeight = -8f;
    private const float sandNoiseScale = 16f;
    private const int maxSlopeForTopsoiling = 7; // Increased from 4 to 8 to prevent checkerboard pattern
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
        for (int z = 0; z < chunkSize; z++)
        {
            // Find surface height in this column
            int surfaceY = -1;
            for (int y = chunkHeight - 1; y >= 0; y--)
            {
                if (target.GetVoxel(x, y, z).type == Voxel.VoxelType.Stone)
                {
                    // Look for air above (may cross chunk boundary)
                    bool nearSurface = false;
                    for (int dy = 1; dy <= maxSurfaceSearch; dy++)
                    {
                        int ay = y + dy;
                        Voxel above;
                        if (ay < chunkHeight)
                        {
                            above = target.GetVoxel(x, ay, z);
                        }
                        else
                        {
                            // Read from neighbor above
                            Vector3Int aboveOffset = new Vector3Int(0, 1, 0);
                            if (neighborSnapshots.TryGetValue(aboveOffset, out var neighbor))
                                above = neighbor.GetVoxel(x, ay - chunkHeight, z);
                            else
                                above = new Voxel(Voxel.VoxelType.Air, false);
                        }
                        if (above.type == Voxel.VoxelType.Air)
                        {
                            nearSurface = true;
                            break;
                        }
                    }
                    if (!nearSurface) continue;
                    surfaceY = y;
                    break;
                }
            }
            if (surfaceY == -1) continue;

            // Check slope using 4-adjacent columns (may cross chunk boundaries)
            int[] adjSurface = new int[4];
            for (int i = 0; i < 4; i++) adjSurface[i] = surfaceY; // Default to current surface height
            int[,] offsets = new int[4, 2] { { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 } };
            for (int i = 0; i < 4; i++)
            {
                int nx = x + offsets[i, 0];
                int nz = z + offsets[i, 1];
                Vector3Int adjOffset = new Vector3Int(
                    nx < 0 ? -1 : (nx >= chunkSize ? 1 : 0),
                    0,
                    nz < 0 ? -1 : (nz >= chunkSize ? 1 : 0)
                );
                int lx = (nx + chunkSize) % chunkSize;
                int lz = (nz + chunkSize) % chunkSize;
                Voxel getVoxel(int xx, int yy, int zz)
                {
                    if (xx >= 0 && xx < chunkSize && zz >= 0 && zz < chunkSize)
                    {
                        // For the current chunk, read from the original terrain snapshot (stage 0)
                        // This ensures we're reading the original stone terrain, not modified topsoil
                        if (neighborSnapshots.TryGetValue(Vector3Int.zero, out var currentChunkSnapshot))
                            return currentChunkSnapshot.GetVoxel(xx, yy, zz);
                        else
                            return target.GetVoxel(xx, yy, zz);
                    }
                    // Cross-chunk neighbor
                    if (neighborSnapshots.TryGetValue(adjOffset, out var neighbor))
                        return neighbor.GetVoxel(lx, yy, lz);
                    return new Voxel(Voxel.VoxelType.Air, false);
                }
                
                // Find the actual surface height for this adjacent column (same logic as main surface detection)
                for (int y = chunkHeight - 1; y >= 0; y--)
                {
                    if (getVoxel(nx, y, nz).type == Voxel.VoxelType.Stone)
                    {
                        // Look for air above (same logic as main surface detection)
                        bool nearSurface = false;
                        for (int dy = 1; dy <= maxSurfaceSearch; dy++)
                        {
                            int ay = y + dy;
                            Voxel above;
                            if (ay < chunkHeight)
                                above = getVoxel(nx, ay, nz);
                            else
                            {
                                Vector3Int aboveOffset = adjOffset + new Vector3Int(0, 1, 0);
                                if (neighborSnapshots.TryGetValue(aboveOffset, out var neighbor))
                                    above = neighbor.GetVoxel(lx, ay - chunkHeight, lz);
                                else
                                    above = new Voxel(Voxel.VoxelType.Air, false);
                            }
                            if (above.type == Voxel.VoxelType.Air)
                            {
                                nearSurface = true;
                                break;
                            }
                        }
                        if (nearSurface)
                        {
                            adjSurface[i] = y;
                            break;
                        }
                    }
                }
            }
            
            // Use a more robust slope calculation that's less sensitive to noise
            int maxHeightDiff = 0;
            for (int i = 0; i < 4; i++)
            {
                int diff = Mathf.Abs(surfaceY - adjSurface[i]);
                if (diff > maxHeightDiff)
                    maxHeightDiff = diff;
            }
            int slope = maxHeightDiff;
            
            // Simple cliff detection: if any adjacent column is significantly lower, it's a cliff
            // This prevents zebra patterns by being more consistent
            bool isCliff = false;
            int steepestDrop = 0;
            for (int i = 0; i < 4; i++)
            {
                int drop = surfaceY - adjSurface[i];
                if (drop > steepestDrop)
                    steepestDrop = drop;
                if (adjSurface[i] < surfaceY - maxSlopeForTopsoiling / 2)
                {
                    isCliff = true;
                    break;
                }
            }
            
            // Skip topsoiling if it's a cliff
            if (isCliff)
            {
                continue; // This is a cliff, no topsoiling
            }
            
            // Check if this column is near a cliff edge (has a significant drop but not enough to be a cliff)
            bool nearCliff = steepestDrop > maxSlopeForTopsoiling / 3; // 3+ block drop indicates near cliff
            
            // Ensure minimum soil depth of 3 layers, and don't reduce too much by slope
            int soilDepth = Mathf.Max(3, maxSoilDepth - Mathf.Min(slope, 2));
            
            // If near a cliff, reduce soil depth significantly to prevent visible soil layers
            if (nearCliff)
            {
                soilDepth = Mathf.Min(soilDepth, 2); // Only 1-2 layers of soil near cliffs
            }

            // Sand/beach logic
            float wx = baseX + x;
            float wz = baseZ + z;
            float sandNoise = Mathf.PerlinNoise(wx / sandNoiseScale, wz / sandNoiseScale);
            float sandCutoff = sandHeight + Mathf.Lerp(-2f, 2f, sandNoise);
            if (baseY + surfaceY < sandCutoff)
            {
                for (int y = surfaceY; y > surfaceY - soilDepth && y >= 0; y--)
                {
                    target.SetVoxel(x, y, z, new Voxel(Voxel.VoxelType.Sand, true));
                }
                continue;
            }

            // Place dirt (and grass on top)
            for (int y = surfaceY; y > surfaceY - soilDepth && y >= 0; y--)
            {
                if (y == surfaceY)
                    target.SetVoxel(x, y, z, new Voxel(Voxel.VoxelType.Grass, true));
                else
                    target.SetVoxel(x, y, z, new Voxel(Voxel.VoxelType.Dirt, true));
            }
        }
    }
}

// --- Lighting Stage with proper sky exposure and light propagation ---
public class LightingStage : IChunkGenerationStage
{
    public int BorderSize => 1; // Need neighbors for proper light propagation across chunk boundaries
    private const byte maxLight = 15;
    public const byte lightAttenuation = 1; // How much light decreases per block distance
    
    public void Generate(
        Vector3Int chunkPos,
        OptimizedVoxelStorage target,
        Dictionary<Vector3Int, OptimizedVoxelStorage> neighborSnapshots,
        World world)
    {
        int chunkSize = world.chunkSize;
        int chunkHeight = world.chunkHeight;
        
        // Step 1: Initialize all voxels to 0 light
        for (int x = 0; x < chunkSize; x++)
        for (int y = 0; y < chunkHeight; y++)
        for (int z = 0; z < chunkSize; z++)
        {
            var voxel = target.GetVoxel(x, y, z);
            voxel.lightLevel = 0;
            target.SetVoxel(x, y, z, voxel);
        }
        
        // Step 2: Find sky-exposed voxels and set them to full light
        var lightSources = new Queue<Vector3Int>();
        
        // Get the terrain snapshot for proper sky detection
        OptimizedVoxelStorage terrainSnapshot = null;
        if (neighborSnapshots.TryGetValue(Vector3Int.zero, out var currentSnapshot))
        {
            terrainSnapshot = currentSnapshot;
        }
        
        for (int x = 0; x < chunkSize; x++)
        for (int z = 0; z < chunkSize; z++)
        {
            // Find the highest non-air voxel in this column
            int surfaceY = -1;
            for (int y = chunkHeight - 1; y >= 0; y--)
            {
                // Use terrain snapshot if available, otherwise use current target
                Voxel voxel;
                if (terrainSnapshot != null)
                    voxel = terrainSnapshot.GetVoxel(x, y, z);
                else
                    voxel = target.GetVoxel(x, y, z);
                    
                if (voxel.type != Voxel.VoxelType.Air)
                {
                    surfaceY = y;
                    break;
                }
            }
            
            // If we found a surface, check if it's exposed to sky
            if (surfaceY != -1)
            {
                // Check if there's air above this surface voxel (including cross-chunk)
                bool exposedToSky = false;
                
                // Check within this chunk first
                for (int y = surfaceY + 1; y < chunkHeight; y++)
                {
                    Voxel aboveVoxel;
                    if (terrainSnapshot != null)
                        aboveVoxel = terrainSnapshot.GetVoxel(x, y, z);
                    else
                        aboveVoxel = target.GetVoxel(x, y, z);
                        
                    if (aboveVoxel.type == Voxel.VoxelType.Air)
                    {
                        exposedToSky = true;
                        break;
                    }
                }
                
                // If not exposed within this chunk, check the chunk above
                if (!exposedToSky && surfaceY == chunkHeight - 1)
                {
                    Vector3Int aboveOffset = new Vector3Int(0, 1, 0);
                    if (neighborSnapshots.TryGetValue(aboveOffset, out var aboveChunk))
                    {
                        // Check the bottom of the chunk above
                        for (int y = 0; y < Mathf.Min(8, chunkHeight); y++) // Limit search to prevent infinite loops
                        {
                            Voxel aboveVoxel = aboveChunk.GetVoxel(x, y, z);
                            if (aboveVoxel.type == Voxel.VoxelType.Air)
                            {
                                exposedToSky = true;
                                break;
                            }
                        }
                    }
                    else
                    {
                        // If no chunk above exists, assume it's exposed to sky
                        exposedToSky = true;
                    }
                }
                
                // If exposed to sky, set to full light and add to light sources
                if (exposedToSky)
                {
                    var voxel = target.GetVoxel(x, surfaceY, z);
                    voxel.lightLevel = maxLight;
                    target.SetVoxel(x, surfaceY, z, voxel);
                    lightSources.Enqueue(new Vector3Int(x, surfaceY, z));
                }
            }
        }
        
        // Step 3: Propagate light using flood-fill algorithm (only within this chunk)
        PropagateLight(target, lightSources, chunkSize, chunkHeight, neighborSnapshots);
        
        // Step 4: Gather light from neighbor chunks (gather operation)
        GatherLightFromNeighbors(target, lightSources, chunkSize, chunkHeight, neighborSnapshots);
        
        // Step 5: Propagate any newly gathered light
        if (lightSources.Count > 0)
        {
            // Use the same propagation as before, but only within this chunk
            ChunkGenerationPipeline.PropagateLightStatic(target, lightSources, chunkSize, chunkHeight);
        }
    }
    
    private void PropagateLight(
        OptimizedVoxelStorage target, 
        Queue<Vector3Int> lightSources, 
        int chunkSize, 
        int chunkHeight,
        Dictionary<Vector3Int, OptimizedVoxelStorage> neighborSnapshots)
    {
        // 6-directional neighbors: up, down, left, right, front, back
        Vector3Int[] directions = {
            new Vector3Int(0, 1, 0),   // Up
            new Vector3Int(0, -1, 0),  // Down
            new Vector3Int(-1, 0, 0),  // Left
            new Vector3Int(1, 0, 0),   // Right
            new Vector3Int(0, 0, -1),  // Back
            new Vector3Int(0, 0, 1)    // Front
        };
        
        while (lightSources.Count > 0)
        {
            Vector3Int current = lightSources.Dequeue();
            byte currentLight = target.GetVoxel(current.x, current.y, current.z).lightLevel;
            
            // Skip if this voxel has no light to propagate
            if (currentLight == 0) continue;
            
            // Calculate new light level for neighbors
            byte newLight = (byte)Mathf.Max(0, currentLight - lightAttenuation);
            
            // Skip propagation if light level is too low to be meaningful
            if (newLight == 0) continue;
            
            // Try to propagate to all 6 neighbors
            foreach (Vector3Int dir in directions)
            {
                Vector3Int neighbor = current + dir;
                
                // Check if neighbor is within chunk bounds
                if (neighbor.x >= 0 && neighbor.x < chunkSize &&
                    neighbor.y >= 0 && neighbor.y < chunkHeight &&
                    neighbor.z >= 0 && neighbor.z < chunkSize)
                {
                    // Get neighbor voxel from our own chunk
                    var neighborVoxel = target.GetVoxel(neighbor.x, neighbor.y, neighbor.z);
                    
                    // Only propagate to air voxels or transparent voxels
                    if (neighborVoxel.type == Voxel.VoxelType.Air || neighborVoxel.transparency > 0.5f)
                    {
                        // Only update if the new light level is higher
                        if (newLight > neighborVoxel.lightLevel)
                        {
                            neighborVoxel.lightLevel = newLight;
                            target.SetVoxel(neighbor.x, neighbor.y, neighbor.z, neighborVoxel);
                            
                            // Add to queue for further propagation (only if it has meaningful light)
                            if (newLight > 0)
                            {
                                lightSources.Enqueue(neighbor);
                            }
                        }
                    }
                }
                // Note: We do NOT propagate light across chunk boundaries
                // Each chunk will handle its own lighting independently
                // This ensures deterministic behavior and prevents race conditions
            }
        }
    }
    
    private void GatherLightFromNeighbors(
        OptimizedVoxelStorage target,
        Queue<Vector3Int> lightSources,
        int chunkSize,
        int chunkHeight,
        Dictionary<Vector3Int, OptimizedVoxelStorage> neighborSnapshots)
    {
        // Check each face of the chunk for light sources from neighbors
        Vector3Int[] neighborOffsets = {
            new Vector3Int(0, 1, 0),   // Above
            new Vector3Int(0, -1, 0),  // Below
            new Vector3Int(-1, 0, 0),  // Left
            new Vector3Int(1, 0, 0),   // Right
            new Vector3Int(0, 0, -1),  // Back
            new Vector3Int(0, 0, 1)    // Front
        };
        
        foreach (Vector3Int offset in neighborOffsets)
        {
            if (neighborSnapshots.TryGetValue(offset, out var neighborChunk))
            {
                // Check the face of the neighbor chunk that borders this chunk
                CheckNeighborFaceForLight(target, lightSources, chunkSize, chunkHeight, 
                    neighborChunk, offset);
            }
        }
    }
    
    private void CheckNeighborFaceForLight(
        OptimizedVoxelStorage target,
        Queue<Vector3Int> lightSources,
        int chunkSize,
        int chunkHeight,
        OptimizedVoxelStorage neighborChunk,
        Vector3Int offset)
    {
        // Determine which face of the neighbor chunk to check
        int neighborX, neighborY, neighborZ;
        
        if (offset.y != 0) // Above or below
        {
            // Check the bottom face of chunk above, or top face of chunk below
            neighborY = offset.y > 0 ? 0 : chunkHeight - 1;
            
            for (int x = 0; x < chunkSize; x++)
            for (int z = 0; z < chunkSize; z++)
            {
                Voxel neighborVoxel = neighborChunk.GetVoxel(x, neighborY, z);
                if (neighborVoxel.lightLevel > 0)
                {
                    // Check if this position in our chunk is air and should receive light
                    int localY = offset.y > 0 ? chunkHeight - 1 : 0;
                    Voxel localVoxel = target.GetVoxel(x, localY, z);
                    
                    if (localVoxel.type == Voxel.VoxelType.Air && localVoxel.lightLevel < neighborVoxel.lightLevel)
                    {
                        // Calculate attenuated light level
                        byte newLight = (byte)Mathf.Max(0, neighborVoxel.lightLevel - lightAttenuation);
                        if (newLight > localVoxel.lightLevel)
                        {
                            localVoxel.lightLevel = newLight;
                            target.SetVoxel(x, localY, z, localVoxel);
                            lightSources.Enqueue(new Vector3Int(x, localY, z));
                        }
                    }
                }
            }
        }
        else if (offset.x != 0) // Left or right
        {
            // Check the right face of left chunk, or left face of right chunk
            neighborX = offset.x > 0 ? 0 : chunkSize - 1;
            
            for (int y = 0; y < chunkHeight; y++)
            for (int z = 0; z < chunkSize; z++)
            {
                Voxel neighborVoxel = neighborChunk.GetVoxel(neighborX, y, z);
                if (neighborVoxel.lightLevel > 0)
                {
                    // Check if this position in our chunk is air and should receive light
                    int localX = offset.x > 0 ? chunkSize - 1 : 0;
                    Voxel localVoxel = target.GetVoxel(localX, y, z);
                    
                    if (localVoxel.type == Voxel.VoxelType.Air && localVoxel.lightLevel < neighborVoxel.lightLevel)
                    {
                        // Calculate attenuated light level
                        byte newLight = (byte)Mathf.Max(0, neighborVoxel.lightLevel - lightAttenuation);
                        if (newLight > localVoxel.lightLevel)
                        {
                            localVoxel.lightLevel = newLight;
                            target.SetVoxel(localX, y, z, localVoxel);
                            lightSources.Enqueue(new Vector3Int(localX, y, z));
                        }
                    }
                }
            }
        }
        else if (offset.z != 0) // Front or back
        {
            // Check the front face of back chunk, or back face of front chunk
            neighborZ = offset.z > 0 ? 0 : chunkSize - 1;
            
            for (int x = 0; x < chunkSize; x++)
            for (int y = 0; y < chunkHeight; y++)
            {
                Voxel neighborVoxel = neighborChunk.GetVoxel(x, y, neighborZ);
                if (neighborVoxel.lightLevel > 0)
                {
                    // Check if this position in our chunk is air and should receive light
                    int localZ = offset.z > 0 ? chunkSize - 1 : 0;
                    Voxel localVoxel = target.GetVoxel(x, y, localZ);
                    
                    if (localVoxel.type == Voxel.VoxelType.Air && localVoxel.lightLevel < neighborVoxel.lightLevel)
                    {
                        // Calculate attenuated light level
                        byte newLight = (byte)Mathf.Max(0, neighborVoxel.lightLevel - lightAttenuation);
                        if (newLight > localVoxel.lightLevel)
                        {
                            localVoxel.lightLevel = newLight;
                            target.SetVoxel(x, y, localZ, localVoxel);
                            lightSources.Enqueue(new Vector3Int(x, y, localZ));
                        }
                    }
                }
            }
        }
    }
} 
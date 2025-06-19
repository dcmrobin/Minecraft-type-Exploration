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
        return GenerateStage(chunkPos, 0, chunkSize, chunkHeight, world, cache);
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
            for (int dx = -border; dx <= border; dx++)
            for (int dy = -border; dy <= border; dy++)
            for (int dz = -border; dz <= border; dz++)
            {
                if (dx == 0 && dy == 0 && dz == 0) continue;
                Vector3Int offset = new Vector3Int(dx, dy, dz);
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
}

// --- Terrain Shaping Stage with Upsampling ---
public class TerrainShapingStage : IChunkGenerationStage
{
    public int BorderSize => 1; // For future neighbor-aware stages

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
    public int BorderSize => 1;
    private const int maxSoilDepth = 5;
    private const int maxSurfaceSearch = 12;
    private const float sandHeight = -8f;
    private const float sandNoiseScale = 16f;
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
            for (int i = 0; i < 4; i++) adjSurface[i] = surfaceY;
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
                        return target.GetVoxel(xx, yy, zz);
                    // Cross-chunk neighbor
                    if (neighborSnapshots.TryGetValue(adjOffset, out var neighbor))
                        return neighbor.GetVoxel(lx, yy, lz);
                    return new Voxel(Voxel.VoxelType.Air, false);
                }
                for (int y = chunkHeight - 1; y >= 0; y--)
                {
                    if (getVoxel(nx, y, nz).type == Voxel.VoxelType.Stone)
                    {
                        // Look for air above
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
            int minAdj = Mathf.Min(adjSurface);
            int maxAdj = Mathf.Max(adjSurface);
            int slope = Mathf.Abs(surfaceY - minAdj) + Mathf.Abs(surfaceY - maxAdj);
            int soilDepth = Mathf.Max(1, maxSoilDepth - slope);

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

// --- Lighting Stage ---
public class LightingStage : IChunkGenerationStage
{
    public int BorderSize => 1; // Need neighbors for seamless lighting
    private const byte maxLight = 15;
    public void Generate(
        Vector3Int chunkPos,
        OptimizedVoxelStorage target,
        Dictionary<Vector3Int, OptimizedVoxelStorage> neighborSnapshots,
        World world)
    {
        int chunkSize = world.chunkSize;
        int chunkHeight = world.chunkHeight;

        // Step 0: Set all voxels to lightLevel = 0
        for (int x = 0; x < chunkSize; x++)
        for (int y = 0; y < chunkHeight; y++)
        for (int z = 0; z < chunkSize; z++)
        {
            var voxel = target.GetVoxel(x, y, z);
            voxel.lightLevel = 0;
            target.SetVoxel(x, y, z, voxel);
        }

        // Step 1: Set sky light for air blocks exposed to the sky
        for (int x = 0; x < chunkSize; x++)
        for (int z = 0; z < chunkSize; z++)
        {
            bool sky = true;
            for (int y = chunkHeight - 1; y >= 0; y--)
            {
                var voxel = target.GetVoxel(x, y, z);
                if (sky && voxel.type == Voxel.VoxelType.Air)
                {
                    voxel.lightLevel = maxLight;
                    target.SetVoxel(x, y, z, voxel);
                }
                else if (voxel.type != Voxel.VoxelType.Air)
                {
                    sky = false;
                }
            }
        }

        // Step 2: Propagate light through air blocks only
        Queue<(int x, int y, int z)> queue = new Queue<(int, int, int)>();
        for (int x = 0; x < chunkSize; x++)
        for (int y = 0; y < chunkHeight; y++)
        for (int z = 0; z < chunkSize; z++)
        {
            if (target.GetVoxel(x, y, z).lightLevel == maxLight)
                queue.Enqueue((x, y, z));
        }
        int[] dx = { -1, 1, 0, 0, 0, 0 };
        int[] dy = { 0, 0, -1, 1, 0, 0 };
        int[] dz = { 0, 0, 0, 0, -1, 1 };
        while (queue.Count > 0)
        {
            var (x, y, z) = queue.Dequeue();
            byte curLight = target.GetVoxel(x, y, z).lightLevel;
            if (curLight <= 1) continue;
            for (int dir = 0; dir < 6; dir++)
            {
                int nx = x + dx[dir];
                int ny = y + dy[dir];
                int nz = z + dz[dir];
                Voxel neighborVoxel;
                if (nx >= 0 && nx < chunkSize && ny >= 0 && ny < chunkHeight && nz >= 0 && nz < chunkSize)
                {
                    neighborVoxel = target.GetVoxel(nx, ny, nz);
                }
                else
                {
                    Vector3Int offset = new Vector3Int(
                        nx < 0 ? -1 : (nx >= chunkSize ? 1 : 0),
                        ny < 0 ? -1 : (ny >= chunkHeight ? 1 : 0),
                        nz < 0 ? -1 : (nz >= chunkSize ? 1 : 0)
                    );
                    int lx = (nx + chunkSize) % chunkSize;
                    int ly = (ny + chunkHeight) % chunkHeight;
                    int lz = (nz + chunkSize) % chunkSize;
                    if (neighborSnapshots.TryGetValue(offset, out var neighborChunk))
                        neighborVoxel = neighborChunk.GetVoxel(lx, ly, lz);
                    else
                        continue;
                }
                // Only propagate into air blocks
                if (neighborVoxel.type == Voxel.VoxelType.Air && neighborVoxel.lightLevel + 2 <= curLight)
                {
                    neighborVoxel.lightLevel = (byte)(curLight - 2);
                    if (nx >= 0 && nx < chunkSize && ny >= 0 && ny < chunkHeight && nz >= 0 && nz < chunkSize)
                        target.SetVoxel(nx, ny, nz, neighborVoxel);
                    queue.Enqueue((nx, ny, nz));
                }
            }
        }
    }
} 
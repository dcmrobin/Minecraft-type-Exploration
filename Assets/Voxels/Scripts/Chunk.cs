using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using System.Threading.Tasks;
using System.Diagnostics;
using VoxelEngine;
using UnityEngine.Rendering;

public class Chunk : MonoBehaviour
{
    public OptimizedVoxelStorage voxels;
    public int chunkSize = 16;
    public int chunkHeight = 16;
    private AnimationCurve continentalnessCurve;
    private float noiseFrequency;
    private float noiseAmplitude;
    private float minTerrainHeight;
    private readonly List<Vector3> vertices = new();
    private readonly List<int> triangles = new();
    private readonly List<Vector2> uvs = new();
    List<Color> colors = new();
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;

    public Vector3 pos;

    private bool isVisible = true;
    public Bounds chunkBounds;
    private static readonly Vector3[] frustumCorners = new Vector3[8];

    private static readonly Queue<Mesh> meshPool = new Queue<Mesh>();
    private static readonly int maxPoolSize = 20;
    private bool needsFullRegeneration = true;
    private HashSet<Vector3Int> modifiedBlocks = new HashSet<Vector3Int>();

    private Dictionary<Vector3Int, Chunk> neighborChunkCache = new Dictionary<Vector3Int, Chunk>();

    private void Awake() {
        pos = transform.position;
        // Initialize chunk bounds
        chunkBounds = new Bounds(
            new Vector3(chunkSize / 2f, chunkHeight / 2f, chunkSize / 2f),
            new Vector3(chunkSize, chunkHeight, chunkSize)
        );
    }

    private Mesh GetMeshFromPool()
    {
        if (meshPool.Count > 0)
        {
            Mesh mesh = meshPool.Dequeue();
            mesh.Clear();
            return mesh;
        }
        return new Mesh();
    }

    private void ReturnMeshToPool(Mesh mesh)
    {
        if (meshPool.Count < maxPoolSize)
        {
            meshPool.Enqueue(mesh);
        }
        else
        {
            Destroy(mesh);
        }
    }

    public void MarkBlockModified(int x, int y, int z)
    {
        modifiedBlocks.Add(new Vector3Int(x, y, z));
        needsFullRegeneration = false;
    }

    public void GenerateMesh()
    {
        if (needsFullRegeneration)
        {
            GenerateFullMesh();
        }
        else
        {
            GeneratePartialMesh();
        }
    }

    private void GenerateFullMesh()
    {
        // Early exit if no voxels are present
        if (voxels == null || voxels.IsEmpty())
        {
            if (meshFilter.mesh != null)
            {
                ReturnMeshToPool(meshFilter.mesh);
                meshFilter.mesh = null;
                meshCollider.sharedMesh = null;
            }
            return;
        }

        // Clear the neighbor chunk cache before generating a new mesh
        neighborChunkCache.Clear();
        
        vertices.Clear();
        triangles.Clear();
        colors.Clear();

        // First pass: identify visible faces and cache neighbor chunks
        for (int y = 0; y < chunkHeight; y++)
        {
            for (int x = 0; x < chunkSize; x++)
            {
                for (int z = 0; z < chunkSize; z++)
                {
                    Voxel voxel = voxels.GetVoxel(x, y, z);
                    if (voxel.type != Voxel.VoxelType.Air)
                    {
                        // Check each face and cache neighbor chunks if needed
                        CheckAndCacheNeighborChunks(x, y, z);
                    }
                }
            }
        }

        // Process top and bottom faces with greedy meshing
        for (int y = 0; y < chunkHeight; y++)
        {
            bool[,] topMask = new bool[chunkSize, chunkSize];
            bool[,] bottomMask = new bool[chunkSize, chunkSize];
            Voxel.VoxelType[,] topTypes = new Voxel.VoxelType[chunkSize, chunkSize];
            Voxel.VoxelType[,] bottomTypes = new Voxel.VoxelType[chunkSize, chunkSize];

            // Create masks for this layer
            for (int x = 0; x < chunkSize; x++)
            {
                for (int z = 0; z < chunkSize; z++)
                {
                    Voxel voxel = voxels.GetVoxel(x, y, z);
                    if (voxel.type != Voxel.VoxelType.Air)
                    {
                        // Check top face
                        if (IsVoxelHiddenInChunk(x, y + 1, z))
                        {
                            topMask[x, z] = true;
                            topTypes[x, z] = voxel.type;
                        }

                        // Check bottom face
                        if (IsVoxelHiddenInChunk(x, y - 1, z))
                        {
                            bottomMask[x, z] = true;
                            bottomTypes[x, z] = voxel.type;
                        }
                    }
                }
            }

            // Greedy meshing for top faces
            GreedyMeshLayer(y, true, topMask, topTypes);
            // Greedy meshing for bottom faces
            GreedyMeshLayer(y, false, bottomMask, bottomTypes);
        }

        // Process side faces with greedy meshing
        // Left and right faces
        for (int x = 0; x < chunkSize; x++)
        {
            GreedyMeshSlice(x, true, true);  // Right face
            GreedyMeshSlice(x, true, false); // Left face
        }

        // Front and back faces
        for (int z = 0; z < chunkSize; z++)
        {
            GreedyMeshSlice(z, false, true);  // Front face
            GreedyMeshSlice(z, false, false); // Back face
        }

        // Early exit if no vertices were generated
        if (vertices.Count == 0)
        {
            if (meshFilter.mesh != null)
            {
                ReturnMeshToPool(meshFilter.mesh);
                meshFilter.mesh = null;
                meshCollider.sharedMesh = null;
            }
            return;
        }

        // Create and update the mesh
        Mesh mesh = GetMeshFromPool();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetColors(colors);
        mesh.RecalculateNormals();
        meshFilter.mesh = mesh;
        meshCollider.sharedMesh = mesh;

        needsFullRegeneration = true;
        modifiedBlocks.Clear();
    }

    private void GeneratePartialMesh()
    {
        // Clear the AO cache before generating a partial mesh
        ClearAOCache();
        
        if (modifiedBlocks.Count == 0) return;

        // Store the current mesh data
        Mesh currentMesh = meshFilter.mesh;
        Vector3[] oldVertices = currentMesh.vertices;
        int[] oldTriangles = currentMesh.triangles;
        Vector2[] oldUVs = currentMesh.uv;
        Color[] oldColors = currentMesh.colors;

        // Clear lists for new mesh data
        vertices.Clear();
        triangles.Clear();
        colors.Clear();

        // Process only the modified blocks and their neighbors
        HashSet<Vector3Int> blocksToProcess = new HashSet<Vector3Int>();
        foreach (var block in modifiedBlocks)
        {
            blocksToProcess.Add(block);
            // Add neighboring blocks
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        if (x == 0 && y == 0 && z == 0) continue;
                        Vector3Int neighbor = block + new Vector3Int(x, y, z);
                        if (IsInChunk(neighbor))
                        {
                            blocksToProcess.Add(neighbor);
                        }
                    }
                }
            }
        }

        // Generate mesh for the modified blocks
        GenerateMesh();

        modifiedBlocks.Clear();
    }

    private bool IsInChunk(Vector3Int pos)
    {
        return pos.x >= 0 && pos.x < chunkSize &&
               pos.y >= 0 && pos.y < chunkHeight &&
               pos.z >= 0 && pos.z < chunkSize;
    }

    public void SetVoxel(int x, int y, int z, Voxel voxel)
    {
        voxels.SetVoxel(x, y, z, voxel);
        MarkBlockModified(x, y, z);
    }

    private void GreedyMeshSlice(int slice, bool isX, bool isPositive)
    {
        bool[,] mask = new bool[isX ? chunkHeight : chunkSize, isX ? chunkSize : chunkHeight];
        Voxel.VoxelType[,] types = new Voxel.VoxelType[isX ? chunkHeight : chunkSize, isX ? chunkSize : chunkHeight];

        // Create the mask and type arrays
        for (int i = 0; i < (isX ? chunkHeight : chunkSize); i++)
        {
            for (int j = 0; j < (isX ? chunkSize : chunkHeight); j++)
            {
                int x = isX ? slice : j;
                int y = isX ? i : slice;
                int z = isX ? j : i;

                if (voxels.GetVoxel(x, y, z).type != Voxel.VoxelType.Air)
                {
                    bool shouldRender = isX ?
                        (isPositive ? IsVoxelHiddenInChunk(x + 1, y, z) : IsVoxelHiddenInChunk(x - 1, y, z)) :
                        (isPositive ? IsVoxelHiddenInChunk(x, y, z + 1) : IsVoxelHiddenInChunk(x, y, z - 1));

                    if (shouldRender)
                    {
                        mask[i, j] = true;
                        types[i, j] = voxels.GetVoxel(x, y, z).type;
                    }
                }
            }
        }

        // Greedy meshing
        for (int i = 0; i < (isX ? chunkHeight : chunkSize); i++)
        {
            for (int j = 0; j < (isX ? chunkSize : chunkHeight); j++)
            {
                if (!mask[i, j]) continue;

                Voxel.VoxelType type = types[i, j];
                int width = 1;
                int height = 1;

                // Find width
                while (j + width < (isX ? chunkSize : chunkHeight) && mask[i, j + width] && types[i, j + width] == type)
                {
                    width++;
                }

                // Find height
                bool canExpand = true;
                while (canExpand && i + height < (isX ? chunkHeight : chunkSize))
                {
                    for (int k = 0; k < width; k++)
                    {
                        if (!mask[i + height, j + k] || types[i + height, j + k] != type)
                        {
                            canExpand = false;
                            break;
                        }
                    }
                    if (canExpand) height++;
                }

                // Create the quad
                int x = isX ? slice : j;
                int y = isX ? i : slice;
                int z = isX ? j : i;
                Vector3 pos = new Vector3(x, y, z);

                if (isX)
                {
                    if (isPositive)
                        AddGreedyQuad(pos, height, width, 3, type); // Right face
                    else
                        AddGreedyQuad(pos, height, width, 2, type); // Left face
                }
                else
                {
                    if (isPositive)
                        AddGreedyQuad(pos, width, height, 4, type); // Front face
                    else
                        AddGreedyQuad(pos, width, height, 5, type); // Back face
                }

                // Clear the mask for this rectangle
                for (int k = 0; k < width; k++)
                {
                    for (int l = 0; l < height; l++)
                    {
                        mask[i + l, j + k] = false;
                    }
                }
            }
        }
    }

    private void AddGreedyQuad(Vector3 position, int width, int height, int faceIndex, Voxel.VoxelType type)
    {
        int vertCount = vertices.Count;

        // Add vertices based on face
        switch (faceIndex)
        {
            case 0: // Top Face
                vertices.Add(new Vector3(position.x, position.y + 1, position.z));
                vertices.Add(new Vector3(position.x, position.y + 1, position.z + height));
                vertices.Add(new Vector3(position.x + width, position.y + 1, position.z + height));
                vertices.Add(new Vector3(position.x + width, position.y + 1, position.z));
                break;
            case 1: // Bottom Face
                vertices.Add(new Vector3(position.x, position.y, position.z));
                vertices.Add(new Vector3(position.x + width, position.y, position.z));
                vertices.Add(new Vector3(position.x + width, position.y, position.z + height));
                vertices.Add(new Vector3(position.x, position.y, position.z + height));
                break;
            case 2: // Left Face
                vertices.Add(new Vector3(position.x, position.y, position.z));
                vertices.Add(new Vector3(position.x, position.y, position.z + height));
                vertices.Add(new Vector3(position.x, position.y + width, position.z + height));
                vertices.Add(new Vector3(position.x, position.y + width, position.z));
                break;
            case 3: // Right Face
                vertices.Add(new Vector3(position.x + 1, position.y, position.z + height));
                vertices.Add(new Vector3(position.x + 1, position.y, position.z));
                vertices.Add(new Vector3(position.x + 1, position.y + width, position.z));
                vertices.Add(new Vector3(position.x + 1, position.y + width, position.z + height));
                break;
            case 4: // Front Face
                vertices.Add(new Vector3(position.x, position.y, position.z + 1));
                vertices.Add(new Vector3(position.x + width, position.y, position.z + 1));
                vertices.Add(new Vector3(position.x + width, position.y + height, position.z + 1));
                vertices.Add(new Vector3(position.x, position.y + height, position.z + 1));
                break;
            case 5: // Back Face
                vertices.Add(new Vector3(position.x + width, position.y, position.z));
                vertices.Add(new Vector3(position.x, position.y, position.z));
                vertices.Add(new Vector3(position.x, position.y + height, position.z));
                vertices.Add(new Vector3(position.x + width, position.y + height, position.z));
                break;
        }

        // Add colors with light levels
        for (int i = 0; i < 4; i++)
        {
            Color color = GetBlockColor(type);
            
            // Get light level from the block itself
            int x = (int)position.x;
            int y = (int)position.y;
            int z = (int)position.z;
            
            // Get the light level from the block and normalize it to 0.1-1.0 range
            Voxel voxel = voxels.GetVoxel(x, y, z);
            // Convert from 0-15 to 0.1-1.0 range
            color.g = 0.1f + (voxel.lightLevel / 15.0f * 0.9f);
            colors.Add(color);
        }

        // Add triangles
        triangles.Add(vertCount);
        triangles.Add(vertCount + 1);
        triangles.Add(vertCount + 2);
        triangles.Add(vertCount);
        triangles.Add(vertCount + 2);
        triangles.Add(vertCount + 3);
    }

    private void ClearAOCache()
    {
        // Removed AO cache
    }

    // New Initialize method for pre-generated voxel data (no terrain params)
    public void Initialize(int size, int height, OptimizedVoxelStorage preGeneratedVoxels)
    {
        chunkSize = size;
        chunkHeight = height;
        voxels = preGeneratedVoxels;
        meshFilter = gameObject.AddComponent<MeshFilter>();
        meshRenderer = gameObject.AddComponent<MeshRenderer>();
        meshCollider = gameObject.AddComponent<MeshCollider>();
        meshRenderer.material = World.Instance.VoxelMaterial;

        // Set up occlusion culling after components are created
        meshRenderer.allowOcclusionWhenDynamic = true;
        meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;

        // Initialize chunk bounds
        chunkBounds = new Bounds(
            new Vector3(chunkSize / 2f, chunkHeight / 2f, chunkSize / 2f),
            new Vector3(chunkSize, chunkHeight, chunkSize)
        );

        // Initially disable the mesh renderer
        meshRenderer.enabled = false;

        // Only update lighting and mesh, do not generate voxel data here
        UpdateLighting();
        GenerateFullMesh();
    }

    public void ActivateMesh()
    {
        if (meshRenderer != null)
        {
            meshRenderer.enabled = true;
        }
    }

    public void DeactivateMesh()
    {
        if (meshRenderer != null)
        {
            meshRenderer.enabled = false;
        }
    }

    public void ResetChunk() {
        // Clear voxel data
        voxels.Clear();

        // Clear mesh data
        vertices.Clear();
        triangles.Clear();
        uvs.Clear();
        colors.Clear();

        // Regenerate voxel data
        GenerateFullMesh();
    }

    public void UpdateAdjacentFaces(Vector3Int direction)
    {
        // Store the current mesh data
        Vector3[] oldVertices = meshFilter.mesh.vertices;
        int[] oldTriangles = meshFilter.mesh.triangles;
        Vector2[] oldUVs = meshFilter.mesh.uv;
        Color[] oldColors = meshFilter.mesh.colors;

        // Clear lists for new mesh data
        vertices.Clear();
        triangles.Clear();
        uvs.Clear();
        colors.Clear();

        // Determine which faces to check based on the direction
        int startX = direction.x == 1 ? chunkSize - 1 : 0;
        int endX = direction.x == -1 ? 1 : chunkSize;
        int startY = direction.y == 1 ? chunkHeight - 1 : 0;
        int endY = direction.y == -1 ? 1 : chunkHeight;
        int startZ = direction.z == 1 ? chunkSize - 1 : 0;
        int endZ = direction.z == -1 ? 1 : chunkSize;

        // Create a HashSet to track which vertices we've updated
        HashSet<int> updatedVertexIndices = new HashSet<int>();

        // Only process voxels on the face that's adjacent to the new chunk
        for (int y = startY; y < endY; y++)
        {
            for (int x = startX; x < endX; x++)
            {
                for (int z = startZ; z < endZ; z++)
                {
                    if (voxels.GetVoxel(x, y, z).type != Voxel.VoxelType.Air)
                    {
                        MarkBlockModified(x, y, z);
                    }
                }
            }
        }

        // Generate mesh for the modified blocks
        GenerateMesh();
    }

    public void UpdateVisibility(Camera mainCamera)
    {
        if (mainCamera == null) return;

        // Get camera frustum corners
        mainCamera.CalculateFrustumCorners(
            new Rect(0, 0, 1, 1),
            mainCamera.farClipPlane,
            Camera.MonoOrStereoscopicEye.Mono,
            frustumCorners
        );

        // Transform frustum corners to world space
        for (int i = 0; i < 4; i++)
        {
            frustumCorners[i] = mainCamera.transform.TransformPoint(frustumCorners[i]);
            frustumCorners[i + 4] = mainCamera.transform.TransformPoint(frustumCorners[i + 4]);
        }

        // Check if chunk bounds intersect with frustum
        isVisible = GeometryUtility.TestPlanesAABB(
            GeometryUtility.CalculateFrustumPlanes(mainCamera),
            new Bounds(transform.position + chunkBounds.center, chunkBounds.size)
        );

        // Only deactivate/activate the mesh renderer based on visibility
        meshRenderer.enabled = isVisible;
    }

    private Color GetBlockColor(Voxel.VoxelType type)
    {
        // Store block type in color.r (using the actual enum value)
        float blockType = (float)type;
        return new Color(blockType, 0, 0, 1);
    }

    private void UpdateLighting()
    {
        // Create native arrays for the jobs
        NativeArray<byte> lightLevels = new NativeArray<byte>(chunkSize * chunkHeight * chunkSize, Allocator.TempJob);
        NativeArray<Voxel> voxelArray = new NativeArray<Voxel>(chunkSize * chunkHeight * chunkSize, Allocator.TempJob);

        // Copy voxel data to native array
        for (int i = 0; i < voxelArray.Length; i++)
        {
            int x = i % chunkSize;
            int y = (i / chunkSize) % chunkHeight;
            int z = i / (chunkSize * chunkHeight);
            voxelArray[i] = voxels.GetVoxel(x, y, z);
        }

        // Create and schedule the initial lighting job
        LightingJob lightingJob = new LightingJob
        {
            voxels = voxelArray,
            chunkSize = chunkSize,
            chunkHeight = chunkHeight,
            chunkWorldPosition = transform.position,
            lightLevels = lightLevels
        };

        JobHandle lightingHandle = lightingJob.Schedule(voxelArray.Length, 64);
        lightingHandle.Complete();

        // Create and schedule the light propagation job
        LightPropagationJob propagationJob = new LightPropagationJob
        {
            voxels = voxelArray,
            chunkSize = chunkSize,
            chunkHeight = chunkHeight,
            lightLevels = lightLevels
        };

        JobHandle propagationHandle = propagationJob.Schedule();
        propagationHandle.Complete();

        // Copy light levels back to voxels
        for (int i = 0; i < lightLevels.Length; i++)
        {
            int x = i % chunkSize;
            int y = (i / chunkSize) % chunkHeight;
            int z = i / (chunkSize * chunkHeight);
            Voxel voxel = voxels.GetVoxel(x, y, z);
            voxel.lightLevel = lightLevels[i];
            voxels.SetVoxel(x, y, z, voxel);
            if (voxel.lightLevel > 0)
            {
                MarkBlockModified(x, y, z);
            }
        }

        // Clean up
        lightLevels.Dispose();
        voxelArray.Dispose();
    }

    public void AddFaceData(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs, List<Color> colors, int faceIndex, Vector3 position, Voxel.VoxelType type)
    {
        Vector2[] faceUVs = GetFaceUVs(type, faceIndex);

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
            colors.Add(new Color(0, 0, 0, 1));
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

    public static Vector2[] GetFaceUVs(Voxel.VoxelType type, int faceIndex)
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

    public static Vector2 GetTileOffset(Voxel.VoxelType type, int faceIndex)
    {
        switch (type)
        {
            case Voxel.VoxelType.Grass:
                if (faceIndex == 0) // Top face
                    return new Vector2(0, 0.75f);
                if (faceIndex == 1) // Bottom face
                    return new Vector2(0.25f, 0.75f); // Use dirt texture for bottom
                return new Vector2(0, 0.5f); // Side faces

            case Voxel.VoxelType.Dirt:
                return new Vector2(0.25f, 0.75f);

            case Voxel.VoxelType.Stone:
                return new Vector2(0.25f, 0.5f);

            case Voxel.VoxelType.Deepslate:
                if (faceIndex == 0) // Top face
                    return new Vector2(0.5f, 0.5f);
                if (faceIndex == 1) // Bottom face
                    return new Vector2(0.5f, 0.5f);
                return new Vector2(0.5f, 0.75f); // Side faces

            case Voxel.VoxelType.Sand:
                return new Vector2(0.75f, 0.75f);

            // Add more cases for other types...

            default:
                return Vector2.zero;
        }
    }

    private bool IsVoxelHiddenInChunk(int x, int y, int z)
    {
        // Check if the position is in a neighboring chunk
        if (x < 0 || x >= chunkSize || y < 0 || y >= chunkHeight || z < 0 || z >= chunkSize)
        {
            // Get the world position of the neighboring voxel
            Vector3 worldPos = transform.position + new Vector3(x, y, z);
            Vector3Int neighborChunkPos = World.Instance.GetChunkPosition(worldPos);
            
            // Use cached chunk if available
            if (neighborChunkCache.TryGetValue(neighborChunkPos, out Chunk neighborChunk))
            {
                Vector3Int localPos = new Vector3Int(
                    Mathf.FloorToInt(worldPos.x) - (neighborChunkPos.x * chunkSize),
                    Mathf.FloorToInt(worldPos.y) - (neighborChunkPos.y * chunkHeight),
                    Mathf.FloorToInt(worldPos.z) - (neighborChunkPos.z * chunkSize)
                );
                return neighborChunk.voxels.GetVoxel(localPos.x, localPos.y, localPos.z).type == Voxel.VoxelType.Air;
            }
            return true; // If no neighboring chunk exists, consider it hidden
        }

        // For positions within this chunk, check if the voxel is air
        return voxels.GetVoxel(x, y, z).type == Voxel.VoxelType.Air;
    }

    private Voxel GetVoxelSafe(int x, int y, int z)
    {
        if (x < 0 || x >= chunkSize || y < 0 || y >= chunkHeight || z < 0 || z >= chunkSize)
        {
            return new Voxel(Voxel.VoxelType.Air);
        }
        return voxels.GetVoxel(x, y, z);
    }

    public Vector3Int NeighborXYZ(Vector3Int pos)
    {
        Vector3Int neighborPos = new();

        if (pos.x < 0)
            neighborPos.x = chunkSize - 1;
        else if (pos.x > chunkSize - 1)
            neighborPos.x = 0;
        else if (pos.y < 0)
            neighborPos.y = chunkHeight - 1;
        else if (pos.y > chunkHeight - 1)
            neighborPos.y = 0;
        else if (pos.z < 0)
            neighborPos.z = chunkSize - 1;
        else if (pos.z > chunkSize - 1)
            neighborPos.z = 0;

        return neighborPos;
    }

    private void GreedyMeshLayer(int y, bool isTop, bool[,] mask, Voxel.VoxelType[,] types)
    {
        // Greedy meshing
        for (int x = 0; x < chunkSize; x++)
        {
            for (int z = 0; z < chunkSize; z++)
            {
                if (!mask[x, z]) continue;

                Voxel.VoxelType type = types[x, z];
                int width = 1;
                int height = 1;

                // Find width
                while (x + width < chunkSize && mask[x + width, z] && types[x + width, z] == type)
                {
                    width++;
                }

                // Find height
                bool canExpand = true;
                while (canExpand && z + height < chunkSize)
                {
                    for (int i = 0; i < width; i++)
                    {
                        if (!mask[x + i, z + height] || types[x + i, z + height] != type)
                        {
                            canExpand = false;
                            break;
                        }
                    }
                    if (canExpand) height++;
                }

                // Create the quad
                Vector3 pos = new Vector3(x, y, z);
                if (isTop)
                {
                    AddGreedyQuad(pos, width, height, 0, type); // Top face
                }
                else
                {
                    AddGreedyQuad(pos, width, height, 1, type); // Bottom face
                }

                // Clear the mask for this rectangle
                for (int i = 0; i < width; i++)
                {
                    for (int j = 0; j < height; j++)
                    {
                        mask[x + i, z + j] = false;
                    }
                }
            }
        }
    }

    private void CheckAndCacheNeighborChunks(int x, int y, int z)
    {
        // Check all 6 directions
        int[] dx = { -1, 1, 0, 0, 0, 0 };
        int[] dy = { 0, 0, -1, 1, 0, 0 };
        int[] dz = { 0, 0, 0, 0, -1, 1 };

        for (int i = 0; i < 6; i++)
        {
            int nx = x + dx[i];
            int ny = y + dy[i];
            int nz = z + dz[i];

            if (nx < 0 || nx >= chunkSize || ny < 0 || ny >= chunkHeight || nz < 0 || nz >= chunkSize)
            {
                Vector3 worldPos = transform.position + new Vector3(nx, ny, nz);
                Vector3Int neighborChunkPos = World.Instance.GetChunkPosition(worldPos);
                
                if (!neighborChunkCache.ContainsKey(neighborChunkPos))
                {
                    Chunk neighborChunk = World.Instance.GetChunkAt(neighborChunkPos);
                    if (neighborChunk != null)
                    {
                        neighborChunkCache[neighborChunkPos] = neighborChunk;
                    }
                }
            }
        }
    }
}
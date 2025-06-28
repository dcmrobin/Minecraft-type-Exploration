using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using System.Threading.Tasks;
using System.Collections;

public class World : MonoBehaviour
{
    [Header("Lighting")]
    [Range(0f, 1f)]
    public float globalLightLevel = 1f;
    public Color dayColor = Color.white;
    public Color nightColor = new Color(0.2f, 0.2f, 0.3f);
    public static float minLightLevel = 0.1f;
    public static float maxLightLevel = 1f;
    public static float lightFalloff = 0.0625f; // 1/16 for Minecraft-style lighting
    public static float maxLightDistance = 16f; // Maximum light propagation distance

    [Header("World")]
    public int chunksPerFrame = 5; // Number of chunks to load per frame
    public bool useVerticalChunks = true;
    public int worldSize = 5; 
    public int chunkSize = 16;
    public int chunkHeight = 16;
    public Material VoxelMaterial;
    public int renderDistance = 5;
    public int safetyRadius = 4;

    private Dictionary<Vector3Int, Chunk> chunks = new Dictionary<Vector3Int, Chunk>();
    private Queue<Vector3Int> chunkLoadQueue = new Queue<Vector3Int>();
    private Transform player;
    private Vector3Int lastPlayerChunkPos;
    public static World Instance { get; private set; }

    private Camera mainCamera;
    private float cullingUpdateInterval = 0.1f; // Update culling every 0.1 seconds
    private float lastCullingUpdate;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        mainCamera = Camera.main;
        
        // Set up occlusion culling
        if (mainCamera != null)
        {
            mainCamera.useOcclusionCulling = true;
            mainCamera.allowHDR = false; // Disable HDR for better performance
            mainCamera.allowMSAA = false; // Disable MSAA for better performance
        }

        player = FindObjectOfType<PlayerController>().transform;
        lastPlayerChunkPos = GetChunkPosition(player.position);
        LoadChunksAround(lastPlayerChunkPos);
        Shader.SetGlobalFloat("minGlobalLightLevel", minLightLevel);
        Shader.SetGlobalFloat("maxGlobalLightLevel", maxLightLevel);
    }

    void Update()
    {
        // Update frustum culling
        if (Time.time - lastCullingUpdate >= cullingUpdateInterval)
        {
            UpdateChunkVisibility();
            lastCullingUpdate = Time.time;
        }

        chunkHeight = useVerticalChunks ? 16 : 260;

        Shader.SetGlobalFloat("GlobalLightLevel", globalLightLevel);
        player.GetComponentInChildren<Camera>().backgroundColor = Color.Lerp(nightColor, dayColor, globalLightLevel);

        Vector3Int currentPlayerChunkPos = GetChunkPosition(player.position);

        if (currentPlayerChunkPos != lastPlayerChunkPos)
        {
            LoadChunksAround(currentPlayerChunkPos);
            UnloadDistantChunks(currentPlayerChunkPos);
            lastPlayerChunkPos = currentPlayerChunkPos;
        }

        // Load a specified number of chunks per frame
        int chunksToLoad = Mathf.Min(chunkLoadQueue.Count, chunksPerFrame);
        for (int i = 0; i < chunksToLoad; i++)
        {
            if (chunkLoadQueue.Count > 0)
            {
                CreateChunk(chunkLoadQueue.Dequeue());
            }
        }
    }

    private void UpdateChunkVisibility()
    {
        if (mainCamera == null) return;

        foreach (var chunk in chunks.Values)
        {
            if (chunk != null)
            {
                // Check if chunk is in view
                bool isInView = GeometryUtility.TestPlanesAABB(
                    GeometryUtility.CalculateFrustumPlanes(mainCamera),
                    new Bounds(chunk.transform.position + chunk.chunkBounds.center, chunk.chunkBounds.size)
                );

                // Calculate distance from player
                float distanceFromPlayer = Vector3.Distance(
                    chunk.transform.position,
                    player.position
                );

                // Always keep chunks within safety radius active
                if (distanceFromPlayer <= safetyRadius * chunkSize)
                {
                    chunk.ActivateMesh();
                    continue;
                }

                // For chunks outside safety radius, only activate if they're in view and in front of player
                if (isInView)
                {
                    Vector3 toChunk = chunk.transform.position - player.position;
                    float dotProduct = Vector3.Dot(toChunk.normalized, player.forward);

                    if (dotProduct > 0)
                    {
                        chunk.ActivateMesh();
                    }
                    else
                    {
                        chunk.DeactivateMesh();
                    }
                }
                else
                {
                    chunk.DeactivateMesh();
                }
            }
        }
    }

    public Vector3Int GetChunkPosition(Vector3 position)
    {
        return new Vector3Int(
            Mathf.FloorToInt(position.x / chunkSize),
            Mathf.FloorToInt(position.y / chunkHeight),
            Mathf.FloorToInt(position.z / chunkSize)
        );
    }

    private bool IsChunkPotentiallyVisible(Vector3Int chunkPos)
    {
        if (mainCamera == null) return true;

        // Calculate chunk bounds in world space
        Bounds chunkBounds = new Bounds(
            new Vector3(
                chunkPos.x * chunkSize + chunkSize / 2f,
                chunkPos.y * chunkHeight + chunkHeight / 2f,
                chunkPos.z * chunkSize + chunkSize / 2f
            ),
            new Vector3(chunkSize, chunkHeight, chunkSize)
        );

        // Get camera frustum planes
        Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(mainCamera);

        // Check if chunk bounds intersect with any frustum plane
        return GeometryUtility.TestPlanesAABB(frustumPlanes, chunkBounds);
    }

    private void LoadChunksAround(Vector3Int centerChunkPos)
    {
        chunkLoadQueue.Clear();

        // Get player's forward direction in chunk coordinates
        Vector3 playerForward = player.forward;
        Vector3Int forwardDir = new Vector3Int(
            Mathf.RoundToInt(playerForward.x),
            Mathf.RoundToInt(playerForward.y),
            Mathf.RoundToInt(playerForward.z)
        );

        // Spiral generation from center outward
        int maxDistance = renderDistance;
        int currentDistance = 0;

        // Start with the center chunk
        Vector3Int centerPos = centerChunkPos;
        if (!chunks.ContainsKey(centerPos))
        {
            chunkLoadQueue.Enqueue(centerPos);
        }

        // Spiral outward
        while (currentDistance < maxDistance)
        {
            currentDistance++;
            
            // Generate chunks in a cube shell at current distance
            for (int x = -currentDistance; x <= currentDistance; x++)
            {
                for (int y = -currentDistance; y <= currentDistance; y++)
                {
                    for (int z = -currentDistance; z <= currentDistance; z++)
                    {
                        // Only process chunks on the outer shell of the cube
                        if (Mathf.Abs(x) == currentDistance || 
                            Mathf.Abs(y) == currentDistance || 
                            Mathf.Abs(z) == currentDistance)
                        {
                            Vector3Int chunkPos = centerChunkPos + new Vector3Int(x, y, z);
                            
                            // Skip if chunk already exists
                            if (chunks.ContainsKey(chunkPos))
                                continue;

                            // Calculate distance from center
                            float distanceFromCenter = Vector3Int.Distance(chunkPos, centerChunkPos);

                            // Always generate chunks within safety radius
                            if (distanceFromCenter <= safetyRadius)
                            {
                                chunkLoadQueue.Enqueue(chunkPos);
                                continue;
                            }

                            // For chunks outside safety radius, only generate if they're in front of the player
                            Vector3Int relativePos = chunkPos - centerChunkPos;
                            float dotProduct = Vector3.Dot(new Vector3(relativePos.x, relativePos.y, relativePos.z), playerForward);

                            // Only generate chunks that are in front of the player (dot product > 0)
                            if (dotProduct > 0)
                            {
                                chunkLoadQueue.Enqueue(chunkPos);
                            }
                        }
                    }
                }
            }
        }
    }

    public void CreateChunk(Vector3Int chunkPos)
    {
        if (chunks.ContainsKey(chunkPos)) return;

        // Start asynchronous chunk generation
        StartCoroutine(CreateChunkAsync(chunkPos));
    }

    private System.Collections.IEnumerator CreateChunkAsync(Vector3Int chunkPos)
    {
        // Generate chunk data using the new pipeline (asynchronously)
        OptimizedVoxelStorage generatedVoxels = null;
        
        // Run chunk generation on a background thread
        bool generationComplete = false;
        System.Threading.Tasks.Task.Run(() =>
        {
            generatedVoxels = ChunkGenerationPipeline.GenerateChunk(
                chunkPos, chunkSize, chunkHeight, this
            );
            generationComplete = true;
        });

        // Wait for generation to complete
        while (!generationComplete)
        {
            yield return null;
        }

        // Create the chunk GameObject on the main thread
        GameObject chunkObj = new GameObject($"Chunk_{chunkPos.x}_{chunkPos.y}_{chunkPos.z}");
        chunkObj.transform.parent = transform;
        chunkObj.transform.position = new Vector3(chunkPos.x * chunkSize, chunkPos.y * chunkHeight, chunkPos.z * chunkSize);

        Chunk chunk = chunkObj.AddComponent<Chunk>();
        chunk.Initialize(chunkSize, chunkHeight, generatedVoxels);
        chunks[chunkPos] = chunk;
        
        // Trigger border light fix-up for this chunk and its neighbors
        StartCoroutine(FixupChunkBorderLighting(chunkPos, generatedVoxels));
    }
    
    private System.Collections.IEnumerator FixupChunkBorderLighting(Vector3Int chunkPos, OptimizedVoxelStorage chunkData)
    {
        // Wait a frame to ensure all chunks are properly registered
        yield return null;
        
        // Get neighbor snapshots for border light fix-up
        Dictionary<Vector3Int, OptimizedVoxelStorage> neighborSnapshots = GetNeighborSnapshots(chunkPos);
        
        // Run the fix-up on a background thread
        bool fixupComplete = false;
        System.Threading.Tasks.Task.Run(() =>
        {
            ChunkGenerationPipeline.FixupChunkBorderLighting(
                chunkPos, chunkData, chunkSize, chunkHeight, neighborSnapshots
            );
            fixupComplete = true;
        });
        
        // Wait for fix-up to complete
        while (!fixupComplete)
        {
            yield return null;
        }
        
        // Regenerate mesh with updated lighting
        if (chunks.TryGetValue(chunkPos, out Chunk chunk))
        {
            chunk.GenerateMesh();
        }
        
        // Notify neighbors to also fix up their borders
        NotifyNeighborsForBorderFixup(chunkPos);
    }
    
    private Dictionary<Vector3Int, OptimizedVoxelStorage> GetNeighborSnapshots(Vector3Int chunkPos)
    {
        Dictionary<Vector3Int, OptimizedVoxelStorage> neighborSnapshots = new Dictionary<Vector3Int, OptimizedVoxelStorage>();
        
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
            Vector3Int neighborPos = chunkPos + offset;
            if (chunks.TryGetValue(neighborPos, out Chunk neighborChunk))
            {
                // Clone the neighbor's voxel data to avoid modifying the original
                neighborSnapshots[offset] = neighborChunk.voxels.Clone();
            }
        }
        
        return neighborSnapshots;
    }
    
    private void NotifyNeighborsForBorderFixup(Vector3Int chunkPos)
    {
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
            Vector3Int neighborPos = chunkPos + offset;
            if (chunks.TryGetValue(neighborPos, out Chunk neighborChunk))
            {
                // Trigger border fix-up for the neighbor
                StartCoroutine(FixupNeighborBorderLighting(neighborPos, -offset));
            }
        }
    }
    
    private System.Collections.IEnumerator FixupNeighborBorderLighting(Vector3Int neighborPos, Vector3Int sourceOffset)
    {
        // Wait a frame to ensure the source chunk is fully processed
        yield return null;
        
        if (chunks.TryGetValue(neighborPos, out Chunk neighborChunk))
        {
            // Get neighbor snapshots for the neighbor chunk
            Dictionary<Vector3Int, OptimizedVoxelStorage> neighborSnapshots = GetNeighborSnapshots(neighborPos);
            
            // Run the fix-up on a background thread
            bool fixupComplete = false;
            System.Threading.Tasks.Task.Run(() =>
            {
                ChunkGenerationPipeline.FixupChunkBorderLighting(
                    neighborPos, neighborChunk.voxels, chunkSize, chunkHeight, neighborSnapshots
                );
                fixupComplete = true;
            });
            
            // Wait for fix-up to complete
            while (!fixupComplete)
            {
                yield return null;
            }
            
            // Regenerate mesh with updated lighting
            neighborChunk.GenerateMesh();
        }
    }

    private void UpdateAdjacentChunksLighting(Vector3Int chunkPos)
    {
        // No need to update lighting anymore
    }

    private void UnloadDistantChunks(Vector3Int centerChunkPos)
    {
        List<Vector3Int> chunksToUnload = new List<Vector3Int>();

        foreach (var chunk in chunks)
        {
            float distance = Vector3Int.Distance(chunk.Key, centerChunkPos);
            
            // Don't unload chunks within safety radius
            if (distance <= safetyRadius)
                continue;

            // For chunks outside safety radius, check if they're behind the player
            Vector3Int relativePos = chunk.Key - centerChunkPos;
            float dotProduct = Vector3.Dot(new Vector3(relativePos.x, relativePos.y, relativePos.z), player.forward);

            // Unload chunks that are behind the player and beyond render distance
            if (dotProduct < 0 && distance > renderDistance)
            {
                chunksToUnload.Add(chunk.Key);
            }
        }

        foreach (var chunkPos in chunksToUnload)
        {
            Destroy(chunks[chunkPos].gameObject);
            chunks.Remove(chunkPos);
        }
    }

    public Chunk GetChunkAt(Vector3Int position)
    {
        chunks.TryGetValue(position, out Chunk chunk);
        return chunk;
    }

    public bool HasChunkAt(Vector3Int position)
    {
        return chunks.ContainsKey(position);
    }

    public Voxel GetVoxelInWorld(Vector3Int worldPos)
    {
        Vector3Int chunkPos = GetChunkPosition(worldPos);
        Vector3Int localPos = GetLocalPosition(worldPos);

        if (chunks.TryGetValue(chunkPos, out Chunk chunk))
        {
            if (localPos.x >= 0 && localPos.x < chunk.chunkSize &&
                localPos.y >= 0 && localPos.y < chunk.chunkHeight &&
                localPos.z >= 0 && localPos.z < chunk.chunkSize)
            {
                return chunk.voxels[localPos.x, localPos.y, localPos.z];
            }
        }

        // Return a default voxel with full light
        return new Voxel
        {
            type = Voxel.VoxelType.Air,
            //light = 1f,
            transparency = 1f
        };
    }

    private Vector3Int GetLocalPosition(Vector3Int worldPos)
    {
        Vector3Int chunkPos = GetChunkPosition(worldPos);
        return new Vector3Int(
            worldPos.x - (chunkPos.x * chunkSize),
            worldPos.y - (chunkPos.y * chunkHeight),
            worldPos.z - (chunkPos.z * chunkSize)
        );
    }

    public void SetBlock(Vector3 worldPos, Voxel.VoxelType type)
    {
        Vector3Int chunkPos = GetChunkPosition(worldPos);
        Vector3Int localPos = GetLocalPosition(new Vector3Int(
            Mathf.FloorToInt(worldPos.x),
            Mathf.FloorToInt(worldPos.y),
            Mathf.FloorToInt(worldPos.z)
        ));

        if (chunks.TryGetValue(chunkPos, out Chunk chunk))
        {
            // Update the block
            chunk.voxels.SetVoxel(localPos.x, localPos.y, localPos.z, new Voxel(type, type != Voxel.VoxelType.Air));
            
            // Check if the block is on a chunk border
            bool isOnBorder = false;
            List<Vector3Int> adjacentChunks = new List<Vector3Int>();
            
            if (localPos.x == 0)
            {
                adjacentChunks.Add(chunkPos + new Vector3Int(-1, 0, 0));
                isOnBorder = true;
            }
            else if (localPos.x == chunkSize - 1)
            {
                adjacentChunks.Add(chunkPos + new Vector3Int(1, 0, 0));
                isOnBorder = true;
            }

            if (localPos.y == 0)
            {
                adjacentChunks.Add(chunkPos + new Vector3Int(0, -1, 0));
                isOnBorder = true;
            }
            else if (localPos.y == chunkHeight - 1)
            {
                adjacentChunks.Add(chunkPos + new Vector3Int(0, 1, 0));
                isOnBorder = true;
            }

            if (localPos.z == 0)
            {
                adjacentChunks.Add(chunkPos + new Vector3Int(0, 0, -1));
                isOnBorder = true;
            }
            else if (localPos.z == chunkSize - 1)
            {
                adjacentChunks.Add(chunkPos + new Vector3Int(0, 0, 1));
                isOnBorder = true;
            }
            
            // Update lighting for this chunk and adjacent chunks
            chunk.GenerateMesh();
            
            // If the block is on a border, trigger border light fix-up
            if (isOnBorder)
            {
                StartCoroutine(FixupChunkBorderLighting(chunkPos, chunk.voxels));
                
                // Also fix up adjacent chunks
                foreach (Vector3Int adjChunkPos in adjacentChunks)
                {
                    if (chunks.TryGetValue(adjChunkPos, out Chunk adjChunk))
                    {
                        adjChunk.GenerateMesh();
                        StartCoroutine(FixupChunkBorderLighting(adjChunkPos, adjChunk.voxels));
                    }
                }
            }
        }
    }
}
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using System.Threading.Tasks;

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
    public AnimationCurve continentalnessCurve;
    public int chunksPerFrame = 5; // Number of chunks to load per frame
    public bool useVerticalChunks = true;
    public int worldSize = 5; 
    public int chunkSize = 16;
    public int chunkHeight = 16;
    public float maxHeight = 0.2f;
    public float noiseFrequency = 50f;
    public float noiseAmplitude = 30;
    public Material VoxelMaterial;
    public int renderDistance = 5;
    public float[,] noiseArray;

    private Dictionary<Vector3Int, Chunk> chunks = new Dictionary<Vector3Int, Chunk>();
    private Queue<Vector3Int> chunkLoadQueue = new Queue<Vector3Int>();
    private Transform player;
    private Vector3Int lastPlayerChunkPos;
    public static World Instance { get; private set; }
    public string noiseSeedString;
    [HideInInspector] public int noiseSeed;

    private Camera mainCamera;
    private float cullingUpdateInterval = 0.1f; // Update culling every 0.1 seconds
    private float lastCullingUpdate;

    private class ChunkLoadRequest
    {
        public Vector3Int position;
        public float priority;
        public float distance;

        public ChunkLoadRequest(Vector3Int pos, float pri, float dist)
        {
            position = pos;
            priority = pri;
            distance = dist;
        }
    }

    private List<ChunkLoadRequest> prioritizedChunkQueue = new List<ChunkLoadRequest>();

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

        string noEmptySpacesNoiseSeed = noiseSeedString.Replace(" ", string.Empty);
        if (noEmptySpacesNoiseSeed == string.Empty)
        {
            noEmptySpacesNoiseSeed = Random.Range(0, 10000000).ToString();
            noiseSeedString = noEmptySpacesNoiseSeed;
        }
        noiseSeed = System.Convert.ToInt32(noEmptySpacesNoiseSeed);

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
                chunk.UpdateVisibility(mainCamera);
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

    private void LoadChunksAround(Vector3Int centerChunkPos)
    {
        prioritizedChunkQueue.Clear();
        Vector3 playerPos = player.position;
        Vector3 cameraForward = mainCamera.transform.forward;

        if (useVerticalChunks)
        {
            int maxDistance = renderDistance;
            for (int distance = 0; distance <= maxDistance; distance++)
            {
                for (int y = distance; y >= -distance; y--)
                {
                    for (int x = -distance; x <= distance; x++)
                    {
                        for (int z = -distance; z <= distance; z++)
                        {
                            if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y), Mathf.Abs(z)) == distance)
                            {
                                Vector3Int chunkPos = centerChunkPos + new Vector3Int(x, y, z);
                                if (!chunks.ContainsKey(chunkPos) && !chunkLoadQueue.Contains(chunkPos))
                                {
                                    // Calculate chunk center in world space
                                    Vector3 chunkCenter = new Vector3(
                                        chunkPos.x * chunkSize + chunkSize / 2f,
                                        chunkPos.y * chunkHeight + chunkHeight / 2f,
                                        chunkPos.z * chunkSize + chunkSize / 2f
                                    );

                                    // Calculate distance to player
                                    float distToPlayer = Vector3.Distance(playerPos, chunkCenter);

                                    // Calculate dot product with camera forward
                                    Vector3 dirToChunk = (chunkCenter - playerPos).normalized;
                                    float dotProduct = Vector3.Dot(cameraForward, dirToChunk);

                                    // Calculate priority
                                    float priority = 0;
                                    
                                    // Higher priority for chunks in front of the player
                                    priority += dotProduct * 2f;
                                    
                                    // Higher priority for closer chunks
                                    priority += 1f / (distToPlayer + 1f);
                                    
                                    // Higher priority for chunks at player's level
                                    float heightDiff = Mathf.Abs(chunkCenter.y - playerPos.y);
                                    priority += 1f / (heightDiff + 1f);

                                    prioritizedChunkQueue.Add(new ChunkLoadRequest(chunkPos, priority, distToPlayer));
                                }
                            }
                        }
                    }
                }
            }
        }
        else
        {
            int maxDistance = renderDistance;
            for (int distance = 0; distance <= maxDistance; distance++)
            {
                for (int x = -distance; x <= distance; x++)
                {
                    for (int z = -distance; z <= distance; z++)
                    {
                        if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) == distance)
                        {
                            Vector3Int chunkPos = centerChunkPos + new Vector3Int(x, 0, z);
                            if (!chunks.ContainsKey(chunkPos) && !chunkLoadQueue.Contains(chunkPos))
                            {
                                // Calculate chunk center in world space
                                Vector3 chunkCenter = new Vector3(
                                    chunkPos.x * chunkSize + chunkSize / 2f,
                                    chunkPos.y * chunkHeight + chunkHeight / 2f,
                                    chunkPos.z * chunkSize + chunkSize / 2f
                                );

                                // Calculate distance to player
                                float distToPlayer = Vector3.Distance(playerPos, chunkCenter);

                                // Calculate dot product with camera forward
                                Vector3 dirToChunk = (chunkCenter - playerPos).normalized;
                                float dotProduct = Vector3.Dot(cameraForward, dirToChunk);

                                // Calculate priority
                                float priority = 0;
                                
                                // Higher priority for chunks in front of the player
                                priority += dotProduct * 2f;
                                
                                // Higher priority for closer chunks
                                priority += 1f / (distToPlayer + 1f);

                                prioritizedChunkQueue.Add(new ChunkLoadRequest(chunkPos, priority, distToPlayer));
                            }
                        }
                    }
                }
            }
        }

        // Sort chunks by priority
        prioritizedChunkQueue.Sort((a, b) => b.priority.CompareTo(a.priority));

        // Add sorted chunks to the load queue
        foreach (var request in prioritizedChunkQueue)
        {
            chunkLoadQueue.Enqueue(request.position);
        }
    }

    public void CreateChunk(Vector3Int chunkPos)
    {
        if (chunks.ContainsKey(chunkPos)) return;

        GameObject chunkObj = new GameObject($"Chunk_{chunkPos.x}_{chunkPos.y}_{chunkPos.z}");
        chunkObj.transform.parent = transform;
        chunkObj.transform.position = new Vector3(chunkPos.x * chunkSize, chunkPos.y * chunkHeight, chunkPos.z * chunkSize);

        Chunk chunk = chunkObj.AddComponent<Chunk>();
        chunk.Initialize(chunkSize, chunkHeight, continentalnessCurve);
        chunks[chunkPos] = chunk;
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
            if (Vector3Int.Distance(chunk.Key, centerChunkPos) > renderDistance)
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
            light = 1f,
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
            
            // Regenerate the chunk's mesh
            chunk.GenerateMesh();

            // Update adjacent chunks if the block is on a chunk border
            if (localPos.x == 0)
            {
                Vector3Int adjChunkPos = chunkPos + new Vector3Int(-1, 0, 0);
                if (chunks.TryGetValue(adjChunkPos, out Chunk adjChunk))
                {
                    adjChunk.GenerateMesh();
                }
            }
            else if (localPos.x == chunkSize - 1)
            {
                Vector3Int adjChunkPos = chunkPos + new Vector3Int(1, 0, 0);
                if (chunks.TryGetValue(adjChunkPos, out Chunk adjChunk))
                {
                    adjChunk.GenerateMesh();
                }
            }

            if (localPos.y == 0)
            {
                Vector3Int adjChunkPos = chunkPos + new Vector3Int(0, -1, 0);
                if (chunks.TryGetValue(adjChunkPos, out Chunk adjChunk))
                {
                    adjChunk.GenerateMesh();
                }
            }
            else if (localPos.y == chunkHeight - 1)
            {
                Vector3Int adjChunkPos = chunkPos + new Vector3Int(0, 1, 0);
                if (chunks.TryGetValue(adjChunkPos, out Chunk adjChunk))
                {
                    adjChunk.GenerateMesh();
                }
            }

            if (localPos.z == 0)
            {
                Vector3Int adjChunkPos = chunkPos + new Vector3Int(0, 0, -1);
                if (chunks.TryGetValue(adjChunkPos, out Chunk adjChunk))
                {
                    adjChunk.GenerateMesh();
                }
            }
            else if (localPos.z == chunkSize - 1)
            {
                Vector3Int adjChunkPos = chunkPos + new Vector3Int(0, 0, 1);
                if (chunks.TryGetValue(adjChunkPos, out Chunk adjChunk))
                {
                    adjChunk.GenerateMesh();
                }
            }
        }
    }
}
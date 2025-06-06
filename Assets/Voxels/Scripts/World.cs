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

        // Spiral generation from center outward
        int maxDistance = renderDistance;
        int currentDistance = 0;

        // Start with the center chunk
        Vector3Int centerPos = centerChunkPos;
        if (!chunks.ContainsKey(centerPos) && IsChunkPotentiallyVisible(centerPos))
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

                            // Skip if chunk is not potentially visible
                            if (!IsChunkPotentiallyVisible(chunkPos))
                                continue;

                            chunkLoadQueue.Enqueue(chunkPos);
                        }
                    }
                }
            }
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
            
            // Update lighting for this chunk and adjacent chunks
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
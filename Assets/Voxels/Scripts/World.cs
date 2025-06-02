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
        if (useVerticalChunks)
        {
            // Generate chunks in a spiral pattern from the center, but vertically from top to bottom
            int maxDistance = renderDistance;
            for (int distance = 0; distance <= maxDistance; distance++)
            {
                // Generate chunks at this distance in a spiral
                for (int y = distance; y >= -distance; y--) // Changed to go from top to bottom
                {
                    for (int x = -distance; x <= distance; x++)
                    {
                        for (int z = -distance; z <= distance; z++)
                        {
                            // Only process chunks at the current distance
                            if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y), Mathf.Abs(z)) == distance)
                            {
                                Vector3Int chunkPos = centerChunkPos + new Vector3Int(x, y, z);
                                if (!chunks.ContainsKey(chunkPos) && !chunkLoadQueue.Contains(chunkPos))
                                {
                                    chunkLoadQueue.Enqueue(chunkPos);
                                }
                            }
                        }
                    }
                }
            }
        }
        else
        {
            // For non-vertical chunks, use a 2D spiral
            int maxDistance = renderDistance;
            for (int distance = 0; distance <= maxDistance; distance++)
            {
                for (int x = -distance; x <= distance; x++)
                {
                    for (int z = -distance; z <= distance; z++)
                    {
                        // Only process chunks at the current distance
                        if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) == distance)
                        {
                            Vector3Int chunkPos = centerChunkPos + new Vector3Int(x, 0, z);
                            if (!chunks.ContainsKey(chunkPos) && !chunkLoadQueue.Contains(chunkPos))
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
}
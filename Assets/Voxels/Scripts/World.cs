using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using System.Threading.Tasks;

public class World : MonoBehaviour
{
    [Header("Lighting")]
    [Range(0f, 1f)]
    public float globalLightLevel;
    public Color dayColor;
    public Color nightColor;
    public static float minLightLevel = 0.1f;
    public static float maxLightLevel = 0.9f;
    public static float lightFalloff = 0.08f;

    [Header("World")]
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
    public int noiseSeed;

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
        player = FindObjectOfType<PlayerController>().transform;
        lastPlayerChunkPos = GetChunkPosition(player.position);
        LoadChunksAround(lastPlayerChunkPos);
        Shader.SetGlobalFloat("minGlobalLightLevel", minLightLevel);
        Shader.SetGlobalFloat("maxGlobalLightLevel", maxLightLevel);
    }

    async void Update()
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
                await CreateChunk(chunkLoadQueue.Dequeue());
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
            for (int y = -renderDistance; y <= renderDistance; y++)
            {
                for (int x = -renderDistance; x <= renderDistance; x++)
                {
                    for (int z = -renderDistance; z <= renderDistance; z++)
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
        else
        {
            for (int x = -renderDistance; x <= renderDistance; x++)
            {
                for (int z = -renderDistance; z <= renderDistance; z++)
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

    private async Task CreateChunk(Vector3Int chunkPos)
    {
        //Stopwatch sw = new();
        //sw.Start();

        GameObject chunkObject = new GameObject($"Chunk {chunkPos}");
        chunkObject.transform.position = new Vector3(chunkPos.x * chunkSize, useVerticalChunks ? chunkPos.y * chunkHeight : 0, chunkPos.z * chunkSize);
        chunkObject.transform.parent = transform;

        Chunk newChunk = chunkObject.AddComponent<Chunk>();
        await newChunk.Initialize(chunkSize, chunkHeight);

        chunks[chunkPos] = newChunk;

        //sw.Stop();
        //UnityEngine.Debug.Log($"Chunk creation at position {chunkPos} took {sw.ElapsedMilliseconds} milliseconds.");
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
}
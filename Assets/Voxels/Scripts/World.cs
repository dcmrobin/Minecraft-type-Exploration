using System.Collections.Generic;
using UnityEngine;

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
    public int worldSize = 5; 
    public int chunkSize = 16;
    public int chunkHeight = 16;
    public float maxHeight = 0.2f;
    public float noiseScale = 0.015f;
    public AnimationCurve mountainsCurve;
    public AnimationCurve mountainBiomeCurve;
    public Material VoxelMaterial;
    public int renderDistance = 5; // The maximum distance from the player to keep chunks
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

    void Update()
    {
        Shader.SetGlobalFloat("GlobalLightLevel", globalLightLevel);
        player.GetComponentInChildren<Camera>().backgroundColor = Color.Lerp(nightColor, dayColor, globalLightLevel);

        Vector3Int currentPlayerChunkPos = GetChunkPosition(player.position);

        if (currentPlayerChunkPos != lastPlayerChunkPos)
        {
            LoadChunksAround(currentPlayerChunkPos);
            UnloadDistantChunks(currentPlayerChunkPos);
            lastPlayerChunkPos = currentPlayerChunkPos;
        }

        if (chunkLoadQueue.Count > 0)
        {
            CreateChunk(chunkLoadQueue.Dequeue());
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

    private void CreateChunk(Vector3Int chunkPos)
    {
        GameObject chunkObject = new GameObject($"Chunk {chunkPos}");
        chunkObject.transform.position = new Vector3(chunkPos.x * chunkSize, 0, chunkPos.z * chunkSize);
        chunkObject.transform.parent = transform;

        Chunk newChunk = chunkObject.AddComponent<Chunk>();
        newChunk.Initialize(chunkSize, chunkHeight, mountainsCurve, mountainBiomeCurve);

        chunks[chunkPos] = newChunk;
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
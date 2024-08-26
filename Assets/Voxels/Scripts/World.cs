using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;

public class World : MonoBehaviour
{
    public int worldSize = 5; 
    public int chunkSize = 16;
    public int chunkHeight = 16;
    public int noiseSeed = 1234;
    public float maxHeight = 0.2f;
    public float noiseScale = 0.015f;
    public float[,] noiseArray;
    public AnimationCurve mountainsCurve;
    public AnimationCurve mountainBiomeCurve;

    private Dictionary<Vector3, Chunk> chunks;
    public static World Instance { get; private set; }

    public Material VoxelMaterial;
    PlayerController playerController;
    Vector3 playerPosition;
    private Vector3Int lastPlayerChunkCoordinates;
    public int loadRadius = 5;
    public int unloadRadius = 7;
    private int chunksMovedCount = 0;
    public int chunkUpdateThreshold = 5;
    private bool JustStarted = true;
    private readonly Queue<Vector3> chunkLoadQueue = new();
    private readonly Queue<Vector3> chunkUnloadQueue = new();

    private const int chunksPerFrame = 1;
    private const int unloadInterval = 2;
    private const int loadInterval = 1;
    private int frameCounter = 0;
    private int unloadFrameCounter = 0;

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
        noiseArray = GlobalNoise.GetNoise();
    }

    void Start()
    {
        noiseSeed = Random.Range(1, 1000000);
        playerController = FindObjectOfType<PlayerController>();
        chunks = new Dictionary<Vector3, Chunk>();
        GlobalNoise.SetSeed();
    }

    async void Update()
    {
        playerPosition = playerController.transform.position;
        UpdateChunks(playerPosition);
        await ProcessChunkQueues();
    }

    void UpdateChunks(Vector3 playerPosition)
    {
        Vector3Int playerChunkCoordinates = new(
            Mathf.FloorToInt(playerPosition.x / chunkSize),
            Mathf.FloorToInt(playerPosition.y / chunkHeight),
            Mathf.FloorToInt(playerPosition.z / chunkSize)
        );

        if (!playerChunkCoordinates.Equals(lastPlayerChunkCoordinates))
        {
            if (chunksMovedCount >= chunkUpdateThreshold || JustStarted)
            {
                LoadChunksAround(playerChunkCoordinates);
                UnloadDistantChunks(playerChunkCoordinates);
                JustStarted = false;
                chunksMovedCount = 0;
            }

            lastPlayerChunkCoordinates = playerChunkCoordinates;
            chunksMovedCount++;
        }
    }

    void LoadChunksAround(Vector3Int centerChunkCoordinates)
    {
        for (int x = -loadRadius; x <= loadRadius; x++)
        {
            for (int y = -loadRadius; y <= loadRadius; y++)
            {
                for (int z = -loadRadius; z <= loadRadius; z++)
                {
                    Vector3Int chunkCoordinates = new(
                        centerChunkCoordinates.x + x,
                        centerChunkCoordinates.y + y,
                        centerChunkCoordinates.z + z
                    );

                    Vector3 chunkPosition = new(
                        chunkCoordinates.x * chunkSize,
                        chunkCoordinates.y * chunkHeight,
                        chunkCoordinates.z * chunkSize
                    );

                    if (!chunks.ContainsKey(chunkPosition))
                    {
                        chunkLoadQueue.Enqueue(chunkPosition);
                    }
                }
            }
        }
    }

    private async Task ProcessChunkQueues()
    {
        frameCounter++;
        unloadFrameCounter++;

        // Load chunks on the main thread, spread over multiple frames
        if (frameCounter % loadInterval == 0 && chunkLoadQueue.Count > 0)
        {
            for (int i = 0; i < chunksPerFrame && chunkLoadQueue.Count > 0; i++)
            {
                Vector3 chunkPosition = chunkLoadQueue.Dequeue();
                await LoadChunk(chunkPosition);
            }
        }

        // Unload chunks on the main thread, spread over multiple frames
        if (unloadFrameCounter % unloadInterval == 0 && chunkUnloadQueue.Count > 0)
        {
            for (int i = 0; i < chunksPerFrame && chunkUnloadQueue.Count > 0; i++)
            {
                Vector3 chunkPosition = chunkUnloadQueue.Dequeue();
                UnloadChunk(chunkPosition);
            }
        }
    }

    private async Task LoadChunk(Vector3 chunkPosition)
    {
        if (!chunks.ContainsKey(chunkPosition))
        {
            // Instantiate new chunk directly
            GameObject chunkObject = new GameObject("Chunk");
            Chunk newChunk = chunkObject.AddComponent<Chunk>();
            newChunk.transform.position = chunkPosition;
            newChunk.transform.parent = this.transform;

            // Initialize the chunk
            await newChunk.Initialize(chunkSize, chunkHeight, mountainsCurve, mountainBiomeCurve);

            // Add the chunk to the dictionary
            chunks.Add(chunkPosition, newChunk);
        }
    }

    void UnloadChunk(Vector3 chunkPosition)
    {
        if (chunks.TryGetValue(chunkPosition, out Chunk chunkToUnload))
        {
            Destroy(chunkToUnload.gameObject); // Destroy the chunk game object
            chunks.Remove(chunkPosition);
        }
    }

    void UnloadDistantChunks(Vector3Int centerChunkCoordinates)
    {
        foreach (var chunk in chunks)
        {
            Vector3Int chunkCoord = new(
                Mathf.FloorToInt(chunk.Key.x / chunkSize),
                Mathf.FloorToInt(chunk.Key.y / chunkHeight),
                Mathf.FloorToInt(chunk.Key.z / chunkSize)
            );

            if (Vector3Int.Distance(chunkCoord, centerChunkCoordinates) > unloadRadius)
            {
                chunkUnloadQueue.Enqueue(chunk.Key);
            }
        }
    }

    public Chunk GetChunkAt(Vector3 globalPosition)
    {
        Vector3Int chunkCoordinates = new(
            Mathf.FloorToInt(globalPosition.x / chunkSize) * chunkSize,
            Mathf.FloorToInt(globalPosition.y / chunkHeight) * chunkHeight,
            Mathf.FloorToInt(globalPosition.z / chunkSize) * chunkSize
        );

        chunks.TryGetValue(chunkCoordinates, out Chunk chunk);
        return chunk;
    }
}

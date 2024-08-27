using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;
using System.Collections;

public class World : MonoBehaviour
{
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

    public static World Instance { get; private set; }
    public int noiseSeed;

    private Dictionary<Vector3, Chunk> chunks = new Dictionary<Vector3, Chunk>();
    private PlayerController playerController;
    private Vector3 playerPosition;
    private Vector3Int lastPlayerChunkCoordinates;

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
        playerController = FindObjectOfType<PlayerController>();
        // Initialize chunk management
        LoadInitialChunks();
    }

    void Update()
    {
        playerPosition = playerController.transform.position;
        Vector3Int playerChunkCoordinates = GetChunkCoordinates(playerPosition);

        // Generate and unload chunks based on the renderDistance
        UpdateChunks(playerChunkCoordinates);
    }

    private void LoadInitialChunks()
    {
        Vector3Int playerChunkCoordinates = GetChunkCoordinates(playerPosition);
        LoadChunksAround(playerChunkCoordinates);
    }

    private void UpdateChunks(Vector3Int playerChunkCoordinates)
    {
        if (!playerChunkCoordinates.Equals(lastPlayerChunkCoordinates))
        {
            LoadChunksAround(playerChunkCoordinates);
            UnloadDistantChunks(playerChunkCoordinates);
            lastPlayerChunkCoordinates = playerChunkCoordinates;
        }
    }

    private void LoadChunksAround(Vector3Int centerChunkCoordinates)
    {
        for (int x = -renderDistance; x <= renderDistance; x++)
        {
            for (int y = -renderDistance; y <= renderDistance; y++)
            {
                for (int z = -renderDistance; z <= renderDistance; z++)
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
                        // Instantiate and initialize new chunk
                        GameObject chunkObject = new GameObject("Chunk");
                        Chunk newChunk = chunkObject.AddComponent<Chunk>();
                        newChunk.transform.position = chunkPosition;
                        newChunk.transform.parent = this.transform;
                        chunks.Add(chunkPosition, newChunk);

                        // Initialize the chunk asynchronously
                        StartCoroutine(InitializeChunkAsync(newChunk, chunkPosition));
                    }
                }
            }
        }
    }

    private IEnumerator InitializeChunkAsync(Chunk chunk, Vector3 chunkPosition)
    {
        yield return chunk.Initialize(chunkSize, chunkHeight, mountainsCurve, mountainBiomeCurve);
    }

    private void UnloadDistantChunks(Vector3Int centerChunkCoordinates)
    {
        List<Vector3> chunksToUnload = new List<Vector3>();

        foreach (var chunk in chunks)
        {
            Vector3Int chunkCoord = GetChunkCoordinates(chunk.Key);
            if (Vector3Int.Distance(chunkCoord, centerChunkCoordinates) > renderDistance)
            {
                chunksToUnload.Add(chunk.Key);
            }
        }

        foreach (var chunkPosition in chunksToUnload)
        {
            UnloadChunk(chunkPosition);
        }
    }

    private void UnloadChunk(Vector3 chunkPosition)
    {
        if (chunks.TryGetValue(chunkPosition, out Chunk chunkToUnload))
        {
            Destroy(chunkToUnload.gameObject); // Destroy the chunk game object
            chunks.Remove(chunkPosition);
        }
    }

    private Vector3Int GetChunkCoordinates(Vector3 position)
    {
        return new Vector3Int(
            Mathf.FloorToInt(position.x / chunkSize),
            Mathf.FloorToInt(position.y / chunkHeight),
            Mathf.FloorToInt(position.z / chunkSize)
        );
    }
}

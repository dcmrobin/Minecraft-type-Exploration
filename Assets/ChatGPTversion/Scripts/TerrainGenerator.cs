using System.Collections.Generic;
using UnityEngine;

public class TerrainGenerator : MonoBehaviour
{
    public GameObject chunkPrefab;  // Prefab of the Chunk
    public int chunkSize = 16;      // Size of each chunk
    public int viewDistance = 4;    // Number of chunks to load in each direction

    private Transform playerTransform;
    private Vector3 lastPlayerPosition;
    private float chunkSizeF;
    
    private Dictionary<Vector3Int, GameObject> chunks = new Dictionary<Vector3Int, GameObject>();

    void Start()
    {
        playerTransform = Camera.main.transform;
        lastPlayerPosition = playerTransform.position;
        chunkSizeF = (float)chunkSize;

        GenerateChunks();
    }

    void Update()
    {
        Vector3 playerPosition = playerTransform.position;
        if (Vector3.Distance(playerPosition, lastPlayerPosition) >= chunkSizeF)
        {
            lastPlayerPosition = playerPosition;
            GenerateChunks();
        }
    }

    void GenerateChunks()
    {
        Vector3Int playerChunkCoord = PositionToChunkCoord(playerTransform.position);

        // Unload chunks that are too far from the player
        List<Vector3Int> chunksToRemove = new List<Vector3Int>();
        foreach (var chunk in chunks)
        {
            if (Vector3Int.Distance(chunk.Key, playerChunkCoord) > viewDistance)
            {
                Destroy(chunk.Value);
                chunksToRemove.Add(chunk.Key);
            }
        }
        foreach (var coord in chunksToRemove)
        {
            chunks.Remove(coord);
        }

        // Load new chunks around the player
        for (int x = -viewDistance; x <= viewDistance; x++)
        {
            for (int z = -viewDistance; z <= viewDistance; z++)
            {
                Vector3Int coord = playerChunkCoord + new Vector3Int(x, 0, z);
                coord.y = 0;
                if (!chunks.ContainsKey(coord))
                {
                    GameObject chunk = Instantiate(chunkPrefab, ChunkCoordToPosition(coord), Quaternion.identity);
                    chunk.transform.parent = transform;
                    chunks[coord] = chunk;
                }
            }
        }
    }

    Vector3Int PositionToChunkCoord(Vector3 position)
    {
        int x = Mathf.FloorToInt(position.x / chunkSize);
        int y = Mathf.FloorToInt(position.y / chunkSize);
        int z = Mathf.FloorToInt(position.z / chunkSize);
        return new Vector3Int(x, y, z);
    }

    Vector3 ChunkCoordToPosition(Vector3Int coord)
    {
        return new Vector3(coord.x * chunkSize, coord.y * chunkSize, coord.z * chunkSize);
    }
}

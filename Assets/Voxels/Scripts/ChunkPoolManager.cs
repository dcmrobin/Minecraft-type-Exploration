using System.Collections.Generic;
using UnityEngine;

public class ChunkPoolManager : MonoBehaviour {
    public static ChunkPoolManager Instance { get; private set; }
    
    private readonly Queue<Chunk> chunkPool = new();
    public int initialPoolSize = 10; // Number of chunks to add to the pool at start


    void Awake() {
        Instance = this;
    }

    public void PopulateInitialPool() {
        for (int i = 0; i < initialPoolSize; i++) {
            Chunk newChunk = InstantiateNewChunk();
            chunkPool.Enqueue(newChunk);
        }
    }

    public Chunk GetChunk() {
        Chunk chunk;
        if (chunkPool.Count > 0) {
            chunk = chunkPool.Dequeue();
        } else {
            chunk = InstantiateNewChunk();
        }
        return chunk;
    }

    public void ReturnChunk(Chunk chunk) {
        chunk.ResetChunk();
        chunk.gameObject.SetActive(false);
        chunkPool.Enqueue(chunk);
    }

    private Chunk InstantiateNewChunk() {
        GameObject chunkObject = new("Chunk");
        Chunk newChunk = chunkObject.AddComponent<Chunk>();
        return newChunk;
    }

   
}
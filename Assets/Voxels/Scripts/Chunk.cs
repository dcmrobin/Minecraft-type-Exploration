using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using System.Diagnostics;
using VoxelEngine;

public class Chunk : MonoBehaviour
{
    private Voxel[,,] voxels;
    private int chunkSize = 16;
    private int chunkHeight = 16;
    private float noiseFrequency;
    private float noiseAmplitude;
    private float lightFalloff;
    private readonly List<Vector3> vertices = new();
    private readonly List<int> triangles = new();
    private readonly List<Vector2> uvs = new();
    List<Color> colors = new();
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;

    public Vector3 pos;

    private void Start() {
        pos = transform.position;
    }

    private void GenerateVoxelData(Vector3 chunkWorldPosition)
    {
        //Stopwatch sw = new();
        //sw.Start();

        GenerateJob generateJob = new()
        {
            chunkHeight = chunkHeight,
            chunkSize = chunkSize,
            frequency = noiseFrequency,
            amplitude = noiseAmplitude,
            lightFalloff = lightFalloff,
            chunkWorldPosition = chunkWorldPosition,
            voxels = new NativeArray<Voxel>(voxels.Length, Allocator.TempJob),
            litVoxels = new NativeQueue<Vector3Int>(Allocator.TempJob)
        };

        JobHandle handle = generateJob.Schedule();
        handle.Complete();

        for (int i = 0; i < generateJob.voxels.Length; i++)
        {
            int x = i % chunkSize;
            int y = (i / chunkSize) % chunkHeight;
            int z = i / (chunkSize * chunkHeight);
            voxels[x, y, z] = generateJob.voxels[i];
        }

        generateJob.voxels.Dispose();
        generateJob.litVoxels.Dispose();

        //sw.Stop();
        //UnityEngine.Debug.Log($"Generating voxel data for {name} took {sw.ElapsedMilliseconds} milliseconds");
    }

    public void GenerateMesh()
    {
        //Stopwatch sw = new();
        //sw.Start();
        for (int y = 0; y < chunkHeight; y++)
        {
            for (int x = 0; x < chunkSize; x++)
            {
                for (int z = 0; z < chunkSize; z++)
                {
                    if (voxels[x, y, z].type == Voxel.VoxelType.Air)
                    {
                        continue;
                    }
                    else
                    {
                        ProcessVoxel(x, y, z);
                    }
                }
            }
        }

        if (vertices.Count > 0) {
            Mesh mesh = new()
            {
                vertices = vertices.ToArray(),
                triangles = triangles.ToArray(),
                uv = uvs.ToArray(),
                colors = colors.ToArray()
            };

            mesh.RecalculateNormals(); // Important for lighting

            meshFilter.mesh = mesh;
            meshCollider.sharedMesh = mesh;

            // Apply a material or texture if needed
            meshRenderer.material = World.Instance.VoxelMaterial;
        }
        //sw.Stop();
        //UnityEngine.Debug.Log($"Mesh generation for {name} took {sw.ElapsedMilliseconds} milliseconds");
    }

    public void Initialize(int size, int height)
    {
        //Stopwatch sw = new();
        //sw.Start();
        this.chunkSize = size;
        this.chunkHeight = height;
        this.noiseFrequency = World.Instance.noiseFrequency;
        this.noiseAmplitude = World.Instance.noiseAmplitude;
        this.lightFalloff = World.lightFalloff;
        voxels = new Voxel[size, height, size];

        GenerateVoxelData(transform.position);

        meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null) { meshFilter = gameObject.AddComponent<MeshFilter>(); }

        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null) { meshRenderer = gameObject.AddComponent<MeshRenderer>(); }

        meshCollider = GetComponent<MeshCollider>();
        if (meshCollider == null) { meshCollider = gameObject.AddComponent<MeshCollider>(); }

        GenerateMesh(); // Call after ensuring all necessary components and data are set
        //sw.Stop();
        //UnityEngine.Debug.Log($"Initialization for {name} took {sw.ElapsedMilliseconds} milliseconds");
    }

    private void ProcessVoxel(int x, int y, int z)
    {
        if (voxels == null || x < 0 || x >= voxels.GetLength(0) || 
            y < 0 || y >= voxels.GetLength(1) || z < 0 || z >= voxels.GetLength(2))
        {
            return; // Skip processing if the array is not initialized or indices are out of bounds
        }

        Voxel voxel = voxels[x, y, z];
        if (voxel.isActive)
        {
            bool[] facesVisible = new bool[6];
            facesVisible[0] = IsVoxelHiddenInChunk(x, y + 1, z); // Top
            facesVisible[1] = IsVoxelHiddenInChunk(x, y - 1, z); // Bottom
            facesVisible[2] = IsVoxelHiddenInChunk(x - 1, y, z); // Left
            facesVisible[3] = IsVoxelHiddenInChunk(x + 1, y, z); // Right
            facesVisible[4] = IsVoxelHiddenInChunk(x, y, z + 1); // Front
            facesVisible[5] = IsVoxelHiddenInChunk(x, y, z - 1); // Back

            for (int i = 0; i < facesVisible.Length; i++)
            {
                if (facesVisible[i])
                {
                    Voxel neighborVoxel = GetVoxelSafe(x, y, z);
                    voxel.AddFaceData(vertices, triangles, uvs, colors, i, neighborVoxel);
                }
            }
        }
    }

    private bool IsVoxelHiddenInChunk(int x, int y, int z)
    {
        if (x < 0 || x >= chunkSize || y < 0 || y >= chunkHeight || z < 0 || z >= chunkSize)
            return true; // Face is at the boundary of the chunk
        return !voxels[x, y, z].isActive;
    }

    public bool IsVoxelActiveAt(Vector3 localPosition)
    {
        // Round the local position to get the nearest voxel index
        int x = Mathf.RoundToInt(localPosition.x);
        int y = Mathf.RoundToInt(localPosition.y);
        int z = Mathf.RoundToInt(localPosition.z);

        // Check if the indices are within the bounds of the voxel array
        if (x >= 0 && x < chunkSize && y >= 0 && y < chunkHeight && z >= 0 && z < chunkSize)
        {
            // Return the active state of the voxel at these indices
            return voxels[x, y, z].isActive;
        }

        // If out of bounds, consider the voxel inactive
        return false;
    }

    private Voxel GetVoxelSafe(int x, int y, int z)
    {
        if (x < 0 || x >= chunkSize || y < 0 || y >= chunkHeight || z < 0 || z >= chunkSize)
        {
            //Debug.Log("Voxel safe out of bounds");
            return new Voxel(); // Default or inactive voxel
        }
        //Debug.Log("Voxel safe is in bounds");
        return voxels[x, y, z];
    }

    public void ResetChunk() {
        // Clear voxel data
        voxels = new Voxel[chunkSize, chunkHeight, chunkSize];

        // Clear mesh data
        if (meshFilter != null && meshFilter.sharedMesh != null) {
            meshFilter.sharedMesh.Clear();
            vertices.Clear();
            triangles.Clear();
            uvs.Clear();
            colors.Clear();
        }
    }
}

[BurstCompile]
public struct GenerateJob : IJob
{
    public int chunkHeight;
    public int chunkSize;
    public float frequency;
    public float amplitude;
    public float lightFalloff;
    public Vector3 chunkWorldPosition;
    public NativeArray<Voxel> voxels;
    public NativeQueue<Vector3Int> litVoxels;

    public void Execute()
    {
        // Voxel position calculation
        for (int index = 0; index < voxels.Length; index++)
        {
            int x = index % chunkSize;
            int y = (index / chunkSize) % chunkHeight;
            int z = index / (chunkSize * chunkHeight);
            int voxelIndex = x + y * chunkSize + z * chunkSize * chunkHeight;

            Vector3 voxelChunkPos = new Vector3(x, y, z);
            float calculatedHeight = Mathf.PerlinNoise((chunkWorldPosition.x + x) / frequency, (chunkWorldPosition.z + z) / frequency) * amplitude;
    
            Voxel.VoxelType type = Voxel.DetermineVoxelType(voxelChunkPos, calculatedHeight);
            voxels[voxelIndex] = new Voxel(new Vector3(x, y, z), type, type != Voxel.VoxelType.Air, 0);
        }

        // Voxel light calculation
        for (int x = 0; x < chunkSize; x++)
        {
            for (int z = 0; z < chunkSize; z++)
            {
                float lightRay = 1f;

                // Process from the top of the chunk to the bottom
                for (int y = chunkHeight - 1; y >= 0; y--)
                {
                    int voxelIndex = x + y * chunkSize + z * chunkSize * chunkHeight;
                    Voxel thisVoxel = voxels[voxelIndex];

                    if (thisVoxel.type != Voxel.VoxelType.Air && thisVoxel.transparency < lightRay)
                    {
                        lightRay = thisVoxel.transparency;
                    }

                    thisVoxel.globalLightPercentage = lightRay;
                    voxels[voxelIndex] = thisVoxel;

                    if (lightRay > lightFalloff)
                    {
                        // Add voxel to the flood-fill queue if it can propagate light
                        litVoxels.Enqueue(new Vector3Int(x, y, z));
                    }
                }
            }
        }

        // Flood-fill the light across neighbors
        while (litVoxels.Count > 0)
        {
            Vector3Int v = litVoxels.Dequeue();
            int voxelIndex = v.x + v.y * chunkSize + v.z * chunkSize * chunkHeight;
            Voxel sourceVoxel = voxels[voxelIndex];

            for (int p = 0; p < 6; p++)
            {
                Vector3Int neighbor = GetNeighbor(v, p);
                int neighborVoxelIndex = neighbor.x + neighbor.y * chunkSize + neighbor.z * chunkSize * chunkHeight;

                if (IsInsideChunk(neighbor, chunkSize, chunkHeight))
                {
                    Voxel neighborVoxel = voxels[neighborVoxelIndex];

                    // Check if the neighbor can be lit by this voxel
                    if (neighborVoxel.globalLightPercentage < sourceVoxel.globalLightPercentage - lightFalloff)
                    {
                        neighborVoxel.globalLightPercentage = sourceVoxel.globalLightPercentage - lightFalloff;
                        voxels[neighborVoxelIndex] = neighborVoxel;

                        if (neighborVoxel.globalLightPercentage > lightFalloff)
                        {
                            litVoxels.Enqueue(neighbor);
                        }
                    }
                }
            }
        }
    }

    private readonly bool IsInsideChunk(Vector3Int position, int chunkSize, int chunkHeight)
    {
        return position.x >= 0 && position.x < chunkSize &&
               position.y >= 0 && position.y < chunkHeight &&
               position.z >= 0 && position.z < chunkSize;
    }

    private readonly Vector3Int GetNeighbor(Vector3Int current, int direction)
    {
        return direction switch
        {
            0 => new Vector3Int(current.x, current.y + 1, current.z), // Top
            1 => new Vector3Int(current.x, current.y - 1, current.z), // Bottom
            2 => new Vector3Int(current.x - 1, current.y, current.z), // Left
            3 => new Vector3Int(current.x + 1, current.y, current.z), // Right
            4 => new Vector3Int(current.x, current.y, current.z + 1), // Front
            5 => new Vector3Int(current.x, current.y, current.z - 1), // Back
            _ => current
        };
    }
}
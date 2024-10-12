using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using System.Threading.Tasks;
using System.Diagnostics;
using VoxelEngine;

public class Chunk : MonoBehaviour
{
    //public Chunk[] neighbors = new Chunk[6];

    public Voxel[,,] voxels;
    private int chunkSize = 16;
    private int chunkHeight = 16;
    private float noiseFrequency;
    private float noiseAmplitude;
    //private float lightFalloff;
    private readonly List<Vector3> vertices = new();
    private readonly List<int> triangles = new();
    private readonly List<Vector2> uvs = new();
    List<Color> colors = new();
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;

    public Vector3 pos;

    private void Awake() {
        pos = transform.position;
    }

    //private void Update() {
    //    if (neighbors[0] == null || neighbors[1] == null || neighbors[2] == null || neighbors[3] == null || neighbors[4] == null || neighbors[5] == null)
    //    {
    //        CheckForNeighbors();
    //    }
    //}

    //public void CheckForNeighbors()
    //{
    //    Vector3Int[] neighborDirections = new Vector3Int[]
    //    {
    //        new Vector3Int(0, 1, 0),  // Top
    //        new Vector3Int(0, -1, 0), // Bottom
    //        new Vector3Int(-1, 0, 0), // Left
    //        new Vector3Int(1, 0, 0),  // Right
    //        new Vector3Int(0, 0, 1),  // Front
    //        new Vector3Int(0, 0, -1)  // Back
    //    };
    //    for (int i = 0; i < neighborDirections.Length; i++)
    //    {
    //        Vector3Int neighborPos = World.Instance.GetChunkPosition(transform.position) + neighborDirections[i];
    //        neighbors[i] = World.Instance.GetChunkAt(neighborPos);
    //    }
    //}

    private void GenerateVoxelData(Vector3 chunkWorldPosition)
    {
        int rand = Random.Range(-2, 2);
        //Stopwatch sw = new();
        //sw.Start();

        GenerateJob generateJob = new()
        {
            useVerticalChunks = World.Instance.useVerticalChunks,
            chunkHeight = chunkHeight,
            chunkSize = chunkSize,
            frequency = noiseFrequency,
            amplitude = noiseAmplitude,
            //lightFalloff = lightFalloff,
            chunkWorldPosition = chunkWorldPosition,
            voxels = new NativeArray<Voxel>(voxels.Length, Allocator.TempJob),
            //litVoxels = new NativeQueue<Vector3Int>(Allocator.TempJob)
            randInt = rand
        };

        JobHandle handle = generateJob.Schedule();
        handle.Complete();

        for (int i = 0; i < generateJob.voxels.Length; i++)
        {
            int x = i % chunkSize;
            int y = (i / chunkSize) % chunkHeight;
            int z = i / (chunkSize * chunkHeight);
            voxels[x, y, z] = generateJob.voxels[i];

            //if (Random.value > 0.99 && voxels[x, y - 1 < 0 ? y : y - 1, z].type == Voxel.VoxelType.Grass)
            //{
            //    Structure testStructure = new(voxels, Vector3Int.FloorToInt(voxels[x, y, z].position), Structure.StructureList.Test);
            //    testStructure.GenerateStructure();
            //}
        }

        generateJob.voxels.Dispose();
        //generateJob.litVoxels.Dispose();

        //sw.Stop();
        //UnityEngine.Debug.Log($"Generating voxel data for {name} took {sw.ElapsedMilliseconds} milliseconds");
    }

    /*public void CalculateLight()
    {
        //Stopwatch sw = new();
        //sw.Start();
        Queue<Vector3Int> litVoxels = new();

        for (int x = 0; x < chunkSize; x++)
        {
            for (int z = 0; z < chunkSize; z++)
            {
                float lightRay = 1f;

                for (int y = chunkHeight - 1; y >= 0; y--)
                {
                    Voxel thisVoxel = voxels[x, y, z];

                    if (thisVoxel.type != Voxel.VoxelType.Air && thisVoxel.transparency < lightRay)
                        lightRay = thisVoxel.transparency;

                    thisVoxel.globalLightPercentage = lightRay;

                    voxels[x, y, z] = thisVoxel;

                    if (lightRay > World.lightFalloff)
                    {
                        litVoxels.Enqueue(new Vector3Int(x, y, z));
                    }
                }
            }
        }

        while (litVoxels.Count > 0)
        {
            Vector3Int v = litVoxels.Dequeue();
            for (int p = 0; p < 6; p++)
            {
                Vector3 currentVoxel = new();

                switch (p)
                {
                    case 0:
                        currentVoxel = new Vector3Int(v.x, v.y + 1, v.z);
                        break;
                    case 1:
                        currentVoxel = new Vector3Int(v.x, v.y - 1, v.z);
                        break;
                    case 2:
                        currentVoxel = new Vector3Int(v.x - 1, v.y, v.z);
                        break;
                    case 3:
                        currentVoxel = new Vector3Int(v.x + 1, v.y, v.z);
                        break;
                    case 4:
                        currentVoxel = new Vector3Int(v.x, v.y, v.z + 1);
                        break;
                    case 5:
                        currentVoxel = new Vector3Int(v.x, v.y, v.z - 1);
                        break;
                }

                Vector3Int neighbor = new((int)currentVoxel.x, (int)currentVoxel.y, (int)currentVoxel.z);

                if (neighbor.x >= 0 && neighbor.x < chunkSize && neighbor.y >= 0 && neighbor.y < chunkHeight && neighbor.z >= 0 && neighbor.z < chunkSize) {
                    if (voxels[neighbor.x, neighbor.y, neighbor.z].globalLightPercentage < voxels[v.x, v.y, v.z].globalLightPercentage - World.lightFalloff)
                    {
                        voxels[neighbor.x, neighbor.y, neighbor.z].globalLightPercentage = voxels[v.x, v.y, v.z].globalLightPercentage - World.lightFalloff;

                        if (voxels[neighbor.x, neighbor.y, neighbor.z].globalLightPercentage > World.lightFalloff)
                        {
                            litVoxels.Enqueue(neighbor);
                        }
                    }
                }
                else
                {
                    //crosschunk lighting
                }
            }
        }
        //sw.Stop();
        //UnityEngine.Debug.Log($"Lighting for {name} took {sw.ElapsedMilliseconds} milliseconds");
    }*/

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
        //this.lightFalloff = World.lightFalloff;
        voxels = new Voxel[size, height, size];

        GenerateVoxelData(transform.position);
        //CalculateLight();

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
        if (voxels == null || x < 0 || x >= voxels.GetLength(0) || y < 0 || y >= voxels.GetLength(1) || z < 0 || z >= voxels.GetLength(2))
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

    public Vector3Int NeighborXYZ(Vector3Int pos)
    {
        Vector3Int neighborPos = new();

        if (pos.x < 0)
            neighborPos.x = chunkSize - 1;
        else if (pos.x > chunkSize - 1)
            neighborPos.x = 0;
        else if (pos.y < 0)
            neighborPos.y = chunkHeight - 1;
        else if (pos.y > chunkHeight - 1)
            neighborPos.y = 0;
        else if (pos.z < 0)
            neighborPos.z = chunkSize - 1;
        else if (pos.z > chunkSize - 1)
            neighborPos.z = 0;

        return neighborPos;
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
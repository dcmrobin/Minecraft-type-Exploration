using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using System.Threading.Tasks;
using System.Diagnostics;
using VoxelEngine;

public class Chunk : MonoBehaviour
{
    public Voxel[,,] voxels;
    private int chunkSize = 16;
    private int chunkHeight = 16;
    private AnimationCurve continentalnessCurve;
    private float noiseFrequency;
    private float noiseAmplitude;
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

    private void GenerateVoxelData(Vector3 chunkWorldPosition)
    {
        //Stopwatch sw = new();
        //sw.Start();

        int sampleCount = 100; // Adjust for desired precision
        NativeArray<float> curveSamples = new(sampleCount, Allocator.TempJob);
        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)(sampleCount - 1);
            curveSamples[i] = continentalnessCurve.Evaluate(t);
        }

        GenerateJob generateJob = new()
        {
            heightCurveSamples = curveSamples,
            useVerticalChunks = World.Instance.useVerticalChunks,
            chunkHeight = chunkHeight,
            chunkSize = chunkSize,
            frequency = noiseFrequency,
            amplitude = noiseAmplitude,
            //lightFalloff = lightFalloff,
            chunkWorldPosition = chunkWorldPosition,
            voxels = new NativeArray<Voxel>(voxels.Length, Allocator.TempJob),
            //litVoxels = new NativeQueue<Vector3Int>(Allocator.TempJob)
            randInt = Random.Range(-2, 2),
            worldSeed = World.Instance.noiseSeed
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

        //curveSamples.Dispose();
        generateJob.voxels.Dispose();
        generateJob.heightCurveSamples.Dispose();
        //generateJob.litVoxels.Dispose();

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

    public void Initialize(int size, int height, AnimationCurve continentalnessCurve)
    {
        //Stopwatch sw = new();
        //sw.Start();
        this.chunkSize = size;
        this.chunkHeight = height;
        this.continentalnessCurve = continentalnessCurve;
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
                    AddFaceData(vertices, triangles, uvs, colors, i, voxel.position, voxel.type);
                }
            }
        }
    }

    public void AddFaceData(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs, List<Color> colors, int faceIndex, Vector3 position, Voxel.VoxelType type)
    {
        Vector2[] faceUVs = GetFaceUVs(type, faceIndex);

        switch (faceIndex)
        {
            case 0: // Top Face
                vertices.Add(new Vector3(position.x, position.y + 1, position.z));
                vertices.Add(new Vector3(position.x, position.y + 1, position.z + 1));
                vertices.Add(new Vector3(position.x + 1, position.y + 1, position.z + 1));
                vertices.Add(new Vector3(position.x + 1, position.y + 1, position.z));
                break;
            case 1: // Bottom Face
                vertices.Add(new Vector3(position.x, position.y, position.z));
                vertices.Add(new Vector3(position.x + 1, position.y, position.z));
                vertices.Add(new Vector3(position.x + 1, position.y, position.z + 1));
                vertices.Add(new Vector3(position.x, position.y, position.z + 1));
                break;
            case 2: // Left Face
                vertices.Add(new Vector3(position.x, position.y, position.z));
                vertices.Add(new Vector3(position.x, position.y, position.z + 1));
                vertices.Add(new Vector3(position.x, position.y + 1, position.z + 1));
                vertices.Add(new Vector3(position.x, position.y + 1, position.z));
                break;
            case 3: // Right Face
                vertices.Add(new Vector3(position.x + 1, position.y, position.z + 1));
                vertices.Add(new Vector3(position.x + 1, position.y, position.z));
                vertices.Add(new Vector3(position.x + 1, position.y + 1, position.z));
                vertices.Add(new Vector3(position.x + 1, position.y + 1, position.z + 1));
                break;
            case 4: // Front Face
                vertices.Add(new Vector3(position.x, position.y, position.z + 1));
                vertices.Add(new Vector3(position.x + 1, position.y, position.z + 1));
                vertices.Add(new Vector3(position.x + 1, position.y + 1, position.z + 1));
                vertices.Add(new Vector3(position.x, position.y + 1, position.z + 1));
                break;
            case 5: // Back Face
                vertices.Add(new Vector3(position.x + 1, position.y, position.z));
                vertices.Add(new Vector3(position.x, position.y, position.z));
                vertices.Add(new Vector3(position.x, position.y + 1, position.z));
                vertices.Add(new Vector3(position.x + 1, position.y + 1, position.z));
                break;
        }

        for (int i = 0; i < 4; i++)
        {
            colors.Add(new Color(0, 0, 0, 1));
        }
        uvs.AddRange(faceUVs);

        // Adding triangle indices
        int vertCount = vertices.Count;
        triangles.Add(vertCount - 4);
        triangles.Add(vertCount - 3);
        triangles.Add(vertCount - 2);
        triangles.Add(vertCount - 4);
        triangles.Add(vertCount - 2);
        triangles.Add(vertCount - 1);
    }

    public static Vector2[] GetFaceUVs(Voxel.VoxelType type, int faceIndex)
    {
        float tileSize = 0.25f; // Assuming a 4x4 texture atlas (1/4 = 0.25)
        Vector2[] uvs = new Vector2[4];

        Vector2 tileOffset = GetTileOffset(type, faceIndex);

        uvs[0] = new Vector2(tileOffset.x, tileOffset.y);
        uvs[1] = new Vector2(tileOffset.x + tileSize, tileOffset.y);
        uvs[2] = new Vector2(tileOffset.x + tileSize, tileOffset.y + tileSize);
        uvs[3] = new Vector2(tileOffset.x, tileOffset.y + tileSize);

        return uvs;
    }

    public static Vector2 GetTileOffset(Voxel.VoxelType type, int faceIndex)
    {
        switch (type)
        {
            case Voxel.VoxelType.Grass:
                if (faceIndex == 0) // Top face
                    return new Vector2(0, 0.75f);
                if (faceIndex == 1) // Bottom face
                    return new Vector2(0.25f, 0.75f);
                return new Vector2(0, 0.5f); // Side faces

            case Voxel.VoxelType.Dirt:
                return new Vector2(0.25f, 0.75f);

            case Voxel.VoxelType.Stone:
                return new Vector2(0.25f, 0.5f);

            case Voxel.VoxelType.Deepslate:
                if (faceIndex == 0) // Top face
                    return new Vector2(0.5f, 0.5f);
                if (faceIndex == 1) // Bottom face
                    return new Vector2(0.5f, 0.5f);
                return new Vector2(0.5f, 0.75f); // Side faces

            case Voxel.VoxelType.Sand:
                return new Vector2(0.75f, 0.75f);

            // Add more cases for other types...

            default:
                return Vector2.zero;
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
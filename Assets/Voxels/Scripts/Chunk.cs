using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using SimplexNoise;
using System.Threading.Tasks;

public class Chunk : MonoBehaviour
{
    public AnimationCurve mountainsCurve;
    public AnimationCurve mountainBiomeCurve;
    private Voxel[,,] voxels;
    private int chunkSize = 16;
    private int chunkHeight = 16;
    private readonly List<Vector3> vertices = new();
    private readonly List<int> triangles = new();
    private readonly List<Vector2> uvs = new();
    List<Color> colors = new();
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;

    public Vector3 pos;
    private FastNoiseLite caveNoise = new();

    private void Start() {
        pos = transform.position;

        caveNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        caveNoise.SetFrequency(0.02f);
    }

    private void GenerateVoxelData(Vector3 chunkWorldPosition)
    {
        float[,] baseNoiseMap = Generate2DNoiseMap(chunkWorldPosition, 0.0055f);
        float[,] lod1Map = Generate2DNoiseMap(chunkWorldPosition, 0.16f, 25);
        float[,] biomeNoiseMap = Generate2DNoiseMap(chunkWorldPosition, 0.004f);

        float[,] mountainCurveValues = EvaluateNoiseMap(baseNoiseMap, mountainsCurve);
        float[,] mountainBiomeCurveValues = EvaluateNoiseMap(biomeNoiseMap, mountainBiomeCurve);

        float[,,] simplexMap = Generate3DNoiseMap(chunkWorldPosition, 0.025f, 1.5f);
        float[,,] caveMap = GenerateCaveMap(chunkWorldPosition, 1.5f);

        for (int x = 0; x < chunkSize; x++)
        {
            for (int z = 0; z < chunkSize; z++)
            {
                for (int y = 0; y < chunkHeight; y++)
                {
                    Vector3 voxelWorldPos = chunkWorldPosition + new Vector3(x, y, z);
                    float calculatedHeight = Voxel.CalculateHeight(x, z, y, mountainCurveValues, simplexMap, lod1Map, World.Instance.maxHeight);

                    Voxel.VoxelType type = Voxel.DetermineVoxelType(voxelWorldPos, calculatedHeight, caveMap[x, y, z]);
                    voxels[x, y, z] = new Voxel(new Vector3(x, y, z), type, type != Voxel.VoxelType.Air);
                }
            }
        }
    }

    private float[,] Generate2DNoiseMap(Vector3 chunkWorldPosition, float frequency, float divisor = 1f)
    {
        float[,] noiseMap = new float[chunkSize, chunkSize];
        for (int x = 0; x < chunkSize; x++)
            for (int z = 0; z < chunkSize; z++)
                noiseMap[x, z] = Mathf.PerlinNoise((chunkWorldPosition.x + x) * frequency, (chunkWorldPosition.z + z) * frequency) / divisor;

        return noiseMap;
    }

    private float[,] EvaluateNoiseMap(float[,] noiseMap, AnimationCurve curve)
    {
        float[,] evaluatedMap = new float[chunkSize, chunkSize];
        for (int x = 0; x < chunkSize; x++)
            for (int z = 0; z < chunkSize; z++)
                evaluatedMap[x, z] = curve.Evaluate(noiseMap[x, z]);

        return evaluatedMap;
    }

    private float[,,] Generate3DNoiseMap(Vector3 chunkWorldPosition, float frequency, float heightScale)
    {
        float[,,] noiseMap = new float[chunkSize, chunkHeight, chunkSize];
        for (int x = 0; x < chunkSize; x++)
            for (int z = 0; z < chunkSize; z++)
                for (int y = 0; y < chunkHeight; y++)
                    noiseMap[x, y, z] = Noise.CalcPixel3D((int)chunkWorldPosition.x + x, y, (int)chunkWorldPosition.z + z, frequency) / 600;

        return noiseMap;
    }

    private float[,,] GenerateCaveMap(Vector3 chunkWorldPosition, float heightScale)
    {
        float[,,] caveMap = new float[chunkSize, chunkHeight, chunkSize];
        for (int x = 0; x < chunkSize; x++)
            for (int z = 0; z < chunkSize; z++)
                for (int y = 0; y < chunkHeight; y++)
                    caveMap[x, y, z] = caveNoise.GetNoise(chunkWorldPosition.x + x, y, chunkWorldPosition.z + z);

        return caveMap;
    }

    void CalculateLight () {

        Queue<Vector3Int> litVoxels = new Queue<Vector3Int>();

        for (int x = 0; x < World.Instance.chunkSize; x++) {
            for (int z = 0; z < World.Instance.chunkSize; z++) {

                float lightRay = 1f;

                for (int y = World.Instance.chunkHeight - 1; y >= 0; y--) {

                    Voxel thisVoxel = voxels[x, y, z];

                    if (thisVoxel.type != Voxel.VoxelType.Air && thisVoxel.transparency < lightRay)
                        lightRay = thisVoxel.transparency;

                    thisVoxel.globalLightPercentage = lightRay;

                    voxels[x, y, z] = thisVoxel;

                    if (lightRay > World.lightFalloff)
                        litVoxels.Enqueue(new Vector3Int(x, y, z));

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

                if (IsWithinBounds(v)) {
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
                    //Debug.Log("out of bounds of chunk");
                }
            }
        }

    }

    private bool IsWithinBounds(Vector3Int v)
    {
        return v.x >= 0 && v.x < chunkSize && v.y >= 0 && v.y < chunkHeight && v.z >= 0 && v.z < chunkSize;
    }

    public void GenerateMesh()
    {
        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkHeight; y++)
            {
                for (int z = 0; z < chunkSize; z++)
                {
                    ProcessVoxel(x, y, z);
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
    }

    public void Initialize(int size, int height, AnimationCurve mountainsCurve, AnimationCurve mountainBiomeCurve)
    {
        this.chunkSize = size;
        this.chunkHeight = height;
        this.mountainsCurve = mountainsCurve;
        this.mountainBiomeCurve = mountainBiomeCurve;
        voxels = new Voxel[size, height, size];

        GenerateVoxelData(transform.position);
        CalculateLight();

        meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null) { meshFilter = gameObject.AddComponent<MeshFilter>(); }

        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null) { meshRenderer = gameObject.AddComponent<MeshRenderer>(); }

        meshCollider = GetComponent<MeshCollider>();
        if (meshCollider == null) { meshCollider = gameObject.AddComponent<MeshCollider>(); }

        GenerateMesh(); // Call after ensuring all necessary components and data are set
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
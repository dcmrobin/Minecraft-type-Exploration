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
                    float calculatedHeight = CalculateHeight(x, z, y, mountainCurveValues, simplexMap, lod1Map);

                    Voxel.VoxelType type = DetermineVoxelType(voxelWorldPos, calculatedHeight, caveMap[x, y, z]);
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
                    noiseMap[x, y, z] = Noise.CalcPixel3D((int)chunkWorldPosition.x + x, (int)((y + chunkWorldPosition.y) / heightScale), (int)chunkWorldPosition.z + z, frequency) / 600;

        return noiseMap;
    }

    private float[,,] GenerateCaveMap(Vector3 chunkWorldPosition, float heightScale)
    {
        float[,,] caveMap = new float[chunkSize, chunkHeight, chunkSize];
        for (int x = 0; x < chunkSize; x++)
            for (int z = 0; z < chunkSize; z++)
                for (int y = 0; y < chunkHeight; y++)
                    caveMap[x, y, z] = caveNoise.GetNoise(chunkWorldPosition.x + x, (y + chunkWorldPosition.y) / heightScale, chunkWorldPosition.z + z);

        return caveMap;
    }

    private float CalculateHeight(int x, int z, int y, float[,] mountainCurveValues, float[,,] simplexMap, float[,] lod1Map)
    {
        float normalizedNoiseValue = (mountainCurveValues[x, z] - simplexMap[x, y, z] + lod1Map[x, z]) * 400;
        float calculatedHeight = normalizedNoiseValue * World.Instance.maxHeight * mountainCurveValues[x, z];
        return calculatedHeight + 150;
    }

    private Voxel.VoxelType DetermineVoxelType(Vector3 voxelWorldPos, float calculatedHeight, float caveNoiseValue)
    {
        Voxel.VoxelType type = voxelWorldPos.y <= calculatedHeight ? Voxel.VoxelType.Stone : Voxel.VoxelType.Air;

        if (type != Voxel.VoxelType.Air && voxelWorldPos.y < calculatedHeight && voxelWorldPos.y >= calculatedHeight - 3)
            type = Voxel.VoxelType.Dirt;

        if (type == Voxel.VoxelType.Dirt && voxelWorldPos.y <= calculatedHeight && voxelWorldPos.y > calculatedHeight - 1)
            type = Voxel.VoxelType.Grass;

        if (caveNoiseValue > 0.45f && voxelWorldPos.y <= 100 + (caveNoiseValue * 20) || caveNoiseValue > 0.8f && voxelWorldPos.y > 100 + (caveNoiseValue * 20))
            type = Voxel.VoxelType.Air;

        return type;
    }

    public void CalculateLight()
    {
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
                Vector3 currentVoxel = GetNeighbor(v, p);

                Vector3Int neighbor = new((int)currentVoxel.x, (int)currentVoxel.y, (int)currentVoxel.z);

                if (IsWithinBounds(neighbor)) {
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

    private Vector3Int GetNeighbor(Vector3Int v, int direction)
    {
        return direction switch
        {
            0 => new Vector3Int(v.x, v.y + 1, v.z),
            1 => new Vector3Int(v.x, v.y - 1, v.z),
            2 => new Vector3Int(v.x - 1, v.y, v.z),
            3 => new Vector3Int(v.x + 1, v.y, v.z),
            4 => new Vector3Int(v.x, v.y, v.z + 1),
            5 => new Vector3Int(v.x, v.y, v.z - 1),
            _ => v
        };
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
        // Check if the voxels array is initialized and the indices are within bounds
        if (voxels == null || x < 0 || x >= voxels.GetLength(0) || 
            y < 0 || y >= voxels.GetLength(1) || z < 0 || z >= voxels.GetLength(2))
        {
            return; // Skip processing if the array is not initialized or indices are out of bounds
        } 
        Voxel voxel = voxels[x, y, z];
        if (voxel.isActive)
        {
            // Check each face of the voxel for visibility
            bool[] facesVisible = new bool[6];

            // Check visibility for each face
            facesVisible[0] = IsFaceVisible(x, y + 1, z); // Top
            facesVisible[1] = IsFaceVisible(x, y - 1, z); // Bottom
            facesVisible[2] = IsFaceVisible(x - 1, y, z); // Left
            facesVisible[3] = IsFaceVisible(x + 1, y, z); // Right
            facesVisible[4] = IsFaceVisible(x, y, z + 1); // Front
            facesVisible[5] = IsFaceVisible(x, y, z - 1); // Back
            
            for (int i = 0; i < facesVisible.Length; i++)
            {
                if (facesVisible[i])
                    AddFaceData(x, y, z, i); // Method to add mesh data for the visible face
            }
        }
    }

    private bool IsFaceVisible(int x, int y, int z)
    {
        // Convert local chunk coordinates to global coordinates
        _ = pos + new Vector3(x, y, z);

        // Check if the neighboring voxel is inactive or out of bounds in the current chunk
        // and also if it's inactive or out of bounds in the world (neighboring chunks)

        //return IsVoxelHiddenInChunk(x, y, z) && IsVoxelHiddenInWorld(globalPos);// doesn't work because of multithreading not allowing use of get_transform
        return IsVoxelHiddenInChunk(x, y, z);
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

    private void AddFaceData(int x, int y, int z, int faceIndex)
    {
        Voxel voxel = voxels[x, y, z];
        Voxel neighborVoxel = new();

        switch (faceIndex)
        {
            case 0:
                neighborVoxel = GetVoxelSafe(x, y + 1, z);
                break;
            case 1:
                neighborVoxel = GetVoxelSafe(x, y - 1, z);
                break;
            case 2:
                neighborVoxel = GetVoxelSafe(x - 1, y, z);
                break;
            case 3:
                neighborVoxel = GetVoxelSafe(x + 1, y, z);
                break;
            case 4:
                neighborVoxel = GetVoxelSafe(x, y, z + 1);
                break;
            case 5:
                neighborVoxel = GetVoxelSafe(x, y, z - 1);
                break;
            default:
                Debug.Log("Error");
                neighborVoxel = voxels[x, y, z];
                break;
        }

        Vector2[] faceUVs = GetFaceUVs(voxel.type, faceIndex);


        float lightLevel = neighborVoxel.globalLightPercentage;
        //float lightLevel = 1;

        if (faceIndex == 0) // Top Face
        {
            vertices.Add(new Vector3(x,     y + 1, z    ));
            vertices.Add(new Vector3(x,     y + 1, z + 1)); 
            vertices.Add(new Vector3(x + 1, y + 1, z + 1));
            vertices.Add(new Vector3(x + 1, y + 1, z    )); 
            colors.Add(new Color(0, 0, 0, lightLevel));
            colors.Add(new Color(0, 0, 0, lightLevel));
            colors.Add(new Color(0, 0, 0, lightLevel));
            colors.Add(new Color(0, 0, 0, lightLevel));
            uvs.AddRange(faceUVs);
        }

        if (faceIndex == 1) // Bottom Face
        {
            vertices.Add(new Vector3(x,     y, z    ));
            vertices.Add(new Vector3(x + 1, y, z    )); 
            vertices.Add(new Vector3(x + 1, y, z + 1));
            vertices.Add(new Vector3(x,     y, z + 1)); 
            colors.Add(new Color(0, 0, 0, lightLevel));
            colors.Add(new Color(0, 0, 0, lightLevel));
            colors.Add(new Color(0, 0, 0, lightLevel));
            colors.Add(new Color(0, 0, 0, lightLevel));
            uvs.AddRange(faceUVs);
        }

        if (faceIndex == 2) // Left Face
        {
            vertices.Add(new Vector3(x, y,     z    ));
            vertices.Add(new Vector3(x, y,     z + 1));
            vertices.Add(new Vector3(x, y + 1, z + 1));
            vertices.Add(new Vector3(x, y + 1, z    ));
            colors.Add(new Color(0, 0, 0, lightLevel));
            colors.Add(new Color(0, 0, 0, lightLevel));
            colors.Add(new Color(0, 0, 0, lightLevel));
            colors.Add(new Color(0, 0, 0, lightLevel));
            uvs.AddRange(faceUVs);
        }

        if (faceIndex == 3) // Right Face
        {
            vertices.Add(new Vector3(x + 1, y,     z + 1));
            vertices.Add(new Vector3(x + 1, y,     z    ));
            vertices.Add(new Vector3(x + 1, y + 1, z    ));
            vertices.Add(new Vector3(x + 1, y + 1, z + 1));
            colors.Add(new Color(0, 0, 0, lightLevel));
            colors.Add(new Color(0, 0, 0, lightLevel));
            colors.Add(new Color(0, 0, 0, lightLevel));
            colors.Add(new Color(0, 0, 0, lightLevel));
            uvs.AddRange(faceUVs);
        }

        if (faceIndex == 4) // Front Face
        {
            vertices.Add(new Vector3(x,     y,     z + 1));
            vertices.Add(new Vector3(x + 1, y,     z + 1));
            vertices.Add(new Vector3(x + 1, y + 1, z + 1));
            vertices.Add(new Vector3(x,     y + 1, z + 1));
            colors.Add(new Color(0, 0, 0, lightLevel));
            colors.Add(new Color(0, 0, 0, lightLevel));
            colors.Add(new Color(0, 0, 0, lightLevel));
            colors.Add(new Color(0, 0, 0, lightLevel));
            uvs.AddRange(faceUVs);
        }

        if (faceIndex == 5) // Back Face
        {
            vertices.Add(new Vector3(x + 1, y,     z    ));
            vertices.Add(new Vector3(x,     y,     z    ));
            vertices.Add(new Vector3(x,     y + 1, z    ));
            vertices.Add(new Vector3(x + 1, y + 1, z    ));
            colors.Add(new Color(0, 0, 0, lightLevel));
            colors.Add(new Color(0, 0, 0, lightLevel));
            colors.Add(new Color(0, 0, 0, lightLevel));
            colors.Add(new Color(0, 0, 0, lightLevel));
            uvs.AddRange(faceUVs);
        }

        AddTriangleIndices();
    }

    private Vector2[] GetFaceUVs(Voxel.VoxelType type, int faceIndex)
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

    private Vector2 GetTileOffset(Voxel.VoxelType type, int faceIndex)
    {
        switch (type)
        {
            case Voxel.VoxelType.Grass:
                if (faceIndex == 0) // Top face
                    return new Vector2(0, 0.75f); // Adjust based on your texture atlas
                if (faceIndex == 1) // Bottom face
                    return new Vector2(0.25f, 0.75f); // Adjust based on your texture atlas
                return new Vector2(0, 0.5f); // Side faces

            case Voxel.VoxelType.Dirt:
                return new Vector2(0.25f, 0.75f); // Adjust based on your texture atlas

            case Voxel.VoxelType.Stone:
                return new Vector2(0.25f, 0.5f); // Adjust based on your texture atlas

            // Add more cases for other types...

            default:
                return Vector2.zero;
        }
    }

    private void AddTriangleIndices()
    {
        int vertCount = vertices.Count;

        // First triangle
        triangles.Add(vertCount - 4);
        triangles.Add(vertCount - 3);
        triangles.Add(vertCount - 2);

        // Second triangle
        triangles.Add(vertCount - 4);
        triangles.Add(vertCount - 2);
        triangles.Add(vertCount - 1);
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
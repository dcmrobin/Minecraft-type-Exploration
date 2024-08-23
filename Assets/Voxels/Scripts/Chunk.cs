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
    private List<Vector3> vertices = new List<Vector3>();
    private List<int> triangles = new List<int>();
    private List<Vector2> uvs = new List<Vector2>();
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;
    private Vector3 pos;
    private Vector3 localPos;

    private void Start() {
        pos = transform.position;
        localPos = transform.InverseTransformPoint(pos);
    }

    private async Task GenerateVoxelDataAsync(Vector3 chunkWorldPosition)
    {
        // Precompute noise values and curve evaluations
        float[,] baseNoiseMap = new float[chunkSize, chunkSize];
        float[,] lod1Map = new float[chunkSize, chunkSize];
        float[,] simplexMap = new float[chunkSize, chunkSize];
        float[,] biomeNoiseMap = new float[chunkSize, chunkSize];
        float[,] mountainCurveValues = new float[chunkSize, chunkSize];
        float[,] biomeCurveValues = new float[chunkSize, chunkSize];

        GenerateVoxelsJob generateVoxelsJob = new GenerateVoxelsJob
        {
            chunkSize = chunkSize,
            chunkHeight = chunkHeight,
            chunkWorldPosition = chunkWorldPosition,
            maxHeight = World.Instance.maxHeight,
            baseNoiseMap = new NativeArray<float>(baseNoiseMap.Length, Allocator.TempJob),
            lod1Map = new NativeArray<float>(lod1Map.Length, Allocator.TempJob),
            simplexMap = new NativeArray<float>(simplexMap.Length, Allocator.TempJob),
            mountainCurveValues = new NativeArray<float>(mountainCurveValues.Length, Allocator.TempJob),
            biomeCurveValues = new NativeArray<float>(biomeCurveValues.Length, Allocator.TempJob),
            voxelsData = new NativeArray<Voxel>(chunkSize * chunkHeight * chunkSize, Allocator.TempJob)
        };

        FixGrassJob fixGrassJob = new FixGrassJob
        {
            chunkSize = chunkSize,
            chunkHeight = chunkHeight,
            voxelsData = generateVoxelsJob.voxelsData,
            updatedVoxelsData = new NativeArray<Voxel>(chunkSize * chunkHeight * chunkSize, Allocator.TempJob)
        };

        // Use Task.Run to run CPU-bound work on a background thread
        await Task.Run(() =>
        {
            for (int x = 0; x < chunkSize; x++)
            {
                for (int z = 0; z < chunkSize; z++)
                {
                    Vector3 worldPos = chunkWorldPosition + new Vector3(x, 0, z);

                    baseNoiseMap[x, z] = Mathf.PerlinNoise(worldPos.x * 0.0055f, worldPos.z * 0.0055f);
                    lod1Map[x, z] = Mathf.PerlinNoise(worldPos.x * 0.16f, worldPos.z * 0.16f) / 25;
                    simplexMap[x, z] = Noise.CalcPixel3D((int)worldPos.x, 0, (int)worldPos.z, 0.025f) / 600;
                    biomeNoiseMap[x, z] = Mathf.PerlinNoise(worldPos.x * 0.004f, worldPos.z * 0.004f);

                    mountainCurveValues[x, z] = mountainsCurve.Evaluate(baseNoiseMap[x, z]);
                    biomeCurveValues[x, z] = mountainBiomeCurve.Evaluate(biomeNoiseMap[x, z]);

                    int index = x * chunkSize + z;
                    generateVoxelsJob.baseNoiseMap[index] = baseNoiseMap[x, z];
                    generateVoxelsJob.lod1Map[index] = lod1Map[x, z];
                    generateVoxelsJob.simplexMap[index] = simplexMap[x, z];
                    generateVoxelsJob.mountainCurveValues[index] = mountainCurveValues[x, z];
                    generateVoxelsJob.biomeCurveValues[index] = biomeCurveValues[x, z];
                }
            }
        });

        JobHandle handle = generateVoxelsJob.Schedule(chunkSize * chunkHeight * chunkSize, 64);
        handle.Complete();

        JobHandle fixGrassHandle = fixGrassJob.Schedule(chunkSize * chunkHeight * chunkSize, 64);
        fixGrassHandle.Complete();

        Vector3 pos = transform.position;

        await Task.Run(() =>
        {
            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkHeight; y++)
                {
                    for (int z = 0; z < chunkSize; z++)
                    {
                        int index = x * chunkSize * chunkHeight + y * chunkSize + z;
                        Voxel voxel = generateVoxelsJob.voxelsData[index];

                        // Use world coordinates for noise sampling
                        Vector3 worldPos = pos + new Vector3(x, y, z);

                        // Now the voxel type is already determined by the job
                        voxels[x, y, z] = new Voxel(worldPos, voxel.type, voxel.isActive);
                    }
                }
            }
        });

        await Task.Run(() =>
        {
            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkHeight; y++)
                {
                    for (int z = 0; z < chunkSize; z++)
                    {
                        int index = x * chunkSize * chunkHeight + y * chunkSize + z;
                        Voxel voxel = fixGrassJob.updatedVoxelsData[index];

                        // Use world coordinates for noise sampling
                        Vector3 worldPos = pos + new Vector3(x, y, z);

                        // Now the voxel type is already determined by the job
                        voxels[x, y, z] = new Voxel(worldPos, voxel.type, voxel.isActive);
                    }
                }
            }
        });

        // Dispose of NativeArrays
        generateVoxelsJob.baseNoiseMap.Dispose();
        generateVoxelsJob.lod1Map.Dispose();
        generateVoxelsJob.simplexMap.Dispose();
        generateVoxelsJob.mountainCurveValues.Dispose();
        generateVoxelsJob.biomeCurveValues.Dispose();
        generateVoxelsJob.voxelsData.Dispose();
        fixGrassJob.updatedVoxelsData.Dispose();
    }

    public async Task GenerateMesh()
    {
        await Task.Run(() => {
            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkHeight; y++)
                {
                    for (int z = 0; z < chunkSize; z++)
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
                }
            }
        });
        if (vertices.Count > 0) {
            Mesh mesh = new Mesh();
            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.uv = uvs.ToArray();

            mesh.RecalculateNormals(); // Important for lighting

            meshFilter.mesh = mesh;
            meshCollider.sharedMesh = mesh;

            // Apply a material or texture if needed
            meshRenderer.material = World.Instance.VoxelMaterial;
        }
    }

    public async Task Initialize(int size, int height, AnimationCurve mountainsCurve, AnimationCurve mountainBiomeCurve)
    {
        this.chunkSize = size;
        this.chunkHeight = height;
        this.mountainsCurve = mountainsCurve;
        this.mountainBiomeCurve = mountainBiomeCurve;
        voxels = new Voxel[size, height, size];

        // Call GenerateVoxelData asynchronously
        await GenerateVoxelDataAsync(transform.position);

        meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null) { meshFilter = gameObject.AddComponent<MeshFilter>(); }

        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null) { meshRenderer = gameObject.AddComponent<MeshRenderer>(); }

        meshCollider = GetComponent<MeshCollider>();
        if (meshCollider == null) { meshCollider = gameObject.AddComponent<MeshCollider>(); }

        await GenerateMesh(); // Call after ensuring all necessary components and data are set
    }

    private bool IsFaceVisible(int x, int y, int z)
    {
        // Convert local chunk coordinates to global coordinates
        Vector3 globalPos = pos + new Vector3(x, y, z);

        // Check if the neighboring voxel is inactive or out of bounds in the current chunk
        // and also if it's inactive or out of bounds in the world (neighboring chunks)
        return IsVoxelHiddenInChunk(x, y, z) && IsVoxelHiddenInWorld(globalPos);
    }

    private bool IsVoxelHiddenInChunk(int x, int y, int z)
    {
        if (x < 0 || x >= chunkSize || y < 0 || y >= chunkHeight || z < 0 || z >= chunkSize)
            return true; // Face is at the boundary of the chunk
        return !voxels[x, y, z].isActive;
    }

    private bool IsVoxelHiddenInWorld(Vector3 globalPos)
    {
        // Check if there is a chunk at the global position
        Chunk neighborChunk = World.Instance.GetChunkAt(globalPos);
        if (neighborChunk == null)
        {
            // No chunk at this position, so the voxel face should be hidden
            return true;
        }

        // Convert the global position to the local position within the neighboring chunk
        //Vector3 localPos = neighborChunk.transform.InverseTransformPoint(globalPos);//                                         -here-

        // If the voxel at this local position is inactive, the face should be visible (not hidden)
        return !neighborChunk.IsVoxelActiveAt(neighborChunk.localPos);
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

    private void AddFaceData(int x, int y, int z, int faceIndex)
    {
        Voxel voxel = voxels[x, y, z];
        Vector2[] faceUVs = GetFaceUVs(voxel.type, faceIndex);

        if (faceIndex == 0) // Top Face
        {
            vertices.Add(new Vector3(x,     y + 1, z    ));
            vertices.Add(new Vector3(x,     y + 1, z + 1)); 
            vertices.Add(new Vector3(x + 1, y + 1, z + 1));
            vertices.Add(new Vector3(x + 1, y + 1, z    )); 
            uvs.AddRange(faceUVs);
        }

        if (faceIndex == 1) // Bottom Face
        {
            vertices.Add(new Vector3(x,     y, z    ));
            vertices.Add(new Vector3(x + 1, y, z    )); 
            vertices.Add(new Vector3(x + 1, y, z + 1));
            vertices.Add(new Vector3(x,     y, z + 1)); 
            uvs.AddRange(faceUVs);
        }

        if (faceIndex == 2) // Left Face
        {
            vertices.Add(new Vector3(x, y,     z    ));
            vertices.Add(new Vector3(x, y,     z + 1));
            vertices.Add(new Vector3(x, y + 1, z + 1));
            vertices.Add(new Vector3(x, y + 1, z    ));
            uvs.AddRange(faceUVs);
        }

        if (faceIndex == 3) // Right Face
        {
            vertices.Add(new Vector3(x + 1, y,     z + 1));
            vertices.Add(new Vector3(x + 1, y,     z    ));
            vertices.Add(new Vector3(x + 1, y + 1, z    ));
            vertices.Add(new Vector3(x + 1, y + 1, z + 1));
            uvs.AddRange(faceUVs);
        }

        if (faceIndex == 4) // Front Face
        {
            vertices.Add(new Vector3(x,     y,     z + 1));
            vertices.Add(new Vector3(x + 1, y,     z + 1));
            vertices.Add(new Vector3(x + 1, y + 1, z + 1));
            vertices.Add(new Vector3(x,     y + 1, z + 1));
            uvs.AddRange(faceUVs);
        }

        if (faceIndex == 5) // Back Face
        {
            vertices.Add(new Vector3(x + 1, y,     z    ));
            vertices.Add(new Vector3(x,     y,     z    ));
            vertices.Add(new Vector3(x,     y + 1, z    ));
            vertices.Add(new Vector3(x + 1, y + 1, z    ));
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
        }
    }
}

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
        vertices.Clear();
        triangles.Clear();
        colors.Clear();

        // Process top and bottom faces with greedy meshing
        for (int y = 0; y < chunkHeight; y++)
        {
            bool[,] topMask = new bool[chunkSize, chunkSize];
            bool[,] bottomMask = new bool[chunkSize, chunkSize];
            Voxel.VoxelType[,] topTypes = new Voxel.VoxelType[chunkSize, chunkSize];
            Voxel.VoxelType[,] bottomTypes = new Voxel.VoxelType[chunkSize, chunkSize];

            // Create masks for this layer
            for (int x = 0; x < chunkSize; x++)
            {
                for (int z = 0; z < chunkSize; z++)
                {
                    if (voxels[x, y, z].type != Voxel.VoxelType.Air)
                    {
                        // Check top face
                        if (IsVoxelHiddenInChunk(x, y + 1, z))
                        {
                            topMask[x, z] = true;
                            topTypes[x, z] = voxels[x, y, z].type;
                        }

                        // Check bottom face
                        if (IsVoxelHiddenInChunk(x, y - 1, z))
                        {
                            bottomMask[x, z] = true;
                            bottomTypes[x, z] = voxels[x, y, z].type;
                        }
                    }
                }
            }

            // Greedy meshing for top faces
            for (int x = 0; x < chunkSize; x++)
            {
                for (int z = 0; z < chunkSize; z++)
                {
                    if (!topMask[x, z]) continue;

                    Voxel.VoxelType type = topTypes[x, z];
                    int width = 1;
                    int height = 1;

                    // Find width
                    while (x + width < chunkSize && topMask[x + width, z] && topTypes[x + width, z] == type)
                    {
                        width++;
                    }

                    // Find height
                    bool canExpand = true;
                    while (canExpand && z + height < chunkSize)
                    {
                        for (int i = 0; i < width; i++)
                        {
                            if (!topMask[x + i, z + height] || topTypes[x + i, z + height] != type)
                            {
                                canExpand = false;
                                break;
                            }
                        }
                        if (canExpand) height++;
                    }

                    // Create the quad
                    Vector3 pos = new Vector3(x, y, z);
                    AddGreedyQuad(pos, width, height, 0, type);

                    // Clear the mask for this rectangle
                    for (int i = 0; i < width; i++)
                    {
                        for (int j = 0; j < height; j++)
                        {
                            topMask[x + i, z + j] = false;
                        }
                    }
                }
            }

            // Greedy meshing for bottom faces
            for (int x = 0; x < chunkSize; x++)
            {
                for (int z = 0; z < chunkSize; z++)
                {
                    if (!bottomMask[x, z]) continue;

                    Voxel.VoxelType type = bottomTypes[x, z];
                    int width = 1;
                    int height = 1;

                    // Find width
                    while (x + width < chunkSize && bottomMask[x + width, z] && bottomTypes[x + width, z] == type)
                    {
                        width++;
                    }

                    // Find height
                    bool canExpand = true;
                    while (canExpand && z + height < chunkSize)
                    {
                        for (int i = 0; i < width; i++)
                        {
                            if (!bottomMask[x + i, z + height] || bottomTypes[x + i, z + height] != type)
                            {
                                canExpand = false;
                                break;
                            }
                        }
                        if (canExpand) height++;
                    }

                    // Create the quad
                    Vector3 pos = new Vector3(x, y, z);
                    AddGreedyQuad(pos, width, height, 1, type);

                    // Clear the mask for this rectangle
                    for (int i = 0; i < width; i++)
                    {
                        for (int j = 0; j < height; j++)
                        {
                            bottomMask[x + i, z + j] = false;
                        }
                    }
                }
            }
        }

        // Process side faces with greedy meshing
        // Left and right faces
        for (int x = 0; x < chunkSize; x++)
        {
            bool[,] leftMask = new bool[chunkHeight, chunkSize];
            bool[,] rightMask = new bool[chunkHeight, chunkSize];
            Voxel.VoxelType[,] leftTypes = new Voxel.VoxelType[chunkHeight, chunkSize];
            Voxel.VoxelType[,] rightTypes = new Voxel.VoxelType[chunkHeight, chunkSize];

            // Create masks for this slice
            for (int y = 0; y < chunkHeight; y++)
            {
                for (int z = 0; z < chunkSize; z++)
                {
                    if (voxels[x, y, z].type != Voxel.VoxelType.Air)
                    {
                        // Check left face
                        if (IsVoxelHiddenInChunk(x - 1, y, z))
                        {
                            leftMask[y, z] = true;
                            leftTypes[y, z] = voxels[x, y, z].type;
                        }

                        // Check right face
                        if (IsVoxelHiddenInChunk(x + 1, y, z))
                        {
                            rightMask[y, z] = true;
                            rightTypes[y, z] = voxels[x, y, z].type;
                        }
                    }
                }
            }

            // Greedy meshing for left faces
            for (int y = 0; y < chunkHeight; y++)
            {
                for (int z = 0; z < chunkSize; z++)
                {
                    if (!leftMask[y, z]) continue;

                    Voxel.VoxelType type = leftTypes[y, z];
                    int width = 1;
                    int height = 1;

                    // Find width (vertical)
                    while (y + width < chunkHeight && leftMask[y + width, z] && leftTypes[y + width, z] == type)
                    {
                        width++;
                    }

                    // Find height (horizontal)
                    bool canExpand = true;
                    while (canExpand && z + height < chunkSize)
                    {
                        for (int i = 0; i < width; i++)
                        {
                            if (!leftMask[y + i, z + height] || leftTypes[y + i, z + height] != type)
                            {
                                canExpand = false;
                                break;
                            }
                        }
                        if (canExpand) height++;
                    }

                    // Create the quad
                    Vector3 pos = new Vector3(x, y, z);
                    AddGreedyQuad(pos, width, height, 2, type);

                    // Clear the mask for this rectangle
                    for (int i = 0; i < width; i++)
                    {
                        for (int j = 0; j < height; j++)
                        {
                            leftMask[y + i, z + j] = false;
                        }
                    }
                }
            }

            // Greedy meshing for right faces
            for (int y = 0; y < chunkHeight; y++)
            {
                for (int z = 0; z < chunkSize; z++)
                {
                    if (!rightMask[y, z]) continue;

                    Voxel.VoxelType type = rightTypes[y, z];
                    int width = 1;
                    int height = 1;

                    // Find width (vertical)
                    while (y + width < chunkHeight && rightMask[y + width, z] && rightTypes[y + width, z] == type)
                    {
                        width++;
                    }

                    // Find height (horizontal)
                    bool canExpand = true;
                    while (canExpand && z + height < chunkSize)
                    {
                        for (int i = 0; i < width; i++)
                        {
                            if (!rightMask[y + i, z + height] || rightTypes[y + i, z + height] != type)
                            {
                                canExpand = false;
                                break;
                            }
                        }
                        if (canExpand) height++;
                    }

                    // Create the quad
                    Vector3 pos = new Vector3(x, y, z);
                    AddGreedyQuad(pos, width, height, 3, type);

                    // Clear the mask for this rectangle
                    for (int i = 0; i < width; i++)
                    {
                        for (int j = 0; j < height; j++)
                        {
                            rightMask[y + i, z + j] = false;
                        }
                    }
                }
            }
        }

        // Front and back faces
        for (int z = 0; z < chunkSize; z++)
        {
            bool[,] frontMask = new bool[chunkHeight, chunkSize];
            bool[,] backMask = new bool[chunkHeight, chunkSize];
            Voxel.VoxelType[,] frontTypes = new Voxel.VoxelType[chunkHeight, chunkSize];
            Voxel.VoxelType[,] backTypes = new Voxel.VoxelType[chunkHeight, chunkSize];

            // Create masks for this slice
            for (int y = 0; y < chunkHeight; y++)
            {
                for (int x = 0; x < chunkSize; x++)
                {
                    if (voxels[x, y, z].type != Voxel.VoxelType.Air)
                    {
                        // Check front face
                        if (IsVoxelHiddenInChunk(x, y, z + 1))
                        {
                            frontMask[y, x] = true;
                            frontTypes[y, x] = voxels[x, y, z].type;
                        }

                        // Check back face
                        if (IsVoxelHiddenInChunk(x, y, z - 1))
                        {
                            backMask[y, x] = true;
                            backTypes[y, x] = voxels[x, y, z].type;
                        }
                    }
                }
            }

            // Greedy meshing for front faces
            for (int y = 0; y < chunkHeight; y++)
            {
                for (int x = 0; x < chunkSize; x++)
                {
                    if (!frontMask[y, x]) continue;

                    Voxel.VoxelType type = frontTypes[y, x];
                    int width = 1;
                    int height = 1;

                    // Find width (vertical)
                    while (y + width < chunkHeight && frontMask[y + width, x] && frontTypes[y + width, x] == type)
                    {
                        width++;
                    }

                    // Find height (horizontal)
                    bool canExpand = true;
                    while (canExpand && x + height < chunkSize)
                    {
                        for (int i = 0; i < width; i++)
                        {
                            if (!frontMask[y + i, x + height] || frontTypes[y + i, x + height] != type)
                            {
                                canExpand = false;
                                break;
                            }
                        }
                        if (canExpand) height++;
                    }

                    // Create the quad
                    Vector3 pos = new Vector3(x, y, z);
                    AddGreedyQuad(pos, height, width, 4, type);

                    // Clear the mask for this rectangle
                    for (int i = 0; i < width; i++)
                    {
                        for (int j = 0; j < height; j++)
                        {
                            frontMask[y + i, x + j] = false;
                        }
                    }
                }
            }

            // Greedy meshing for back faces
            for (int y = 0; y < chunkHeight; y++)
            {
                for (int x = 0; x < chunkSize; x++)
                {
                    if (!backMask[y, x]) continue;

                    Voxel.VoxelType type = backTypes[y, x];
                    int width = 1;
                    int height = 1;

                    // Find width (vertical)
                    while (y + width < chunkHeight && backMask[y + width, x] && backTypes[y + width, x] == type)
                    {
                        width++;
                    }

                    // Find height (horizontal)
                    bool canExpand = true;
                    while (canExpand && x + height < chunkSize)
                    {
                        for (int i = 0; i < width; i++)
                        {
                            if (!backMask[y + i, x + height] || backTypes[y + i, x + height] != type)
                            {
                                canExpand = false;
                                break;
                            }
                        }
                        if (canExpand) height++;
                    }

                    // Create the quad
                    Vector3 pos = new Vector3(x, y, z);
                    AddGreedyQuad(pos, height, width, 5, type);

                    // Clear the mask for this rectangle
                    for (int i = 0; i < width; i++)
                    {
                        for (int j = 0; j < height; j++)
                        {
                            backMask[y + i, x + j] = false;
                        }
                    }
                }
            }
        }

        if (vertices.Count > 0)
        {
            Mesh mesh = new()
            {
                vertices = vertices.ToArray(),
                triangles = triangles.ToArray(),
                colors = colors.ToArray()
            };

            mesh.RecalculateNormals();
            meshFilter.mesh = mesh;
            meshCollider.sharedMesh = mesh;
            meshRenderer.material = World.Instance.VoxelMaterial;
        }
    }

    private void GreedyMeshLayer(int y, bool isTop)
    {
        bool[,] mask = new bool[chunkSize, chunkSize];
        Voxel.VoxelType[,] types = new Voxel.VoxelType[chunkSize, chunkSize];

        // Create the mask and type arrays
        for (int x = 0; x < chunkSize; x++)
        {
            for (int z = 0; z < chunkSize; z++)
            {
                if (voxels[x, y, z].type != Voxel.VoxelType.Air)
                {
                    bool shouldRender = isTop ? 
                        IsVoxelHiddenInChunk(x, y + 1, z) : 
                        IsVoxelHiddenInChunk(x, y - 1, z);

                    if (shouldRender)
                    {
                        mask[x, z] = true;
                        types[x, z] = voxels[x, y, z].type;
                    }
                }
            }
        }

        // Greedy meshing
        for (int x = 0; x < chunkSize; x++)
        {
            for (int z = 0; z < chunkSize; z++)
            {
                if (!mask[x, z]) continue;

                Voxel.VoxelType type = types[x, z];
                int width = 1;
                int height = 1;

                // Find width
                while (x + width < chunkSize && mask[x + width, z] && types[x + width, z] == type)
                {
                    width++;
                }

                // Find height
                bool canExpand = true;
                while (canExpand && z + height < chunkSize)
                {
                    for (int i = 0; i < width; i++)
                    {
                        if (!mask[x + i, z + height] || types[x + i, z + height] != type)
                        {
                            canExpand = false;
                            break;
                        }
                    }
                    if (canExpand) height++;
                }

                // Create the quad
                Vector3 pos = new Vector3(x, y, z);
                if (isTop)
                {
                    AddGreedyQuad(pos, width, height, 0, type); // Top face
                }
                else
                {
                    AddGreedyQuad(pos, width, height, 1, type); // Bottom face
                }

                // Clear the mask for this rectangle
                for (int i = 0; i < width; i++)
                {
                    for (int j = 0; j < height; j++)
                    {
                        mask[x + i, z + j] = false;
                    }
                }
            }
        }
    }

    private void GreedyMeshSlice(int slice, bool isX, bool isPositive)
    {
        bool[,] mask = new bool[isX ? chunkHeight : chunkSize, isX ? chunkSize : chunkHeight];
        Voxel.VoxelType[,] types = new Voxel.VoxelType[isX ? chunkHeight : chunkSize, isX ? chunkSize : chunkHeight];

        // Create the mask and type arrays
        for (int i = 0; i < (isX ? chunkHeight : chunkSize); i++)
        {
            for (int j = 0; j < (isX ? chunkSize : chunkHeight); j++)
            {
                int x = isX ? slice : j;
                int y = isX ? i : slice;
                int z = isX ? j : i;

                if (voxels[x, y, z].type != Voxel.VoxelType.Air)
                {
                    bool shouldRender = isX ?
                        (isPositive ? IsVoxelHiddenInChunk(x + 1, y, z) : IsVoxelHiddenInChunk(x - 1, y, z)) :
                        (isPositive ? IsVoxelHiddenInChunk(x, y, z + 1) : IsVoxelHiddenInChunk(x, y, z - 1));

                    if (shouldRender)
                    {
                        mask[i, j] = true;
                        types[i, j] = voxels[x, y, z].type;
                    }
                }
            }
        }

        // Greedy meshing
        for (int i = 0; i < (isX ? chunkHeight : chunkSize); i++)
        {
            for (int j = 0; j < (isX ? chunkSize : chunkHeight); j++)
            {
                if (!mask[i, j]) continue;

                Voxel.VoxelType type = types[i, j];
                int width = 1;
                int height = 1;

                // Find width
                while (j + width < (isX ? chunkSize : chunkHeight) && mask[i, j + width] && types[i, j + width] == type)
                {
                    width++;
                }

                // Find height
                bool canExpand = true;
                while (canExpand && i + height < (isX ? chunkHeight : chunkSize))
                {
                    for (int k = 0; k < width; k++)
                    {
                        if (!mask[i + height, j + k] || types[i + height, j + k] != type)
                        {
                            canExpand = false;
                            break;
                        }
                    }
                    if (canExpand) height++;
                }

                // Create the quad
                int x = isX ? slice : j;
                int y = isX ? i : slice;
                int z = isX ? j : i;
                Vector3 pos = new Vector3(x, y, z);

                if (isX)
                {
                    if (isPositive)
                        AddGreedyQuad(pos, height, width, 3, type); // Right face
                    else
                        AddGreedyQuad(pos, height, width, 2, type); // Left face
                }
                else
                {
                    if (isPositive)
                        AddGreedyQuad(pos, width, height, 4, type); // Front face
                    else
                        AddGreedyQuad(pos, width, height, 5, type); // Back face
                }

                // Clear the mask for this rectangle
                for (int k = 0; k < width; k++)
                {
                    for (int l = 0; l < height; l++)
                    {
                        mask[i + l, j + k] = false;
                    }
                }
            }
        }
    }

    private void AddGreedyQuad(Vector3 position, int width, int height, int faceIndex, Voxel.VoxelType type)
    {
        int vertCount = vertices.Count;

        switch (faceIndex)
        {
            case 0: // Top Face
                vertices.Add(new Vector3(position.x, position.y + 1, position.z));
                vertices.Add(new Vector3(position.x, position.y + 1, position.z + height));
                vertices.Add(new Vector3(position.x + width, position.y + 1, position.z + height));
                vertices.Add(new Vector3(position.x + width, position.y + 1, position.z));
                break;
            case 1: // Bottom Face
                vertices.Add(new Vector3(position.x, position.y, position.z));
                vertices.Add(new Vector3(position.x + width, position.y, position.z));
                vertices.Add(new Vector3(position.x + width, position.y, position.z + height));
                vertices.Add(new Vector3(position.x, position.y, position.z + height));
                break;
            case 2: // Left Face
                vertices.Add(new Vector3(position.x, position.y, position.z));
                vertices.Add(new Vector3(position.x, position.y, position.z + height));
                vertices.Add(new Vector3(position.x, position.y + width, position.z + height));
                vertices.Add(new Vector3(position.x, position.y + width, position.z));
                break;
            case 3: // Right Face
                vertices.Add(new Vector3(position.x + 1, position.y, position.z + height));
                vertices.Add(new Vector3(position.x + 1, position.y, position.z));
                vertices.Add(new Vector3(position.x + 1, position.y + width, position.z));
                vertices.Add(new Vector3(position.x + 1, position.y + width, position.z + height));
                break;
            case 4: // Front Face
                vertices.Add(new Vector3(position.x, position.y, position.z + 1));
                vertices.Add(new Vector3(position.x + width, position.y, position.z + 1));
                vertices.Add(new Vector3(position.x + width, position.y + height, position.z + 1));
                vertices.Add(new Vector3(position.x, position.y + height, position.z + 1));
                break;
            case 5: // Back Face
                vertices.Add(new Vector3(position.x + width, position.y, position.z));
                vertices.Add(new Vector3(position.x, position.y, position.z));
                vertices.Add(new Vector3(position.x, position.y + height, position.z));
                vertices.Add(new Vector3(position.x + width, position.y + height, position.z));
                break;
        }

        // Calculate light level for each vertex
        float[] lightLevels = new float[4];
        for (int i = 0; i < 4; i++)
        {
            Vector3 vertPos = vertices[vertCount + i];
            lightLevels[i] = CalculateLightLevel((int)vertPos.x, (int)vertPos.y, (int)vertPos.z, faceIndex);
        }

        // Store block type in color.r and light level in color.a
        for (int i = 0; i < 4; i++)
        {
            colors.Add(new Color((float)type, 0, 0, lightLevels[i]));
        }

        triangles.Add(vertCount);
        triangles.Add(vertCount + 1);
        triangles.Add(vertCount + 2);
        triangles.Add(vertCount);
        triangles.Add(vertCount + 2);
        triangles.Add(vertCount + 3);
    }

    private float CalculateLightLevel(int x, int y, int z, int faceIndex)
    {
        // Get the light level from the voxel
        float lightLevel = GetVoxelLightLevel(x, y, z);

        // Apply ambient occlusion
        float ao = CalculateAmbientOcclusion(x, y, z, faceIndex);
        lightLevel *= ao;

        return lightLevel;
    }

    private float GetVoxelLightLevel(int x, int y, int z)
    {
        // Check if coordinates are within chunk bounds
        if (x >= 0 && x < chunkSize && y >= 0 && y < chunkHeight && z >= 0 && z < chunkSize)
        {
            return voxels[x, y, z].lightLevel;
        }

        // If outside chunk bounds, get from world
        Vector3Int worldPos = new Vector3Int(
            Mathf.FloorToInt(pos.x) + x,
            Mathf.FloorToInt(pos.y) + y,
            Mathf.FloorToInt(pos.z) + z
        );

        Voxel neighborVoxel = World.Instance.GetVoxelInWorld(worldPos);
        return neighborVoxel.lightLevel;
    }

    private float CalculateAmbientOcclusion(int x, int y, int z, int faceIndex)
    {
        float ao = 1.0f;
        int side1, side2, corner;

        switch (faceIndex)
        {
            case 0: // Top face
                side1 = IsVoxelSolid(x - 1, y + 1, z) ? 1 : 0;
                side2 = IsVoxelSolid(x, y + 1, z - 1) ? 1 : 0;
                corner = IsVoxelSolid(x - 1, y + 1, z - 1) ? 1 : 0;
                break;
            case 1: // Bottom face
                side1 = IsVoxelSolid(x - 1, y - 1, z) ? 1 : 0;
                side2 = IsVoxelSolid(x, y - 1, z - 1) ? 1 : 0;
                corner = IsVoxelSolid(x - 1, y - 1, z - 1) ? 1 : 0;
                break;
            case 2: // Left face
                side1 = IsVoxelSolid(x - 1, y, z - 1) ? 1 : 0;
                side2 = IsVoxelSolid(x - 1, y - 1, z) ? 1 : 0;
                corner = IsVoxelSolid(x - 1, y - 1, z - 1) ? 1 : 0;
                break;
            case 3: // Right face
                side1 = IsVoxelSolid(x + 1, y, z - 1) ? 1 : 0;
                side2 = IsVoxelSolid(x + 1, y - 1, z) ? 1 : 0;
                corner = IsVoxelSolid(x + 1, y - 1, z - 1) ? 1 : 0;
                break;
            case 4: // Front face
                side1 = IsVoxelSolid(x - 1, y, z + 1) ? 1 : 0;
                side2 = IsVoxelSolid(x, y - 1, z + 1) ? 1 : 0;
                corner = IsVoxelSolid(x - 1, y - 1, z + 1) ? 1 : 0;
                break;
            case 5: // Back face
                side1 = IsVoxelSolid(x - 1, y, z - 1) ? 1 : 0;
                side2 = IsVoxelSolid(x, y - 1, z - 1) ? 1 : 0;
                corner = IsVoxelSolid(x - 1, y - 1, z - 1) ? 1 : 0;
                break;
            default:
                return 1.0f;
        }

        // Calculate ambient occlusion
        if (side1 + side2 == 2)
        {
            ao = 0.5f;
        }
        else
        {
            ao = 1.0f - (side1 + side2 + corner) * 0.2f;
        }

        return ao;
    }

    private bool IsVoxelSolid(int x, int y, int z)
    {
        // Check if coordinates are within chunk bounds
        if (x >= 0 && x < chunkSize && y >= 0 && y < chunkHeight && z >= 0 && z < chunkSize)
        {
            return voxels[x, y, z].type != Voxel.VoxelType.Air;
        }

        // If outside chunk bounds, get from world
        Vector3Int worldPos = new Vector3Int(
            Mathf.FloorToInt(pos.x) + x,
            Mathf.FloorToInt(pos.y) + y,
            Mathf.FloorToInt(pos.z) + z
        );

        Voxel neighborVoxel = World.Instance.GetVoxelInWorld(worldPos);
        return neighborVoxel.type != Voxel.VoxelType.Air;
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
            return;
        }

        Voxel voxel = voxels[x, y, z];
        if (voxel.isActive)
        {
            // We'll handle face generation in GenerateMesh instead
            return;
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
        // If the coordinates are within the chunk bounds, check normally
        if (x >= 0 && x < chunkSize && y >= 0 && y < chunkHeight && z >= 0 && z < chunkSize)
        {
            return voxels[x, y, z].type == Voxel.VoxelType.Air;
        }

        // If we're outside the chunk bounds, we need to check neighboring chunks
        Vector3Int worldPos = new Vector3Int(
            Mathf.FloorToInt(pos.x) + x,
            Mathf.FloorToInt(pos.y) + y,
            Mathf.FloorToInt(pos.z) + z
        );

        // Get the voxel from the world
        Voxel neighborVoxel = World.Instance.GetVoxelInWorld(worldPos);
        return neighborVoxel.type == Voxel.VoxelType.Air;
    }

    private Voxel GetVoxelSafe(int x, int y, int z)
    {
        if (x < 0 || x >= chunkSize || y < 0 || y >= chunkHeight || z < 0 || z >= chunkSize)
        {
            return Voxel.CreateDefault(); // Default or inactive voxel
        }
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
        vertices.Clear();
        triangles.Clear();
        uvs.Clear();
        colors.Clear();

        // Regenerate voxel data
        GenerateVoxelData(transform.position);
    }

    public void UpdateAdjacentFaces(Vector3Int direction)
    {
        // Store the current mesh data
        Vector3[] oldVertices = meshFilter.mesh.vertices;
        int[] oldTriangles = meshFilter.mesh.triangles;
        Vector2[] oldUVs = meshFilter.mesh.uv;
        Color[] oldColors = meshFilter.mesh.colors;

        // Clear lists for new mesh data
            vertices.Clear();
            triangles.Clear();
            uvs.Clear();
            colors.Clear();

        // Determine which faces to check based on the direction
        int startX = direction.x == 1 ? chunkSize - 1 : 0;
        int endX = direction.x == -1 ? 1 : chunkSize;
        int startY = direction.y == 1 ? chunkHeight - 1 : 0;
        int endY = direction.y == -1 ? 1 : chunkHeight;
        int startZ = direction.z == 1 ? chunkSize - 1 : 0;
        int endZ = direction.z == -1 ? 1 : chunkSize;

        // Create a HashSet to track which vertices we've updated
        HashSet<int> updatedVertexIndices = new HashSet<int>();

        // Only process voxels on the face that's adjacent to the new chunk
        for (int y = startY; y < endY; y++)
        {
            for (int x = startX; x < endX; x++)
            {
                for (int z = startZ; z < endZ; z++)
                {
                    if (voxels[x, y, z].type != Voxel.VoxelType.Air)
                    {
                        ProcessVoxel(x, y, z);
                    }
                }
            }
        }

        // Create new arrays that combine old and new data
        List<Vector3> combinedVertices = new List<Vector3>();
        List<int> combinedTriangles = new List<int>();
        List<Vector2> combinedUVs = new List<Vector2>();
        List<Color> combinedColors = new List<Color>();

        // Add all old vertices that weren't updated
        for (int i = 0; i < oldVertices.Length; i++)
        {
            if (!updatedVertexIndices.Contains(i))
            {
                combinedVertices.Add(oldVertices[i]);
                combinedUVs.Add(oldUVs[i]);
                combinedColors.Add(oldColors[i]);
            }
        }

        // Add all new vertices
        combinedVertices.AddRange(vertices);
        combinedUVs.AddRange(uvs);
        combinedColors.AddRange(colors);

        // Update triangle indices to match the new vertex positions
        for (int i = 0; i < oldTriangles.Length; i++)
        {
            if (!updatedVertexIndices.Contains(oldTriangles[i]))
            {
                combinedTriangles.Add(oldTriangles[i]);
            }
        }
        combinedTriangles.AddRange(triangles);

        // Update the mesh with the combined data
        if (combinedVertices.Count > 0)
        {
            Mesh mesh = new()
            {
                vertices = combinedVertices.ToArray(),
                triangles = combinedTriangles.ToArray(),
                uv = combinedUVs.ToArray(),
                colors = combinedColors.ToArray()
            };

            mesh.RecalculateNormals();
            meshFilter.mesh = mesh;
            meshCollider.sharedMesh = mesh;
        }
    }
}
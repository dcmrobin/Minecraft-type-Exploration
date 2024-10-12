using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;

[BurstCompile]
public struct GenerateJob : IJob
{
    public bool useVerticalChunks;
    public int chunkHeight;
    public int chunkSize;
    public float frequency;
    public float amplitude;
    //public float lightFalloff;
    public Vector3 chunkWorldPosition;
    public NativeArray<Voxel> voxels;
    public int randInt;
    public int worldSeed;
    //public NativeQueue<Vector3Int> litVoxels;

    public void Execute()
    {
        // Voxel position calculation
        for (int index = 0; index < voxels.Length; index++)
        {
            int x = index % chunkSize;
            int y = (index % (chunkSize * chunkHeight)) / chunkSize;
            int z = index / (chunkSize * chunkHeight);
            int voxelIndex = x + y * chunkSize + z * chunkSize * chunkHeight;

            Vector3 voxelChunkPos = new Vector3(x, y, z);
            float calculatedHeight = Mathf.PerlinNoise(((chunkWorldPosition.x + x) + worldSeed) / frequency, ((chunkWorldPosition.z + z) + worldSeed) / frequency) * amplitude;
            calculatedHeight += useVerticalChunks ? 150 : 0;
    
            Voxel.VoxelType type = Voxel.DetermineVoxelType(voxelChunkPos, calculatedHeight, chunkWorldPosition, useVerticalChunks, randInt, worldSeed);
            voxels[voxelIndex] = new Voxel(new Vector3(x, y, z), type, type != Voxel.VoxelType.Air, 0);
        }

        // Voxel light calculation
        /*for (int x = 0; x < chunkSize; x++)
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

                if (IsInsideChunk(neighbor))
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
                else
                {
                    //
                }
            }
        }*/
    }

    private readonly bool IsInsideChunk(Vector3Int position)
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
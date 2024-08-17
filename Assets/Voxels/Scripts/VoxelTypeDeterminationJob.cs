using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using SimplexNoise;

public struct VoxelTypeDeterminationJob : IJob
{
    public NativeArray<Voxel> voxels;
    public int chunkSize;
    public int chunkHeight;
    public float maxHeight;
    public float noiseScale;
    public Vector3 chunkWorldPosition;

    public void Execute()
    {
        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkHeight; y++)
            {
                for (int z = 0; z < chunkSize; z++)
                {
                    int index = x * chunkSize * chunkHeight + y * chunkSize + z;
                    Vector3 worldPos = chunkWorldPosition + new Vector3(x, y, z);

                    // Calculate noise
                    float noiseValue = Noise.CalcPixel2D((int)worldPos.x, (int)worldPos.z, noiseScale);
                    float normalizedNoiseValue = (noiseValue + 1) / 2;
                    float calculatedHeight = normalizedNoiseValue * maxHeight;

                    // Determine voxel type
                    Voxel.VoxelType type = (y <= calculatedHeight) ? Voxel.VoxelType.Grass : Voxel.VoxelType.Air;

                    // Calculate the position for the voxel
                    Vector3 voxelPosition = new Vector3(x, y, z); // Assuming local position in chunk

                    // Set voxel data
                    voxels[index] = new Voxel(voxelPosition, type, type != Voxel.VoxelType.Air);
                }
            }
        }
    }

}
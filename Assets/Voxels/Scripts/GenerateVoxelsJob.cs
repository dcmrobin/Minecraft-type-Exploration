using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

public struct GenerateVoxelsJob : IJobParallelFor
{
    public int chunkSize;
    public int chunkHeight;
    public Vector3 chunkWorldPosition;
    public float maxHeight;
    [ReadOnly]
    public NativeArray<float> baseNoiseMap;
    [ReadOnly]
    public NativeArray<float> lod1Map;
    [ReadOnly]
    public NativeArray<float> simplexMap;
    [ReadOnly]
    public NativeArray<float> caveMap;
    [ReadOnly]
    public NativeArray<float> mountainCurveValues;
    [ReadOnly]
    public NativeArray<float> mountainBiomeCurveValues;
    public NativeArray<Voxel> voxelsData;
    public void Execute(int index)
    {
        int x = index / (chunkSize * chunkHeight);
        int y = (index % (chunkSize * chunkHeight)) / chunkSize;
        int z = index % chunkSize;
        _ = chunkWorldPosition + new Vector3(x, y, z);
        int mapIndex = x * chunkSize + z;
        _ = baseNoiseMap[mapIndex];
        float lod1 = lod1Map[mapIndex];
        float simplexNoise = simplexMap[x * chunkSize * chunkHeight + y * chunkSize + z]; // 3D index calculation
        float caveNoise = caveMap[x * chunkSize * chunkHeight + y * chunkSize + z];
        float mountainCurve = mountainCurveValues[mapIndex];
        float mountainBiomeCurve = mountainBiomeCurveValues[mapIndex];
        float normalizedNoiseValue = (mountainCurve - simplexNoise + lod1) * 400;
        float calculatedHeight = normalizedNoiseValue * maxHeight;
        calculatedHeight *= mountainBiomeCurve;
        calculatedHeight += 150;
        Voxel.VoxelType type = (y <= calculatedHeight + 1) ? Voxel.VoxelType.Grass : Voxel.VoxelType.Air;
        if (y < calculatedHeight - 2)
        {
            type = Voxel.VoxelType.Stone;
        }
        if (type == Voxel.VoxelType.Air && y < 3)
        {
            type = Voxel.VoxelType.Grass;
        }

        if (caveNoise > 0.45 && chunkWorldPosition.y + y <= (100 + (caveNoise * 20)) || caveNoise > 0.8 && chunkWorldPosition.y + y > (100 + (caveNoise * 20)))
        {
            type = Voxel.VoxelType.Air;
        }
        Vector3 voxelPosition = new(x, y, z);
        voxelsData[index] = new Voxel(voxelPosition, type, type != Voxel.VoxelType.Air);
    }
}
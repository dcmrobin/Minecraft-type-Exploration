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
    public NativeArray<float> overhangsMap;
    [ReadOnly]
    public NativeArray<float> mountainCurveValues;
    [ReadOnly]
    public NativeArray<float> biomeCurveValues;
    public NativeArray<Voxel> voxelsData;
    public void Execute(int index)
    {
        int x = index / (chunkSize * chunkHeight);
        int y = (index / chunkSize) % chunkHeight;
        int z = index % chunkSize;
        Vector3 worldPos = chunkWorldPosition + new Vector3(x, y, z);
        int mapIndex = x * chunkSize + z;
        float baseNoise = baseNoiseMap[mapIndex];
        float lod1 = lod1Map[mapIndex];
        float overhangsNoise = overhangsMap[mapIndex];
        float mountainCurve = mountainCurveValues[mapIndex];
        float biomeCurve = biomeCurveValues[mapIndex];
        float normalizedNoiseValue = (mountainCurve - overhangsNoise + lod1) * 400;
        float calculatedHeight = normalizedNoiseValue * maxHeight;
        calculatedHeight *= biomeCurve;
        Voxel.VoxelType type = (y <= calculatedHeight + 1) ? Voxel.VoxelType.Grass : Voxel.VoxelType.Air;
        if (y < calculatedHeight - 2)
        {
            type = Voxel.VoxelType.Stone;
        }
        if (type == Voxel.VoxelType.Air && y < 3)
        {
            type = Voxel.VoxelType.Grass;
        }
        Vector3 voxelPosition = new Vector3(x, y, z);
        voxelsData[index] = new Voxel(voxelPosition, type, type != Voxel.VoxelType.Air);
    }
}
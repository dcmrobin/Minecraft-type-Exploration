using Unity.Collections;
using Unity.Jobs;

public struct FixGrassJob : IJobParallelFor
{
    public int chunkSize;
    public int chunkHeight;
    [ReadOnly]
    public NativeArray<Voxel> voxelsData;
    public NativeArray<Voxel> updatedVoxelsData;
    public void Execute(int index)
    {
        _ = index / (chunkSize * chunkHeight);
        int y = (index / chunkSize) % chunkHeight;
        _ = index % chunkSize;
        Voxel voxel = voxelsData[index];
        
        if (voxel.type == Voxel.VoxelType.Grass)
        {
            // Check if there is a voxel directly above this one
            if (y < chunkHeight - 1)
            {
                int aboveIndex = index + chunkSize; // Move one level up
                Voxel aboveVoxel = voxelsData[aboveIndex];
                // If the voxel above is not air, convert this voxel to dirt
                if (aboveVoxel.type != Voxel.VoxelType.Air)
                {
                    voxel.type = Voxel.VoxelType.Dirt;
                    voxel.isActive = true;
                }
            }
        }
        updatedVoxelsData[index] = voxel;
    }
}
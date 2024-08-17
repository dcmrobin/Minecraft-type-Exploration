public class Voxel
{
    public enum VoxelType { Air, Dirt, Grass, Stone, Bedrock }

    public VoxelType Type { get; private set; }

    public Voxel(VoxelType type)
    {
        Type = type;
    }
}

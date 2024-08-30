using UnityEngine;

// Define a simple Voxel struct
public struct Voxel
{
  public Vector3 position;
  public VoxelType type; // Using the VoxelType enum
  public bool isActive;
  public float globalLightPercentage;
  public float transparency;
  public enum VoxelType
  {
      Air,    // Represents empty space
      Grass,  // Represents grass block
      Dirt,
      Stone,  // Represents stone block
      // Add more types as needed
  }
  public Voxel(Vector3 position, VoxelType type, bool isActive = true)
  {
      this.position = position;
      this.type = type;
      this.isActive = isActive;
      this.globalLightPercentage = 0;
      this.transparency = type == VoxelType.Air ? 1 : 0;
  }
}
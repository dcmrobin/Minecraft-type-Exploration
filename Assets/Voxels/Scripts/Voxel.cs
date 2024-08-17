using UnityEngine;

// Define a simple Voxel struct
public struct Voxel
{
  public Vector3 position;
  public VoxelType type; // Using the VoxelType enum
  public bool isActive;
  public enum VoxelType
  {
      Air,    // Represents empty space
      Grass,  // Represents grass block
      Stone,  // Represents stone block
      // Add more types as needed
  }
  public Voxel(Vector3 position, VoxelType type, bool isActive = true)
  {
      this.position = position;
      this.type = type;
      this.isActive = isActive;
  }
}
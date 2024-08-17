using UnityEngine;

// Define a simple Voxel struct
public struct Voxel
{
  public Vector3 position;
  public Color color;
  public bool isActive;
  public Voxel(Vector3 position, Color color, bool isActive = true)
  {
    this.position = position;
    this.color = color;
    this.isActive = isActive;
  }
}
using UnityEngine;

public struct Voxel
{
    public Vector3 position;
    public VoxelType type;
    public bool isActive;
    public float globalLightPercentage;
    public float transparency;
    public bool visibility;

    public enum VoxelType
    {
        Air,    // Represents empty space
        Grass,  // Represents grass block
        Dirt,
        Stone,  // Represents stone block
        // Add more types as needed
    }

    // Face checks to determine neighbor positions (left, right, top, bottom, front, back)
    public static readonly Vector3[] FaceChecks = {
        new Vector3(0, 1, 0),   // Top
        new Vector3(0, -1, 0),  // Bottom
        new Vector3(-1, 0, 0),  // Left
        new Vector3(1, 0, 0),   // Right
        new Vector3(0, 0, 1),   // Front
        new Vector3(0, 0, -1)   // Back
    };

    // Vertices for each face of a cube (voxel)
    private static readonly Vector3[] VoxelVerts = {
        new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0), // Back face
        new Vector3(0, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1), // Front face
    };

    // Quad triangles for each face of a cube (voxel)
    private static readonly int[][] VoxelTris = {
        new int[] { 0, 3, 1, 2 }, // Back
        new int[] { 5, 6, 4, 7 }, // Front
        new int[] { 3, 7, 2, 6 }, // Top
        new int[] { 1, 5, 0, 4 }, // Bottom
        new int[] { 4, 7, 0, 3 }, // Left
        new int[] { 1, 2, 5, 6 }  // Right
    };

    // UVs for each face based on voxel type (this is a simplified version)
    private static readonly Vector2[] FaceUVs = {
        new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1)
    };

    public Voxel(Vector3 position, VoxelType type, bool isActive = true)
    {
        this.position = position;
        this.type = type;
        this.isActive = isActive;
        this.globalLightPercentage = 0;
        this.transparency = type == VoxelType.Air ? 1 : 0;
        this.visibility = type != VoxelType.Air;
    }

    // Returns the vertices for a specific face
    public static Vector3[] VoxelVertsFace(int faceIndex, Vector3 voxelPosition)
    {
        return new Vector3[] {
            voxelPosition + VoxelVerts[VoxelTris[faceIndex][0]],
            voxelPosition + VoxelVerts[VoxelTris[faceIndex][1]],
            voxelPosition + VoxelVerts[VoxelTris[faceIndex][2]],
            voxelPosition + VoxelVerts[VoxelTris[faceIndex][3]]
        };
    }

    // Returns UV coordinates for the specified face based on voxel type
    public static Vector2[] GetFaceUVs(VoxelType type, int faceIndex)
    {
        // In a more complex implementation, different UVs would be returned based on voxel type.
        // This implementation uses the same UVs for simplicity.
        return new Vector2[] {
            FaceUVs[0],
            FaceUVs[1],
            FaceUVs[2],
            FaceUVs[3]
        };
    }
}

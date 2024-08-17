using System.Collections.Generic;
using UnityEngine;

public class Chunk : MonoBehaviour
{
    public int width = 16;
    public int height = 16;
    public int depth = 16;

    private Voxel[,,] voxels;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Mesh mesh;

    void Start()
    {
        meshFilter = gameObject.GetComponent<MeshFilter>() == false ? gameObject.AddComponent<MeshFilter>() : gameObject.GetComponent<MeshFilter>();
        meshRenderer = gameObject.GetComponent<MeshRenderer>() == false ? gameObject.AddComponent<MeshRenderer>() : gameObject.GetComponent<MeshRenderer>();

        meshRenderer.material = meshRenderer.material == null ? new Material(Shader.Find("Standard")) : meshRenderer.material;

        InitializeChunk();
        GenerateMesh();
    }

    void InitializeChunk()
    {
        voxels = new Voxel[width, height, depth];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int z = 0; z < depth; z++)
                {
                    Voxel.VoxelType type = Voxel.VoxelType.Air;

                    if (y == 0)
                        type = Voxel.VoxelType.Bedrock;
                    else if (y < 4)
                        type = Voxel.VoxelType.Dirt;
                    else if (y == 4)
                        type = Voxel.VoxelType.Grass;

                    voxels[x, y, z] = new Voxel(type);
                }
            }
        }
    }

    void GenerateMesh()
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector2> uvs = new List<Vector2>();

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int z = 0; z < depth; z++)
                {
                    if (voxels[x, y, z].Type != Voxel.VoxelType.Air)
                    {
                        if (IsFaceVisible(x, y, z, Vector3.forward))  // Front face
                            AddFace(vertices, triangles, uvs, new Vector3(x, y, z), Vector3.forward);
                        if (IsFaceVisible(x, y, z, Vector3.back))     // Back face
                            AddFace(vertices, triangles, uvs, new Vector3(x, y, z), Vector3.back);
                        if (IsFaceVisible(x, y, z, Vector3.up))       // Top face
                            AddFace(vertices, triangles, uvs, new Vector3(x, y, z), Vector3.up);
                        if (IsFaceVisible(x, y, z, Vector3.down))     // Bottom face
                            AddFace(vertices, triangles, uvs, new Vector3(x, y, z), Vector3.down);
                        if (IsFaceVisible(x, y, z, Vector3.left))     // Left face
                            AddFace(vertices, triangles, uvs, new Vector3(x, y, z), Vector3.left);
                        if (IsFaceVisible(x, y, z, Vector3.right))    // Right face
                            AddFace(vertices, triangles, uvs, new Vector3(x, y, z), Vector3.right);
                    }
                }
            }
        }

        mesh = new Mesh();
        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.RecalculateNormals();

        meshFilter.mesh = mesh;
    }

    bool IsFaceVisible(int x, int y, int z, Vector3 direction)
    {
        int nx = x + (int)direction.x;
        int ny = y + (int)direction.y;
        int nz = z + (int)direction.z;

        if (nx < 0 || nx >= width || ny < 0 || ny >= height || nz < 0 || nz >= depth)
            return true;  // Face on chunk boundary is visible

        return voxels[nx, ny, nz].Type == Voxel.VoxelType.Air;
    }

    void AddFace(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs, Vector3 position, Vector3 normal)
    {
        int vertexIndex = vertices.Count;

        Vector3 right = Vector3.Cross(normal, Vector3.up);
        Vector3 up = Vector3.Cross(normal, right);

        if (normal == Vector3.up || normal == Vector3.down)
        {
            right = Vector3.right;
            up = Vector3.forward;
        }

        vertices.Add(position + (normal - right - up) * 0.5f);
        vertices.Add(position + (normal + right - up) * 0.5f);
        vertices.Add(position + (normal + right + up) * 0.5f);
        vertices.Add(position + (normal - right + up) * 0.5f);

        triangles.Add(vertexIndex);
        triangles.Add(vertexIndex + 2);
        triangles.Add(vertexIndex + 1);
        triangles.Add(vertexIndex);
        triangles.Add(vertexIndex + 3);
        triangles.Add(vertexIndex + 2);

        uvs.Add(new Vector2(0, 0));
        uvs.Add(new Vector2(1, 0));
        uvs.Add(new Vector2(1, 1));
        uvs.Add(new Vector2(0, 1));
    }
}

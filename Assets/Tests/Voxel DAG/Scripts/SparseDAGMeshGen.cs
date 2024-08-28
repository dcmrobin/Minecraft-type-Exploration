using System.Collections.Generic;
using UnityEngine;

public class SparseDAGMeshGen : MonoBehaviour
{
    // cubeVertices and cubeNormals are courtesy of dentedPixel's ProceduralCube.cs on GitHub:
    // https://gist.github.com/dentedpixel/a04e9145add77bab71f5c066f3530749
    private static Vector3[] cubeVertices = new Vector3[]{
        // Front Side
        new Vector3(0f, 0f, 0f),
        new Vector3(1f, 0f, 0f),
        new Vector3(0f, 1f, 0f),
        new Vector3(1f, 1f, 0f),

        // Right Side
        new Vector3(1f, 0f, 0f),
        new Vector3(1f, 0f, 1f),
        new Vector3(1f, 1f, 0f),
        new Vector3(1f, 1f, 1f),

        // Back Side
        new Vector3(1f, 0f, 1f),
        new Vector3(0f, 0f, 1f),
        new Vector3(1f, 1f, 1f),
        new Vector3(0f, 1f, 1f),

        // Left Side
        new Vector3(0f, 1f, 0f),
        new Vector3(0f, 1f, 1f),
        new Vector3(0f, 0f, 0f),
        new Vector3(0f, 0f, 1f),

        // Top Side
        new Vector3(0f, 1f, 0f),
        new Vector3(1f, 1f, 0f),
        new Vector3(0f, 1f, 1f),
        new Vector3(1f, 1f, 1f),

        // Bottom Side
        new Vector3(1f, 0f, 0f),
        new Vector3(0f, 0f, 0f),
        new Vector3(1f, 0f, 1f),
        new Vector3(0f, 0f, 1f),
    };

    private static Vector3[] cubeNormals = new Vector3[]{
        // Front Side
        new Vector3(0f, 0f, 1f),
        new Vector3(0f, 0f, 1f),
        new Vector3(0f, 0f, 1f),
        new Vector3(0f, 0f, 1f),

        // Right Side
        new Vector3(0f, 1f, 0f),
        new Vector3(0f, 1f, 0f),
        new Vector3(0f, 0f, -1f),
        new Vector3(0f, 0f, -1f),

        // Back Side
        new Vector3(0f, 1f, 0f),
        new Vector3(0f, 1f, 0f),
        new Vector3(0f, 0f, -1f),
        new Vector3(0f, 0f, -1f),

        // Left Side
        new Vector3(0f, -1f, 0f),
        new Vector3(0f, -1f, 0f),
        new Vector3(0f, -1f, 0f),
        new Vector3(0f, -1f, 0f),

        // Top Side
        new Vector3(-1f, 0f, 0f),
        new Vector3(-1f, 0f, 0f),
        new Vector3(-1f, 0f, 0f),
        new Vector3(-1f, 0f, 0f),

        // Bottom Side
        new Vector3(1f, 0f, 0f),
        new Vector3(1f, 0f, 0f),
        new Vector3(1f, 0f, 0f),
        new Vector3(1f, 0f, 0f),
    };

    private List<Vector3> vertices = new List<Vector3>();
    private List<int> triangles = new List<int>();
    private List<Vector2> uvs = new List<Vector2>();
    private List<Vector3> normals = new List<Vector3>();
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;

    public Material VoxelMat;

    public static SparseDAGMeshGen Instance { get; private set; }

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    public Mesh GenerateMesh(VoxelNode rootNode, float size)
    {
        TraverseSVDAG(rootNode, Vector3.zero, size);
        Mesh mesh = CreateMesh();
        return mesh;
    }

    private void TraverseSVDAG(VoxelNode node, Vector3 position, float size)
    {
        if (node == null) 
            return;

        if (node.IsLeaf)
        {
            // If the node is a leaf and it's filled, create mesh data
            if (node.Value == 1)
                AddVoxelMeshData(position, size);
        }
        else
        {
            // If the node is not a leaf, recursively traverse its children
            float childSize = size * 0.5f;

            for (int i = 0; i < 8; i++)
            {
                Vector3 childPosition = SparseVoxelDAG.Instance.CalculateChildPosition(position, childSize, i);
                TraverseSVDAG(node.Children[i], childPosition, childSize);
            }
        }
    }

    private void AddVoxelMeshData(Vector3 position, float size)
    {
        // Code adapted from dentedPixel's ProceduralCube.cs on GitHub - https://gist.github.com/dentedpixel/a04e9145add77bab71f5c066f3530749
        for (int i = 0; i < 6; i++)
        {
            int i4 = i * 4;
            int vertexStartIndex = vertices.Count;
            vertices.Add(position + cubeVertices[i4] * size);
            vertices.Add(position + cubeVertices[i4 + 1] * size);
            vertices.Add(position + cubeVertices[i4 + 2] * size);
            vertices.Add(position + cubeVertices[i4 + 3] * size);
            normals.Add(cubeNormals[i4]);
            normals.Add(cubeNormals[i4 + 1]);
            normals.Add(cubeNormals[i4 + 2]);
            normals.Add(cubeNormals[i4 + 3]);
            uvs.Add(new(0, 0));
            uvs.Add(new(1, 0));
            uvs.Add(new(0, 1));
            uvs.Add(new(1, 1));
            triangles.Add(vertexStartIndex);
            triangles.Add(vertexStartIndex + 2);
            triangles.Add(vertexStartIndex + 1);
            triangles.Add(vertexStartIndex + 2);
            triangles.Add(vertexStartIndex + 3);
            triangles.Add(vertexStartIndex + 1);
        }
    }

    private Mesh CreateMesh()
    {
        Mesh mesh = new Mesh();
        Debug.Log("vertices: " + vertices.Count);
        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.normals = normals.ToArray(); 
        mesh.uv = uvs.ToArray();
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        meshFilter.mesh = mesh;
        meshRenderer.material = VoxelMat;
        return mesh;
    }
}
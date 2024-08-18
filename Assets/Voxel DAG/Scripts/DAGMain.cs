using UnityEngine;

public class DAGMain : MonoBehaviour
{
    VoxelNode voxels;
    Mesh voxelMesh;

    void Start()
    {
        voxels = SparseVoxelDAG.Instance.InitializeVoxels();
        voxelMesh = SparseDAGMeshGen.Instance.GenerateMesh(voxels, SparseVoxelDAG.Instance.gridDepth);
    }
}
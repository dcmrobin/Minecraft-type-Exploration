using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct Structure
{
    public Voxel[,,] voxelList;
    public Vector3Int originPos;
    public enum StructureList {Test}
    public StructureList structures;

    public Structure(Voxel[,,] voxels, Vector3Int structureOriginPos, StructureList list)
    {
        this.voxelList = voxels;
        this.structures = list;
        this.originPos = structureOriginPos;
    }

    public void GenerateStructure()
    {
        switch (structures)
        {
            case StructureList.Test:
                voxelList[originPos.x, originPos.y, originPos.z] = Voxel.Create(Voxel.VoxelType.Stone, originPos);
                break;
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class AABB
{
    public Vector3 center = Vector3.zero;
    public Vector3[] cornerPoints = new Vector3[8];
    public Vector3 size = Vector3.zero;
}

[Serializable]
public class HouseObject
{
    // individual object
    public string objName;
    public string objIndex;
    public string objCategory;
    public string objInWhichFloor;

    public Vector3 objPos;
    public Quaternion objRot;

    public AABB aabb = new AABB();
    public Vector3[] obb = new Vector3[8];


    public HouseObject(string objName)
    {
        this.objName = objName;
    }

    // setters
    public void SetObjIndex(string objIndex)
    {
        this.objIndex = objIndex;
    }
    public void SetObjCategoryIndex(string objCategory)
    {
        this.objCategory = objCategory;
    }

    public void SetObjInWhichFloor(string objInWhichFloor)
    {
        this.objInWhichFloor = objInWhichFloor;
    }


    public void SetObjPos(Vector3 pos)
    {
        this.objPos = pos;
    }

    public void SetObjRot(Quaternion rot)
    {
        this.objRot = rot;
    }

    public void SetAABB(Vector3 center, Vector3[] cornerPoints, Vector3 size)
    {
        aabb.center = center;
        aabb.cornerPoints = cornerPoints;
        aabb.size = size;

    }

    public void SetOBB(Vector3[] cornerPoints)
    {
        obb = cornerPoints;
    }


}

[Serializable]
public class HouseWall
{
    public string wallName;
    public string wallIndex;

    public Vector3 wallPos;
    public Quaternion wallRot;

    public Vector3[] wallCornerPoints = new Vector3[4];


    public HouseWall(string wallName)
    {
        this.wallName = wallName;
    }

    public void SetWallIndex(string wallIndex)
    {
        this.wallIndex = wallIndex;
    }

    public void SetWallPos(Vector3 pos)
    {
        this.wallPos = pos;
    }

    public void SetWallRot(Quaternion rot)
    {
        this.wallRot = rot;
    }

    public void SetWallCornerPoints(Vector3[] cornerPoints)
    {
        wallCornerPoints = cornerPoints;
    }

}


[Serializable]
public class HouseFloor
{
    public string floorName;
    public string floorIndex;

    public Vector3 floorPos;
    public Quaternion floorRot;

    public List<Vector3> floorCornerPoints = new List<Vector3>();
    //public int[] floorTriangles = new int[];

    public HouseFloor(string floorName)
    {
        this.floorName = floorName;
    }

    public void SetFloorIndex(string floorIndex)
    {
        this.floorIndex = floorIndex;
    }

    public void SetFloorPos(Vector3 pos)
    {
        this.floorPos = pos;
    }

    public void setFloorRot(Quaternion rot)
    {
        this.floorRot = rot;
    }

    public void setFloorCornerPoints(List<Vector3> cornerPoints)
    {
        this.floorCornerPoints = cornerPoints;
    }


}


[Serializable]
public class HouseCeiling
{
    public string ceilingName;
    public string ceilingIndex;

    public Vector3 ceilingPos;
    public Quaternion ceilingRot;

    public Vector3[] ceilingCornerPoints = new Vector3[4];
    //public int[] ceilingTriangles = new int[];

    public HouseCeiling(string ceilingName)
    {
        this.ceilingName = ceilingName;
    }

    public void SetCeilingIndex(string ceilingIndex)
    {
        this.ceilingIndex = ceilingIndex;
    }

    public void SetCeilingPos(Vector3 pos)
    {
        this.ceilingPos = pos;
    }

    public void SetCeilingRot(Quaternion rot)
    {
        this.ceilingRot = rot;
    }

    public void SetCeilingCornerPoints(Vector3[] cornerPoints)
    {
        this.ceilingCornerPoints = cornerPoints;
    }


}



[Serializable]
public class House
{
    public string houseName;


    // objects
    public List<HouseObject> allHouseObjects = new List<HouseObject>();
    public List<HouseWall> allHouseWalls = new List<HouseWall>();
    public List<HouseFloor> allHouseFloors = new List<HouseFloor>();
    public List<HouseCeiling> allHouseCeilings = new List<HouseCeiling>();


    // initialization
    public House(string houseName)
    {
        this.houseName = houseName;
    }

    public void AddObjects(HouseObject houseObject)
    {
        allHouseObjects.Add(houseObject);

    }

    public void AddWalls(HouseWall houseWall)
    {
        allHouseWalls.Add(houseWall);

    }

    public void AddFloors(HouseFloor houseFloor)
    {
        allHouseFloors.Add(houseFloor);
    }
    
    public void AddCeilings(HouseCeiling houseCeiling)
    {
        allHouseCeilings.Add(houseCeiling);
    }


    public void UpdateHouseName(string updateHouseName)
    {
        this.houseName = updateHouseName;
    }

}

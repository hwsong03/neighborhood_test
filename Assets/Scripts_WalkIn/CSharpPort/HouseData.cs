using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

// Mirrors datapreproc/jsonToHouse.py's House/HouseObject/HouseWall/HouseFloor classes.
// Loads the same house_000.json / house_001.json / house_002.json scan data.

public class Vec3Data
{
    public double x;
    public double y;
    public double z;
}

public class QuatData
{
    public float x;
    public float y;
    public float z;
    public float w;
}

public class AabbData
{
    public Vec3Data center;
    public List<Vec3Data> cornerPoints;
    public Vec3Data size;
}

public class HouseObjectData
{
    public string objName;
    public string objIndex;
    public string objCategory;
    public string objInWhichFloor;
    public Vec3Data objPos;
    public QuatData objRot;
    public AabbData aabb;
    public List<Vec3Data> obb;
}

public class HouseWallData
{
    public string wallName;
    public string wallIndex;
    public Vec3Data wallPos;
    public QuatData wallRot;
    public List<Vec3Data> wallCornerPoints;
}

public class HouseFloorData
{
    public string floorName;
    public string floorIndex;
    public Vec3Data floorPos;
    public QuatData floorRot;
    public List<Vec3Data> floorCornerPoints;
}

public class HouseData
{
    public string houseName;
    public List<HouseObjectData> allHouseObjects;
    public List<HouseWallData> allHouseWalls;
    public List<HouseFloorData> allHouseFloors;
}

public static class HouseLoader
{
    // index 0 -> house_000.json, 1 -> house_001.json, 2 -> house_002.json
    // matches Python's jsonToHouse_selective() default folder (data/scannet/), sorted filenames
    public static HouseData LoadHouseByIndex(int index)
    {
        string fileName = $"house_{index:D3}.json";
        string path = Path.Combine(Application.streamingAssetsPath, "HouseData", fileName);
        string json = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<HouseData>(json);
    }
}

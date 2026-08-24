using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System;
using System.Linq;


public class TempInfo
{
    public string type;
    public List<List<List<float>>> coordinates;
    // list 갯수 맞춰서 뽑아주면 되나? 


    public static TempInfo CreateFromJSON(string jsonString)
    {
        return JsonUtility.FromJson<TempInfo>(jsonString);
    }
}


public class ObjectInfo
{
    public string houseName;
    public List<string> objectName;
}


public class SharedSpacePoints
{
    public List<Vector3> exteriorPoints = new List<Vector3>();
    // num of interior
    public List<List<Vector3>> interiorPoints = new List<List<Vector3>>();

}


public class DataFromPython
{
    public List<float> house_0;
    public List<float> house_1;
    public List<float> house_2;
    public List<List<object>> objects;
    public List<object> sharedspace;

    public class SharedSpace
    {
        public string type;
        public List<List<float>> coordinates;
        public float xcentroid;
        public float ycentroid;
    }

    public SharedSpace savesharedspace()
    {
        if (sharedspace == null || sharedspace.Count < 4)
        {
            Debug.LogError("Sharedspace data is incomplete.");
            return null;
        }

        SharedSpace sharedSpace = new SharedSpace
        {
            type = sharedspace[0] as string,
            coordinates = JsonConvert.DeserializeObject<List<List<float>>>(sharedspace[1].ToString()),
            xcentroid = Convert.ToSingle(sharedspace[2]),
            ycentroid = Convert.ToSingle(sharedspace[3])
        };

        return sharedSpace;
    }


}


public class AlignedSpace : MonoBehaviour
{
    public void DrawLinePolygon(GameObject addToWhichGameobj, string filename, Vector3 currentHouseWorldPos)
    {

        // read polygon info
        //string filename = Application.dataPath + "/Tayoptoutput/mergedPolygon.json";
        string jsonToRead = File.ReadAllText(filename);
        TempInfo temp;// = TempInfo.CreateFromJSON(jsonToRead);
        temp = JsonConvert.DeserializeObject<TempInfo>(jsonToRead);

        List<List<List<float>>> allcoords = temp.coordinates;
        int numOfPolygon = allcoords.Count; // 이거 갯수만큼 polygon이 있는거



        GameObject eachPolygon = null;

        for (int i = 0; i < numOfPolygon; i++)
        {
            eachPolygon = new GameObject("eachPolygon_" + i.ToString());
            eachPolygon.transform.parent = addToWhichGameobj.transform;


            List<List<float>> eachcoords = new List<List<float>>();
            eachcoords = allcoords[i]; // polygon 하나를 이루는 점의 길이 


            Vector3[] points = new Vector3[eachcoords.Count + 1];
            for (int currentPoint = 0; currentPoint < eachcoords.Count; currentPoint++)
            {
                Vector3 tempPoint = new Vector3(Mathf.Round(eachcoords[currentPoint][0] * 1000.0f) * 0.001f, 0.1f, Mathf.Round(eachcoords[currentPoint][1] * 1000.0f) * 0.001f);
                tempPoint = tempPoint + currentHouseWorldPos;
                points[currentPoint] = tempPoint;
                //Debug.Log(tempPoint);

            }

            points[eachcoords.Count] = points[0];


            LineRenderer lineRenderer = new LineRenderer();
            lineRenderer = eachPolygon.AddComponent<LineRenderer>();
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));


            //lineRenderer.startColor = new Color(0f / 255f, 167f / 255f, 141f / 255f, 1f);
            //lineRenderer.endColor = new Color(0f / 255f, 167f / 255f, 141f / 255f, 1f);
            lineRenderer.startColor = new Color(241f / 255f, 189f / 255f, 235f / 255f, 0.8f);
            lineRenderer.endColor = new Color(241f / 255f, 189f / 255f, 235f / 255f, 0.8f);

            lineRenderer.widthMultiplier = 0.015f;

            lineRenderer.positionCount = eachcoords.Count + 1;
            lineRenderer.SetPositions(points);



        }

    }

    public List<Vector3> GetAnchorOutpoints(GameObject addToWhichGameobj, string filename, Vector3 currentHouseWorldPos)
    {
        List<Vector3> outPoints = new List<Vector3>();


        // read polygon info
        //string filename = Application.dataPath + "/Tayoptoutput/mergedPolygon.json";
        string jsonToRead = File.ReadAllText(filename);


        // ---------- original ---------- //
        TempInfo temp;// = TempInfo.CreateFromJSON(jsonToRead);
        temp = JsonConvert.DeserializeObject<TempInfo>(jsonToRead);

        List<List<List<float>>> allcoords = temp.coordinates;
        int numOfPolygon = allcoords.Count; // 이거 갯수만큼 polygon이 있는거
        // ---------- original ---------- //



        GameObject eachPolygon = null;

        for (int i = 0; i < numOfPolygon; i++)
        {
            eachPolygon = new GameObject("eachPolygon_" + i.ToString());
            eachPolygon.transform.parent = addToWhichGameobj.transform;


            List<List<float>> eachcoords = new List<List<float>>();
            eachcoords = allcoords[i]; // polygon 하나를 이루는 점의 길이 


            Vector3[] points = new Vector3[eachcoords.Count + 1];
            for (int currentPoint = 0; currentPoint < eachcoords.Count; currentPoint++)
            {
                Vector3 tempPoint = new Vector3(Mathf.Round(eachcoords[currentPoint][0] * 1000.0f) * 0.001f, 0.02f, Mathf.Round(eachcoords[currentPoint][1] * 1000.0f) * 0.001f);
                tempPoint = tempPoint + currentHouseWorldPos;
                points[currentPoint] = tempPoint;
                //Debug.Log(tempPoint);

            }

            points[eachcoords.Count] = points[0];


            LineRenderer lineRenderer = new LineRenderer();
            lineRenderer = eachPolygon.AddComponent<LineRenderer>();
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));

            lineRenderer.startColor = new Color(241f / 255f, 189f / 255f, 235f / 255f, 0.8f);
            lineRenderer.endColor = new Color(241f / 255f, 189f / 255f, 235f / 255f, 0.8f);

            lineRenderer.widthMultiplier = 0.015f;

            lineRenderer.positionCount = eachcoords.Count + 1;
            lineRenderer.SetPositions(points);



            if (i == 0)
            {
                outPoints = points.ToList();
            }


        }

        return outPoints;
    }

    public SharedSpacePoints GetAnchorSpacepoints(GameObject addToWhichGameobj, string filename, Vector3 currentHouseWorldPos)
    {
        SharedSpacePoints sharedspacePoints = new SharedSpacePoints();

        // read polygon 
        string jsonToRead = File.ReadAllText(filename);

        /*
        TempInfo temp;// = TempInfo.CreateFromJSON(jsonToRead);
        temp = JsonConvert.DeserializeObject<TempInfo>(jsonToRead);

        List<List<List<float>>> allcoords = temp.coordinates;
        int numOfPolygon = allcoords.Count; // 이거 갯수만큼 polygon이 있는거
        */


        List<List<List<float>>> allcoords = new List<List<List<float>>>();
        int numOfPolygon;

        List<List<List<float>>> eachpolygon_coords;


        dynamic testing = JsonConvert.DeserializeObject(jsonToRead);
        if (testing["type"] == "MultiPolygon")
        {
            string coordinatesJson = testing["coordinates"].ToString();
            var multipolygon_coordinates = JsonConvert.DeserializeObject<List<List<List<List<float>>>>>(coordinatesJson);
            int numOfOuterPolygon = multipolygon_coordinates.Count;

            for (int k = 0; k < numOfOuterPolygon; k++)
            {
                eachpolygon_coords = multipolygon_coordinates[k];
                int numOfEachPolygon = eachpolygon_coords.Count;

                //Debug.Log(eachpolygon_coords[0]); // 이 단계로 내려오면, polygon 접근이 됨
                for (int numOfEachPolygonCount = 0; numOfEachPolygonCount < numOfEachPolygon; numOfEachPolygonCount++)
                {
                    allcoords.Add(eachpolygon_coords[numOfEachPolygonCount]);
                }
            }
            numOfPolygon = allcoords.Count;

        }
        else // if polygon
        {
            string coordinatesJson = testing["coordinates"].ToString();
            allcoords = JsonConvert.DeserializeObject<List<List<List<float>>>>(coordinatesJson);
            numOfPolygon = allcoords.Count;

        }

        GameObject eachPolygon = null;
        int longest = 0; // 그때의 i를 기억해야함
        int longestIndex = 0;

        for (int i = 0; i < numOfPolygon; i++)
        {
            eachPolygon = new GameObject("eachPolygon_" + i.ToString());
            eachPolygon.transform.parent = addToWhichGameobj.transform;


            List<List<float>> eachcoords = new List<List<float>>();
            eachcoords = allcoords[i]; // polygon 하나를 이루는 점의 길이 

            if (longest < eachcoords.Count)
            {
                longest = eachcoords.Count;
                longestIndex = i;
            }


            Vector3[] points = new Vector3[eachcoords.Count + 1];
            for (int currentPoint = 0; currentPoint < eachcoords.Count; currentPoint++)
            {
                Vector3 tempPoint = new Vector3(Mathf.Round(eachcoords[currentPoint][0] * 1000.0f) * 0.001f, 0.02f, Mathf.Round(eachcoords[currentPoint][1] * 1000.0f) * 0.001f);
                tempPoint = tempPoint + currentHouseWorldPos;
                points[currentPoint] = tempPoint;
                //Debug.Log(tempPoint);

            }

            points[eachcoords.Count] = points[0];


            LineRenderer lineRenderer = new LineRenderer();
            lineRenderer = eachPolygon.AddComponent<LineRenderer>();
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));

            // light pink
            //lineRenderer.startColor = new Color(241f / 255f, 189f / 255f, 235f / 255f, 0.8f);
            //lineRenderer.endColor = new Color(241f / 255f, 189f / 255f, 235f / 255f, 0.8f);

            // 
            lineRenderer.startColor = Color.red;
            lineRenderer.endColor = Color.red;

            lineRenderer.widthMultiplier = 0.015f;

            lineRenderer.positionCount = eachcoords.Count + 1;
            lineRenderer.SetPositions(points);

            // probably longest
            if (i == longestIndex)
            {
                sharedspacePoints.exteriorPoints = points.ToList();
            }
            else
            {
                sharedspacePoints.interiorPoints.Add(points.ToList());
            }


        }

        return sharedspacePoints;
    }


    public SharedSpacePoints DrawSingleSharedSpace(GameObject addToWhichGameobj, Vector3[] points)
    {

        SharedSpacePoints sharedspacePoints = new SharedSpacePoints();
        LineRenderer lineRenderer = new LineRenderer();


        lineRenderer = addToWhichGameobj.AddComponent<LineRenderer>();
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));

        // light pink
        //lineRenderer.startColor = new Color(241f / 255f, 189f / 255f, 235f / 255f, 0.8f);
        //lineRenderer.endColor = new Color(241f / 255f, 189f / 255f, 235f / 255f, 0.8f);


        lineRenderer.startColor = Color.green;
        lineRenderer.endColor = Color.green;

        lineRenderer.widthMultiplier = 0.015f;

        lineRenderer.positionCount = points.Length;
        lineRenderer.SetPositions(points);

        sharedspacePoints.exteriorPoints = points.ToList();

        return sharedspacePoints;
    }


    public Dictionary<string, List<float>> getHouseTrans(string transfilename)
    {

        string transjson = File.ReadAllText(transfilename);
        JObject o = JObject.Parse(transjson);
        Dictionary<string, List<float>> transDict = new Dictionary<string, List<float>>();

        // create dictionary with objects
        foreach (var p in o)
        {
            float[] TransValues = p.Value.ToObject<float[]>();

            List<float> TransList = new List<float>(TransValues);

            foreach (var t in TransValues)
            {
                TransList.Append(t);
            }
            transDict[p.Key] = TransList;
        }

        return transDict;
    }

    public Dictionary<string, List<float>> getGroundTrans(string groundtransfilename)
    {

        string transjson = File.ReadAllText(groundtransfilename);
        JObject o = JObject.Parse(transjson);
        Dictionary<string, List<float>> groundtransDict = new Dictionary<string, List<float>>();

        // create dictionary with objects
        foreach (var p in o)
        {
            float[] TransValues = p.Value.ToObject<float[]>();

            List<float> TransList = new List<float>(TransValues);

            foreach (var t in TransValues)
            {
                TransList.Append(t);
            }
            groundtransDict[p.Key] = TransList;
        }

        return groundtransDict;
    }







}





public class Anchor : MonoBehaviour
{

    public GameObject whichHouse;
    public List<GameObject> otherHouses;

    // initalize drawing class
    public AlignedSpace alignedBaseline = new AlignedSpace();


    [SerializeField]
    void Start()
    {

        /*
        // ---------------------- house 5 --------------------------//
        // create gameobject to attach all anchor space gameobjects
        GameObject allPolygonToDraw = new GameObject("allPolygonToDraw");
        allPolygonToDraw.transform.parent = whichHouse.transform;

        // read anchor
        string filename = Application.dataPath + "/Tayoptoutput/mergedPolygon.json";
        alignedBaseline.DrawLinePolygon(allPolygonToDraw, filename);

        // read trans and save
        string transfilename = Application.dataPath + "/Tayoptoutput/houseTrans.json";
        Dictionary<string, List<float>> transDict = alignedBaseline.getHouseTrans(transfilename);
        List<float> currentHouseTrans = transDict[whichHouse.name];

        // read current house trans
        string byHouse = Application.dataPath + "/Tayoptoutput/objectBy_house_5.json";
        Dictionary<string, List<float>> objByDict = alignedBaseline.getHouseTrans(byHouse);




        // 들어온 key를 잘라서 
        foreach (KeyValuePair<string, List<float>> pair in objByDict)
        {
            //Debug.Log("Key: " + pair.Key + ", Value: " + pair.Value);

            var ind1 = pair.Key.IndexOf('_');
            var ind2 = pair.Key.IndexOf('_', ind1 + 1);
            string houseName = pair.Key.Substring(0, ind2);
            string objectName = pair.Key.Substring(ind2 + 1);


            

            if (houseName != whichHouse.name)
            {
                GameObject currentHouseToSearch = null;
                // 누구를 복사할지 선택한다
                for (int otherHousesCount = 0; otherHousesCount < otherHouses.Count; otherHousesCount++)
                { 
                    if (otherHouses[otherHousesCount].name == houseName)
                    {
                        currentHouseToSearch = otherHouses[otherHousesCount];
                    }
                }

                
                GameObject copyingObject = currentHouseToSearch.gameObject.transform.Find("Objects/" + objectName.ToString()).gameObject;


                // 얘의 origin에서부터의 local transformation을 알아
                GameObject copyedObject = Instantiate(copyingObject);


                // sample pivot을 만들어서, 그 주위로 돌린다
                //copyedObject.transform.Rotate(0, currentHouseTrans[2], 0);
                copyedObject.transform.RotateAround(new Vector3(currentHouseTrans[0], 0, currentHouseTrans[1]), Vector3.up, currentHouseTrans[2]);
                copyedObject.transform.position = new Vector3(pair.Value[0], 0, pair.Value[1]);


                Debug.Log(houseName);
                Debug.Log(objectName);
            }



        }

        */


    }


    // Update is called once per frame
    void Update()
    {

    }
}

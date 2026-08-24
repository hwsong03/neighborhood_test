using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using UnityEngine;

public class ProcessKorea : MonoBehaviour
{

    // read object
    public GameObject korea;
    public GameObject Origin;


    private Renderer[] checkMesh;

    // AABB bounds
    private Renderer[] renderers;
    private Bounds bounds;
    private GameObject AABBCollider;

    // OBB bounds
    private Renderer[] OBBrenderers;
    private Bounds OBBbounds;
    private GameObject OBBCollider;

    // floor 
    private UnityEngine.Mesh floorMesh;

    string SAVE_BASE_PATH = "/Json/";






    void Start()
    {

        House eachHouse = new House(korea.name);

        foreach (Transform child in korea.GetComponentInChildren<Transform>())
        {
            if (child.name.Contains("Objects"))
            {
                int objectCounter = 0;

                foreach (Transform furniture in child.GetComponentInChildren<Transform>())
                {

                    // check whether mesh exists
                    checkMesh = furniture.GetComponentsInChildren<Renderer>();

                    if (checkMesh.Length == 0)
                    {
                        continue;
                    }
                    else
                    {
                        // prepare saver
                        HouseObject tempobject = new HouseObject(furniture.name);

                        // AABB
                        renderers = furniture.GetComponentsInChildren<Renderer>();
                        bounds = renderers[0].bounds;
                        for (var meshNum = 1; meshNum < renderers.Length; ++meshNum)
                            bounds.Encapsulate(renderers[meshNum].bounds);

                        AABBCollider = new GameObject("AABBCollider");
                        AABBCollider.transform.position = Vector3.zero;
                        AABBCollider.transform.rotation = Quaternion.identity;
                        AABBCollider.transform.parent = furniture.transform;

                        BoxCollider furnitureCollider = AABBCollider.AddComponent<BoxCollider>();
                        furnitureCollider.center = bounds.center;
                        furnitureCollider.size = bounds.size;

                        Vector3[] AABBList = GetColliderVertexPos(AABBCollider);
                        //DrawBBox(AABBList);

                        // OBB
                        Quaternion currentObjRot = furniture.transform.localRotation;

                        furniture.transform.localRotation = Quaternion.identity;
                        OBBrenderers = furniture.GetComponentsInChildren<Renderer>();
                        OBBbounds = OBBrenderers[0].bounds;
                        for (var meshNum = 1; meshNum < OBBrenderers.Length; ++meshNum)
                        {
                            OBBbounds.Encapsulate(OBBrenderers[meshNum].bounds);
                        }

                        OBBCollider = new GameObject("OBBCollider");
                        OBBCollider.transform.position = Vector3.zero;
                        OBBCollider.transform.rotation = Quaternion.identity;
                        OBBCollider.transform.parent = furniture.transform;

                        BoxCollider furnitureOBBCollider = OBBCollider.AddComponent<BoxCollider>();
                        furnitureOBBCollider.center = OBBbounds.center;
                        furnitureOBBCollider.size = OBBbounds.size;

                        // rotate to the original direction
                        furniture.transform.localRotation = currentObjRot;

                        Vector3[] OBBList = GetColliderVertexPos(OBBCollider);
                        //DrawBBox(OBBList);


                        // save each furniture wrt origin
                        tempobject.objName = furniture.name;
                        tempobject.objIndex = objectCounter.ToString();

                        if (furniture.name.Contains("chair") || furniture.name.Contains("Couch"))
                        {
                            tempobject.objCategory = 0.ToString();
                        }
                        else if (furniture.name.Contains("table") || furniture.name.Contains("cabinet"))
                        {
                            tempobject.objCategory = 1.ToString();
                        }
                        else { tempobject.objCategory = 2.ToString(); }

                        // object in which room?
                        RaycastHit[] hits = Physics.RaycastAll(AABBCollider.GetComponent<BoxCollider>().center, -Vector3.up, Mathf.Infinity);
                        foreach (RaycastHit hit in hits)
                        {

                            tempobject.SetObjInWhichFloor("room_2");
                        }


                        //Debug.DrawRay(AABBCollider.GetComponent<BoxCollider>().center, furniture.transform.TransformDirection(-Vector3.up) * hit.distance, Color.yellow);

                        tempobject.SetObjPos(Origin.transform.InverseTransformPoint(furniture.transform.position)); // pos
                        tempobject.SetObjRot(Quaternion.Inverse(Origin.transform.rotation) * furniture.transform.rotation);


                        // check facing direction
                        //Vector3 forward = furniture.transform.TransformDirection(Vector3.forward) * 2;
                        //Debug.DrawRay(furniture.transform.position, forward, Color.green, 30);

                        Vector3[] AABBListWRTOrigin = new Vector3[8];
                        for (int boundsLength = 0; boundsLength < AABBList.Length; boundsLength++)
                        {
                            AABBListWRTOrigin[boundsLength] = Origin.transform.InverseTransformPoint(AABBList[boundsLength]);
                        }


                        tempobject.SetAABB(Origin.transform.InverseTransformPoint(bounds.center), AABBListWRTOrigin, bounds.size);

                        Vector3[] OBBListWRTOrigin = new Vector3[8];
                        for (int boundsLength = 0; boundsLength < AABBList.Length; boundsLength++)
                        {
                            OBBListWRTOrigin[boundsLength] = Origin.transform.InverseTransformPoint(OBBList[boundsLength]);
                        }
                        tempobject.SetOBB(OBBListWRTOrigin);


                        // increase object num
                        objectCounter += 1;
                        eachHouse.AddObjects(tempobject);



                    }




                }
            }
            else if (child.name.Contains("Structure"))
            {

                foreach (Transform structure in child.GetComponentsInChildren<Transform>())
                {
                    if (structure.name.Contains("Floor"))
                    {
                        int floorCounter = 0;

                        foreach (Transform floor in structure.GetComponentInChildren<Transform>())
                        {

                            if (floor.childCount == 0)
                                continue;

                            Debug.Log(floor.name);
                            Transform floorMeshObject = floor.GetChild(0);
                            


                            HouseFloor tempfloor = new HouseFloor(floorMeshObject.gameObject.name);
                            tempfloor.SetFloorIndex(floorCounter.ToString());

                            // Transform floor's world position and rotation into Origin-relative space
                            tempfloor.SetFloorPos(Origin.transform.InverseTransformPoint(floorMeshObject.transform.position));
                            tempfloor.setFloorRot(Quaternion.Inverse(Origin.transform.rotation) * floorMeshObject.transform.rotation);

                            floorMesh = floorMeshObject.GetComponent<MeshFilter>().mesh;

                            // Compute bounds-based corner points in local space
                            Vector3 meshCenter = floorMesh.bounds.center;
                            Vector3 meshExtents = floorMesh.bounds.extents;

                            Vector3[] localCorners = new Vector3[4];
                            localCorners[0] = meshCenter + new Vector3(-meshExtents.x, 0, -meshExtents.z);
                            localCorners[1] = meshCenter + new Vector3(meshExtents.x, 0, -meshExtents.z);
                            localCorners[2] = meshCenter + new Vector3(meshExtents.x, 0, meshExtents.z);
                            localCorners[3] = meshCenter + new Vector3(-meshExtents.x, 0, meshExtents.z);

                            // Convert to Origin-relative space
                            List<Vector3> floorCornerListWRTOrigin = new List<Vector3>();
                            foreach (Vector3 corner in localCorners)
                            {
                                Vector3 worldCorner = floor.transform.TransformPoint(corner);
                                Vector3 originRelativeCorner = Origin.transform.InverseTransformPoint(worldCorner);
                                floorCornerListWRTOrigin.Add(originRelativeCorner);
                            }

                            tempfloor.setFloorCornerPoints(floorCornerListWRTOrigin);

                            floorCounter += 1;
                            eachHouse.AddFloors(tempfloor);
                        }


                        /*
                        foreach (Transform floor in structure.GetComponentInChildren<Transform>())
                        {
                            HouseFloor tempfloor = new HouseFloor(floor.gameObject.name);
                            tempfloor.SetFloorIndex(floorCounter.ToString());

                            tempfloor.SetFloorPos(Origin.transform.InverseTransformPoint(floor.transform.position));
                            tempfloor.setFloorRot(Quaternion.Inverse(Origin.transform.rotation) * floor.transform.rotation);


                            floorMesh = floor.GetComponent<MeshFilter>().mesh;

                            List<Vector3> floorCornerListWRTOrigin = new List<Vector3>();
                            for (int boundsLength = 0; boundsLength < floorMesh.vertices.Length; boundsLength++)
                            {
                                floorCornerListWRTOrigin.Add(Origin.transform.InverseTransformPoint(floorMesh.vertices[boundsLength]));
                            }

                            tempfloor.setFloorCornerPoints(floorCornerListWRTOrigin);

                            // mesh triangle도 mesh 생성을 위해 저장해두자 


                            floorCounter += 1;
                            eachHouse.AddFloors(tempfloor);
                        }
                        */

                    }
                    else if (structure.name.Contains("Walls"))
                    {
                        int wallCounter = 0;
                        foreach (Transform wall in structure.GetComponentInChildren<Transform>())
                        {
                            if(wall.childCount == 0)
                                continue;

                            
                            Transform wallMeshObject = wall.GetChild(0);

                            HouseWall tempwall = new HouseWall(wallMeshObject.gameObject.name);

                            Vector3[] wallCornerPoints = new Vector3[8];
                            wallCornerPoints = GetWallVertexPos(wallMeshObject.gameObject);
                            //DrawBBoxLine(wallCornerPoints, wall.gameObject);

                            tempwall.SetWallIndex(wallCounter.ToString());

                            tempwall.SetWallPos(Origin.transform.InverseTransformPoint(wallMeshObject.transform.position));
                            tempwall.SetWallRot(Quaternion.Inverse(Origin.transform.rotation) * wallMeshObject.transform.rotation);

                            Vector3[] wallCornerListWRTOrigin = new Vector3[8];
                            for (int boundsLength = 0; boundsLength < wallCornerPoints.Length; boundsLength++)
                            {
                                wallCornerListWRTOrigin[boundsLength] = Origin.transform.InverseTransformPoint(wallCornerPoints[boundsLength]);
                            }

                            tempwall.SetWallCornerPoints(wallCornerListWRTOrigin);

                            wallCounter++;
                            eachHouse.AddWalls(tempwall);
                        }


                    }
                    else if (structure.name.Contains("Ceiling"))
                    {


                        int ceilingCounter = 0;
                        //foreach (Transform ceiling in structure.GetComponentInChildren<Transform>())
                        foreach (Transform ceiling in structure.GetComponentInChildren<Transform>())
                        {
                            Transform ceilingMeshObject = ceiling.GetChild(0);
                            HouseCeiling tempceiling = new HouseCeiling(ceilingMeshObject.gameObject.name);


                            Vector3[] ceilingCornerPoints = new Vector3[8];
                            ceilingCornerPoints = GetCeilingVertexPos(ceilingMeshObject.gameObject);
                            //DrawBBoxLine(wallCornerPoints, wall.gameObject);

                            tempceiling.SetCeilingIndex(ceilingCounter.ToString());

                            tempceiling.SetCeilingPos(Origin.transform.InverseTransformPoint(ceilingMeshObject.transform.position));
                            tempceiling.SetCeilingRot(Quaternion.Inverse(Origin.transform.rotation) * ceilingMeshObject.transform.rotation);

                            Vector3[] ceilingCornerListWRTOrigin = new Vector3[8];
                            for (int boundsLength = 0; boundsLength < ceilingCornerPoints.Length; boundsLength++)
                            {
                                ceilingCornerListWRTOrigin[boundsLength] = Origin.transform.InverseTransformPoint(ceilingCornerPoints[boundsLength]);
                            }

                            tempceiling.SetCeilingCornerPoints(ceilingCornerListWRTOrigin);

                            ceilingCounter++;
                            eachHouse.AddCeilings(tempceiling);
                        }


                    }
                    else
                    {



                    }

                }

            }


        }



        SaveHouseToJson(eachHouse);





    }

    // Update is called once per frame
    void Update()
    {
        
    }


    public Vector3[] GetColliderVertexPos(GameObject obj)
    {
        BoxCollider b = obj.GetComponent<BoxCollider>(); //retrieves the Box Collider of the GameObject called obj
        Vector3[] vertices = new Vector3[8];
        vertices[0] = obj.transform.TransformPoint(b.center + new Vector3(-b.size.x, -b.size.y, -b.size.z) * 0.5f);
        vertices[1] = obj.transform.TransformPoint(b.center + new Vector3(b.size.x, -b.size.y, -b.size.z) * 0.5f);
        vertices[2] = obj.transform.TransformPoint(b.center + new Vector3(b.size.x, -b.size.y, b.size.z) * 0.5f);
        vertices[3] = obj.transform.TransformPoint(b.center + new Vector3(-b.size.x, -b.size.y, b.size.z) * 0.5f);
        vertices[4] = obj.transform.TransformPoint(b.center + new Vector3(-b.size.x, b.size.y, -b.size.z) * 0.5f);
        vertices[5] = obj.transform.TransformPoint(b.center + new Vector3(b.size.x, b.size.y, -b.size.z) * 0.5f);
        vertices[6] = obj.transform.TransformPoint(b.center + new Vector3(b.size.x, b.size.y, b.size.z) * 0.5f);
        vertices[7] = obj.transform.TransformPoint(b.center + new Vector3(-b.size.x, b.size.y, b.size.z) * 0.5f);

        return vertices;
    }




    public Vector3[] GetWallVertexPos(GameObject obj)
    {
        Vector3[] vertices = new Vector3[8];

        Renderer wallRenderer = obj.GetComponent<Renderer>();
        Bounds bounds = wallRenderer.bounds;

        // transfrom 내 origin 이랑 transform이 뭔가 좀 다른거같음
        vertices[0] = bounds.center + new Vector3(-bounds.size.x, -bounds.size.y, -bounds.size.z) * 0.5f;
        vertices[1] = bounds.center + new Vector3(bounds.size.x, -bounds.size.y, -bounds.size.z) * 0.5f;
        vertices[2] = bounds.center + new Vector3(bounds.size.x, -bounds.size.y, bounds.size.z) * 0.5f;
        vertices[3] = bounds.center + new Vector3(-bounds.size.x, -bounds.size.y, bounds.size.z) * 0.5f;
        vertices[4] = bounds.center + new Vector3(-bounds.size.x, bounds.size.y, -bounds.size.z) * 0.5f;
        vertices[5] = bounds.center + new Vector3(bounds.size.x, bounds.size.y, -bounds.size.z) * 0.5f;
        vertices[6] = bounds.center + new Vector3(bounds.size.x, bounds.size.y, bounds.size.z) * 0.5f;
        vertices[7] = bounds.center + new Vector3(-bounds.size.x, bounds.size.y, bounds.size.z) * 0.5f;


        return vertices;
    }





    public Vector3[] GetCeilingVertexPos(GameObject obj)
    {
        Vector3[] vertices = new Vector3[8];

        Renderer wallRenderer = obj.GetComponent<Renderer>();
        Bounds bounds = wallRenderer.bounds;

        // transfrom 내 origin 이랑 transform이 뭔가 좀 다른거같음
        vertices[0] = bounds.center + new Vector3(-bounds.size.x, -bounds.size.y, -bounds.size.z) * 0.5f;
        vertices[1] = bounds.center + new Vector3(bounds.size.x, -bounds.size.y, -bounds.size.z) * 0.5f;
        vertices[2] = bounds.center + new Vector3(bounds.size.x, -bounds.size.y, bounds.size.z) * 0.5f;
        vertices[3] = bounds.center + new Vector3(-bounds.size.x, -bounds.size.y, bounds.size.z) * 0.5f;
        vertices[4] = bounds.center + new Vector3(-bounds.size.x, bounds.size.y, -bounds.size.z) * 0.5f;
        vertices[5] = bounds.center + new Vector3(bounds.size.x, bounds.size.y, -bounds.size.z) * 0.5f;
        vertices[6] = bounds.center + new Vector3(bounds.size.x, bounds.size.y, bounds.size.z) * 0.5f;
        vertices[7] = bounds.center + new Vector3(-bounds.size.x, bounds.size.y, bounds.size.z) * 0.5f;


        return vertices;
    }



    public void DrawBBox(Vector3[] cubeVertices)
    {

        GameObject CubeforCheck = new GameObject("Cube");
        CubeforCheck.transform.parent = transform;
        MeshFilter meshFilter = CubeforCheck.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = CubeforCheck.AddComponent<MeshRenderer>();

        // Create a new mesh
        UnityEngine.Mesh mesh = new UnityEngine.Mesh();
        mesh.vertices = cubeVertices;

        int[] triangles = new int[]
        {
            0, 1, 2, 2, 3, 0, // Front face
            4, 5, 6, 6, 7, 4, // Back face
            0, 4, 7, 7, 3, 0, // Left face
            1, 5, 6, 6, 2, 1, // Right face
            0, 1, 5, 5, 4, 0, // Bottom face
            2, 3, 7, 7, 6, 2  // Top face
        };
        mesh.triangles = triangles;

        meshFilter.mesh = mesh;
    }

    public void SaveHouseToJson(House eachHouse)
    {

        string path = Application.dataPath + SAVE_BASE_PATH + eachHouse.houseName + ".json";



        string eachHouseJson = JsonUtility.ToJson(eachHouse);

        StreamWriter sw = new StreamWriter(path);
        sw.AutoFlush = true;
        sw.Write(eachHouseJson);
        Debug.Log(eachHouse.houseName.ToString() + " is written to the json file");

    }




}

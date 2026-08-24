using System.Collections;
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;

public class MeshEdgeAligner : MonoBehaviour
{
    //public MeshFilter sourceMesh;
    public GameObject pivot;
    //public MeshFilter targetMesh;
    public GameObject charcter;
    MRUKRoom room;
    GameObject floor;
    public bool isAligned = false;
    Vector3 originpos;
    Quaternion originrot;


    void Start()
    {
        
    }

    public void AlignMeshes(MeshFilter targetMeshData)
    {
        // whether to show mesh
        GameObject.FindObjectOfType<RoomMeshAnchor>().gameObject.GetComponent<MeshRenderer>().enabled = false;
        room = GameObject.FindAnyObjectByType<MRUKRoom>();
        originpos = pivot.transform.position;
        originrot = pivot.transform.rotation;
        floor = room.transform.Find("FLOOR").gameObject;

        GameObject go = Instantiate(floor, floor.transform.position, floor.transform.rotation);
        //pivot.transform.position = floor.transform.position;
        //pivot.transform.rotation = floor.transform.rotation;

        room.transform.parent = go.transform;
        charcter.transform.parent = go.transform;


        go.transform.position = originpos;
        go.transform.rotation = originrot;

        room.transform.parent = null;
        charcter.transform.parent = null;

        Destroy(go);

        // 변환을 적용하는 함수
        if (!isAligned) isAligned = true;
        // 변환 구해서 방과 유저에 적용해주기 (방을 같이 안하면 아바타가 혼자 움직임)


    }


    public void Hello()
    {
        Debug.Log("======================= Hello =======================");
    }


}

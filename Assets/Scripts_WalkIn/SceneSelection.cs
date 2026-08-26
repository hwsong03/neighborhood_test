using System.Collections;
using System.Collections.Generic;
using UnityEngine;
// [NETCODE - 주석처리] using Unity.Netcode;
using Random = UnityEngine.Random;
using Oculus.Avatar2;
using UnityEngine.SceneManagement;
using Meta.XR.MultiplayerBlocks.Shared;
// [NETCODE - 주석처리] using Meta.XR.MultiplayerBlocks.NGO;
using Meta.XR.Simulator;
using Meta.XR.BuildingBlocks;
using System.IO;
// [FUSION - 추가]
using Fusion;
using Meta.XR.MultiplayerBlocks.Fusion;  // AvatarBehaviourFusion
// using Fusion.Addons.Physics; // 필요시 활성화
using NetworkTransform = Fusion.NetworkTransform;
using AvatarBehaviourFusion = Meta.XR.MultiplayerBlocks.Fusion.AvatarBehaviourFusion;
using Oculus.Skinning.GpuSkinning;



public class SceneSelection : MonoBehaviour
{
    [SerializeField] public int type = 0;
    [SerializeField] int[] AvatarIndexForUser = new int[3];
    public GameObject target;
    [SerializeField] GameObject avt;
    public GameObject pivot1, pivot2;
    [SerializeField] bool done = false;
    [SerializeField] GameObject houses;
    [SerializeField] bool IsDemoModeAndThisIsServer = false;
    bool A, B, C = false;

    // check augmented
    public bool notaugmented = true;

    // check MR Headset on
    public GameObject cameraRig;
    bool onlyOnce = false;

    // stop voronoi
    public bool modeB = false;


    // to detect pivot
    public GameObject meshEdgeAligner;
    public GameObject arrangeMRHouse;

    public Vector3 pivotDiff = Vector3.zero;
    public Vector3 diffChange = Vector3.zero;

    // detect my movement
    private Vector3 previousCharacterPos = Vector3.zero;
    private Vector3 currentCharacterPos = Vector3.zero;

    // return to original alignment
    private List<Vector3> originalRoomPos = new List<Vector3>();

    // transfer data
    public GameObject transfer;

    // ===================== OPTIMIZATION OFFSET SYSTEM ===================== //
    [Header("Optimization Offset System")]
    public bool useOptimizationOffset = false;
    public Vector3 remote1Offset = Vector3.zero;  // Legacy - kept for reference
    public Vector3 remoteOffset = Vector3.zero;   // Legacy - kept for reference
    public Vector3 localAvatarOffset = Vector3.zero;

    [Header("Simple House Transform System")]
    // Remote avatar sender indices
    public int remoteIndex = -1;   // Which house sends to "RemoteAvatar"
    public int remote1Index = -1;  // Which house sends to "RemoteAvatar1"

    // Fixed offsets calculated from house positions (added every frame like original working approach)
    private Vector3 remoteFixedOffset = Vector3.zero;
    private Vector3 remote1FixedOffset = Vector3.zero;

    // House Transform references (for compatibility with existing logic)
    private Transform remoteHouseTransform = null;
    private Transform remote1HouseTransform = null;

    private void Awake()
    {
        //GameObject.Find("[BuildingBlock] Networked Avatar").GetComponent<>
    }

    void Start()
    {
        GameObject oveCamrig = GameObject.FindFirstObjectByType<OVRCameraRig>().gameObject;
        switch (type)
        {
            // ���⼭ player layer change
            case 0:
                A = true;
                ChangeLayerRecursively(oveCamrig, 14);
                break;
            case 1: B = true; ChangeLayerRecursively(oveCamrig, 15); break;
            case 2: C = true; ChangeLayerRecursively(oveCamrig, 16); break;
        }
        if (!IsDemoModeAndThisIsServer)
        {

            //setPassthrogh(type);
        }





    }
    // Re-runs Start()'s player-layer switch for a (network-assigned) house type.
    // Called by HouseJoinOrderAssigner once join order is known, since Start()
    // runs before the Fusion connection completes and can only use the default type.
    public void ApplyType(int newType)
    {
        type = newType;
        A = B = C = false;

        GameObject oveCamrig = GameObject.FindFirstObjectByType<OVRCameraRig>().gameObject;
        switch (type)
        {
            case 0: A = true; ChangeLayerRecursively(oveCamrig, 14); break;
            case 1: B = true; ChangeLayerRecursively(oveCamrig, 15); break;
            case 2: C = true; ChangeLayerRecursively(oveCamrig, 16); break;
        }
    }

    private void ChangeLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;

        foreach (Transform child in obj.transform)
        {
            ChangeLayerRecursively(child.gameObject, layer);
        }
    }


    // Update is called once per frame
    void Update()
    {

        if (notaugmented == true)
        {
            //activateAugObjs();

            notaugmented = false;
        }


        if (target.GetComponent<Arrange_Walkin>().characterInitialized && !done)
        {

            avt = target.transform.GetChild(0).GetChild(type).gameObject;
            
            //canvas.SetActive(true);

            // save original house location 
            for (int i = 0; i < arrangeMRHouse.GetComponent<Arrange_Walkin>().houses.Count; i++)
            {
                originalRoomPos.Add(arrangeMRHouse.GetComponent<Arrange_Walkin>().houses[i].transform.position);

            }


            done = true;
        }

        if (avt != null)
        {
            // Use LocalAvatar Joint Chest position for consistency with remote avatars
            // This ensures X key sends consistent positions to Python
            GameObject localAvatar = GameObject.Find("LocalAvatar");
            if (localAvatar != null)
            {
                Transform jointChest = AvatarJointHelper.FindJointChest(localAvatar.transform);
                if (jointChest != null)
                {
                    avt.transform.position = jointChest.position;
                    avt.transform.rotation = jointChest.rotation;
                }
            }


            if (IsHeadsetOff())
            {
                // sync camera and character
                if (GameObject.Find("LocalAvatar") != null)
                {
                    GameObject me = GameObject.Find("LocalAvatar");
                    Camera.main.transform.parent.parent.SetParent(me.transform);


                    // locate avatar pos
                    if (onlyOnce == false)
                    {
                        //me.transform.position = new Vector3(2.47f, 0f, 3.26f);
                        me.transform.position = new Vector3(4.1f, 0, 3.26f);
                        previousCharacterPos = me.transform.position;
                        currentCharacterPos = me.transform.position;
                        onlyOnce = true;
                    }

                    previousCharacterPos = currentCharacterPos;
                    currentCharacterPos = me.transform.position;

                    // Calculate the movement that occurred this frame
                    Vector3 positionChange = currentCharacterPos - previousCharacterPos;
                    // save to send
                    diffChange = positionChange;

                }
            }

            // deactivate when other user comes in

            if (GameObject.Find("RemoteAvatar1") != null)
            {
                GameObject remote1 = GameObject.Find("RemoteAvatar1");

                if (modeB == true)
                {
                    // [MODEB] Keep NetworkTransform ENABLED so we can read network position in LateUpdate
                    // ModeBCalculator handles avatar and house positioning in LateUpdate

                    // [MODEB - 주석처리] Old diffChange approach - now handled by ModeBCalculator
                    // if (transfer.GetComponent<TransferManager>().startSendingVec == true)
                    // {
                    //     var nt1 = remote1.GetComponent<NetworkTransform>();
                    //     if (nt1 != null) nt1.enabled = false;
                    // }
                    // remote1.transform.position += diffChange;
                    // var aw1 = arrangeMRHouse.GetComponent<Arrange_Walkin>();
                    // for (int i = 0; i < aw1.houses.Count; i++)
                    // {
                    //     if (i == type) continue;
                    //     aw1.houses[i].transform.position += diffChange;
                    // }
                }
                else
                {
                    // [NETCODE - 주석처리]
                    // remote1.GetComponent<ClientNetworkTransform>().SyncPositionX = true;
                    // remote1.GetComponent<ClientNetworkTransform>().SyncPositionY = true;
                    // remote1.GetComponent<ClientNetworkTransform>().SyncPositionZ = true;
                    
                    // [FUSION - 추가] NetworkTransform 활성화
                    var nt1 = remote1.GetComponent<NetworkTransform>();
                    if (nt1 != null) nt1.enabled = true;
                }

            }

            if (GameObject.Find("RemoteAvatar") != null)
            {
                GameObject remote = GameObject.Find("RemoteAvatar");

                if (modeB == true)
                {
                    // [MODEB] Keep NetworkTransform ENABLED so we can read network position in LateUpdate
                    // ModeBCalculator handles avatar and house positioning in LateUpdate

                    // [MODEB - 주석처리] Old diffChange approach - now handled by ModeBCalculator
                    // if (transfer.GetComponent<TransferManager>().startSendingVec == true)
                    // {
                    //     var nt = remote.GetComponent<NetworkTransform>();
                    //     if (nt != null) nt.enabled = false;
                    // }
                    // remote.transform.position += diffChange;
                }
                else
                {
                    // [NETCODE - 주석처리]
                    // remote.GetComponent<ClientNetworkTransform>().SyncPositionX = true;
                    // remote.GetComponent<ClientNetworkTransform>().SyncPositionY = true;
                    // remote.GetComponent<ClientNetworkTransform>().SyncPositionZ = true;
                    
                    // [FUSION - 추가] NetworkTransform 활성화
                    var nt = remote.GetComponent<NetworkTransform>();
                    if (nt != null) nt.enabled = true;
                }

            }


            // [FUSION - 수정] A,B 키 처리는 TransferManager에서 서버만 처리하고 RPC로 동기화됨
            // 클라이언트에서는 더 이상 A,B 키를 직접 처리하지 않음
            // 기존 A,B 로직은 OnModeA(), OnModeB() 함수로 분리됨


            updateavatar();
            checkAvatartandUpdate();

            // if headset is not on
            if (IsHeadsetOff())
            {
                // this is to sync me and my real physical space
                if (pivot1 != null && A)
                {
                    if (pivot1.transform.childCount > 0)
                    {
                        try
                        {
                            target.transform.GetChild(0).GetChild(1).gameObject.transform.position = pivot1.transform.position;
                            target.transform.GetChild(0).GetChild(1).gameObject.transform.rotation = pivot1.transform.rotation;
                        }
                        catch { }

                    }
                }
                if (pivot2 != null && A)
                {

                    if (pivot2.transform.childCount > 0)
                    {
                        try
                        {
                            target.transform.GetChild(0).GetChild(2).gameObject.transform.position = pivot2.transform.position;
                            target.transform.GetChild(0).GetChild(2).gameObject.transform.rotation = pivot2.transform.rotation;
                        }
                        catch { }
                        //target.transform.GetChild(0).GetChild(2).gameObject.transform.position = pivot2.transform.GetChild(1).position;
                        //target.transform.GetChild(0).GetChild(2).gameObject.transform.rotation = pivot2.transform.GetChild(1).rotation;
                    }
                }

                if (pivot1 != null && B)
                {
                    if (pivot1.transform.childCount > 0)
                    {
                        try
                        {

                            // ���⼭ ��� character ��ġ�� �������ִ±���? 
                            //Debug.Log(pivot1.transform.GetChild(2).position);
                            target.transform.GetChild(0).GetChild(0).gameObject.transform.position = pivot1.transform.position;
                            target.transform.GetChild(0).GetChild(0).gameObject.transform.rotation = pivot1.transform.rotation;
                        }
                        catch { }
                        //target.transform.GetChild(0).GetChild(0).gameObject.transform.position = pivot1.transform.GetChild(1).position;
                        //target.transform.GetChild(0).GetChild(0).gameObject.transform.rotation = pivot1.transform.GetChild(1).rotation;
                    }
                }
                if (pivot2 != null && B)
                {
                    if (pivot2.transform.childCount > 0)
                    {
                        try
                        {
                            target.transform.GetChild(0).GetChild(2).gameObject.transform.position = pivot2.transform.position;
                            target.transform.GetChild(0).GetChild(2).gameObject.transform.rotation = pivot2.transform.rotation;
                        }
                        catch { }
                        //target.transform.GetChild(0).GetChild(2).gameObject.transform.position = pivot2.transform.GetChild(1).position;
                        //target.transform.GetChild(0).GetChild(2).gameObject.transform.rotation = pivot2.transform.GetChild(1).rotation;
                    }
                }

                if (pivot1 != null && C)
                {
                    if (pivot1.transform.childCount > 0)
                    {
                        try
                        {
                            target.transform.GetChild(0).GetChild(0).gameObject.transform.position = pivot1.transform.position;
                            target.transform.GetChild(0).GetChild(0).gameObject.transform.rotation = pivot1.transform.rotation;
                        }
                        catch { }
                        //target.transform.GetChild(0).GetChild(0).gameObject.transform.position = pivot1.transform.GetChild(1).position;
                        //target.transform.GetChild(0).GetChild(0).gameObject.transform.rotation = pivot1.transform.GetChild(1).rotation;
                    }
                }
                if (pivot2 != null && C)
                {
                    if (pivot2.transform.childCount > 0)
                    {
                        try
                        {
                            target.transform.GetChild(0).GetChild(1).gameObject.transform.position = pivot2.transform.position;
                            target.transform.GetChild(0).GetChild(1).gameObject.transform.rotation = pivot2.transform.rotation;
                        }
                        catch { }
                        //target.transform.GetChild(0).GetChild(1).gameObject.transform.position = pivot2.transform.GetChild(1).position;
                        //target.transform.GetChild(0).GetChild(1).gameObject.transform.rotation = pivot2.transform.GetChild(1).rotation;
                    }
                }



            }
            else
            {
                // this is to sync me and my real physical space
                if (pivot1 != null && A)
                {

                        //try
                        //{
                        //    target.transform.GetChild(0).GetChild(2).gameObject.transform.position = pivot1.transform.GetChild(2).position;
                        //    target.transform.GetChild(0).GetChild(2).gameObject.transform.rotation = pivot1.transform.GetChild(2).rotation;

                        //}
                        //catch { }
                        try //  DEMO�� ���� ����; �Ʒ��� ���� �ڵ�
                        {
                            //target.transform.GetChild(0).GetChild(1).gameObject.transform.position = pivot1.transform.GetChild(2).position;
                            //target.transform.GetChild(0).GetChild(1).gameObject.transform.rotation = pivot1.transform.GetChild(2).rotation;
                            target.transform.GetChild(0).GetChild(1).gameObject.transform.position = AvatarJointHelper.FindJointChest(pivot1.transform).position;
                            target.transform.GetChild(0).GetChild(1).gameObject.transform.rotation = AvatarJointHelper.FindJointChest(pivot1.transform).rotation;
                        }
                        catch { }

                }
                if (pivot2 != null && A)
                {


                        try
                        {
                            //target.transform.GetChild(0).GetChild(2).gameObject.transform.position = pivot2.transform.GetChild(2).position;
                            //target.transform.GetChild(0).GetChild(2).gameObject.transform.rotation = pivot2.transform.GetChild(2).rotation;
                            target.transform.GetChild(0).GetChild(2).gameObject.transform.position = AvatarJointHelper.FindJointChest(pivot2.transform).position;
                            target.transform.GetChild(0).GetChild(2).gameObject.transform.rotation = AvatarJointHelper.FindJointChest(pivot2.transform).rotation;
                        }
                        catch { }
                        //target.transform.GetChild(0).GetChild(2).gameObject.transform.position = pivot2.transform.GetChild(1).position;
                        //target.transform.GetChild(0).GetChild(2).gameObject.transform.rotation = pivot2.transform.GetChild(1).rotation;
                    
                }

                if (pivot1 != null && B)
                {

                        try
                        {
                            //target.transform.GetChild(0).GetChild(0).gameObject.transform.position = pivot1.transform.GetChild(2).position;
                            //target.transform.GetChild(0).GetChild(0).gameObject.transform.rotation = pivot1.transform.GetChild(2).rotation;
                            target.transform.GetChild(0).GetChild(0).gameObject.transform.position = AvatarJointHelper.FindJointChest(pivot1.transform).position;
                            target.transform.GetChild(0).GetChild(0).gameObject.transform.rotation = AvatarJointHelper.FindJointChest(pivot1.transform).rotation;
                        }
                        catch { }
                        //target.transform.GetChild(0).GetChild(0).gameObject.transform.position = pivot1.transform.GetChild(1).position;
                        //target.transform.GetChild(0).GetChild(0).gameObject.transform.rotation = pivot1.transform.GetChild(1).rotation;
                    
                }
                if (pivot2 != null && B)
                {

                        try
                        {
                            //target.transform.GetChild(0).GetChild(2).gameObject.transform.position = pivot2.transform.GetChild(2).position;
                            //target.transform.GetChild(0).GetChild(2).gameObject.transform.rotation = pivot2.transform.GetChild(2).rotation;
                            target.transform.GetChild(0).GetChild(2).gameObject.transform.position = AvatarJointHelper.FindJointChest(pivot2.transform).position;
                            target.transform.GetChild(0).GetChild(2).gameObject.transform.rotation = AvatarJointHelper.FindJointChest(pivot2.transform).rotation;
                        }
                        catch { }
                        //target.transform.GetChild(0).GetChild(2).gameObject.transform.position = pivot2.transform.GetChild(1).position;
                        //target.transform.GetChild(0).GetChild(2).gameObject.transform.rotation = pivot2.transform.GetChild(1).rotation;
                    
                }

                if (pivot1 != null && C)
                {

                        try
                        {
                            //target.transform.GetChild(0).GetChild(0).gameObject.transform.position = pivot1.transform.GetChild(2).position;
                            //target.transform.GetChild(0).GetChild(0).gameObject.transform.rotation = pivot1.transform.GetChild(2).rotation;
                            target.transform.GetChild(0).GetChild(0).gameObject.transform.position = AvatarJointHelper.FindJointChest(pivot1.transform).position;
                            target.transform.GetChild(0).GetChild(0).gameObject.transform.rotation = AvatarJointHelper.FindJointChest(pivot1.transform).rotation;
                        }
                        catch { }
                        //target.transform.GetChild(0).GetChild(0).gameObject.transform.position = pivot1.transform.GetChild(1).position;
                        //target.transform.GetChild(0).GetChild(0).gameObject.transform.rotation = pivot1.transform.GetChild(1).rotation;
                    
                }
                if (pivot2 != null && C)
                {
  
                        try
                        {
                            //target.transform.GetChild(0).GetChild(1).gameObject.transform.position = pivot2.transform.GetChild(2).position;
                            //target.transform.GetChild(0).GetChild(1).gameObject.transform.rotation = pivot2.transform.GetChild(2).rotation;
                            target.transform.GetChild(0).GetChild(1).gameObject.transform.position = AvatarJointHelper.FindJointChest(pivot2.transform).position;
                            target.transform.GetChild(0).GetChild(1).gameObject.transform.rotation = AvatarJointHelper.FindJointChest(pivot2.transform).rotation;
                        }
                        catch { }
                        //target.transform.GetChild(0).GetChild(1).gameObject.transform.position = pivot2.transform.GetChild(1).position;
                        //target.transform.GetChild(0).GetChild(1).gameObject.transform.rotation = pivot2.transform.GetChild(1).rotation;
                    
                }
            }


        }


    }



    // 한번만 브로드캐스트하기 위한 플래그
    private bool avatarIndicesBroadcasted = false;

    void checkAvatartandUpdate()
    {
        // 모든 type에서 자신의 LocalAvatar 인덱스 설정
        GameObject go = null;
        try
        {
            go = GameObject.Find("LocalAvatar");
            if (go != null)
            {
                var avatarBehaviour = go.GetComponent<AvatarBehaviourFusion>();
                if (avatarBehaviour != null)
                {
                    // State Authority가 있을 때만 수정 가능
                    if (avatarBehaviour.Object != null && avatarBehaviour.Object.HasStateAuthority)
                    {
                        if (avatarBehaviour.LocalAvatarIndex != AvatarIndexForUser[type])
                        {
                            Debug.Log($"[SceneSelection] Setting LocalAvatar index from {avatarBehaviour.LocalAvatarIndex} to {AvatarIndexForUser[type]} for type {type}");
                            avatarBehaviour.LocalAvatarIndex = AvatarIndexForUser[type];
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("[SceneSelection] LocalAvatar has no AvatarBehaviourFusion component");
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SceneSelection] Error setting LocalAvatar index: {e.Message}");
        }

        // type 0 (서버)일 때: RPC로 모든 클라이언트에게 아바타 인덱스 브로드캐스트
        if (type == 0 && !avatarIndicesBroadcasted && transfer != null)
        {
            // pivot1, pivot2가 존재할 때 브로드캐스트 (클라이언트들이 접속했을 때)
            if (pivot1 != null || pivot2 != null)
            {
                var transferManager = transfer.GetComponent<TransferManager>();
                if (transferManager != null)
                {
                    Debug.Log($"[SceneSelection] Broadcasting avatar indices: [{AvatarIndexForUser[0]}, {AvatarIndexForUser[1]}, {AvatarIndexForUser[2]}]");
                    transferManager.SetAvatarIndexForClients(AvatarIndexForUser);
                    avatarIndicesBroadcasted = true;
                }
            }
        }
    }


    void updateavatar()
    {
        // Find remote avatar by name "RemoteAvatar" (not yet renamed)
        GameObject go = null;
        try
        {
            go = GameObject.Find("RemoteAvatar");
            if (go == null) return;

            // Get the AvatarBehaviourFusion to identify which user this avatar belongs to
            var avatarBehaviour = go.GetComponent<AvatarBehaviourFusion>();
            if (avatarBehaviour == null) return;

            int remoteAvatarIndex = avatarBehaviour.LocalAvatarIndex;

            // Determine which user ID this avatar belongs to by matching LocalAvatarIndex
            int remoteUserId = -1;
            for (int i = 0; i < AvatarIndexForUser.Length; i++)
            {
                if (AvatarIndexForUser[i] == remoteAvatarIndex && i != type)
                {
                    remoteUserId = i;
                    break;
                }
            }

            // If we couldn't identify the user, fall back to old behavior
            if (remoteUserId == -1)
            {
                // Fallback: just assign in order found
                if (pivot1 == null)
                {
                    pivot1 = go;
                    go.transform.name += "1";
                }
                else if (pivot2 == null)
                {
                    pivot2 = go;
                }
                return;
            }

            // Determine correct assignment based on user mapping:
            // pivot1 should be RemoteAvatar1, pivot2 should be RemoteAvatar
            // The naming convention expects:
            // - For id==0: RemoteAvatar1 = user1, RemoteAvatar = user2
            // - For id==1: RemoteAvatar1 = user0, RemoteAvatar = user2
            // - For id==2: RemoteAvatar1 = user0, RemoteAvatar = user1
            int expectedRemote1UserId, expectedRemoteUserId;
            switch (type)
            {
                case 0:
                    expectedRemote1UserId = 1;
                    expectedRemoteUserId = 2;
                    break;
                case 1:
                    expectedRemote1UserId = 0;
                    expectedRemoteUserId = 2;
                    break;
                case 2:
                default:
                    expectedRemote1UserId = 0;
                    expectedRemoteUserId = 1;
                    break;
            }

            // Assign to correct pivot based on identified user
            if (remoteUserId == expectedRemote1UserId && pivot1 == null)
            {
                pivot1 = go;
                go.transform.name += "1";  // Rename to RemoteAvatar1
                Debug.Log($"[SceneSelection] Assigned user{remoteUserId} avatar to pivot1 (RemoteAvatar1)");
            }
            else if (remoteUserId == expectedRemoteUserId && pivot2 == null)
            {
                pivot2 = go;
                // Keep name as RemoteAvatar
                Debug.Log($"[SceneSelection] Assigned user{remoteUserId} avatar to pivot2 (RemoteAvatar)");
            }
            else if (pivot1 == null)
            {
                // If expected slot is taken but this slot is free, use it anyway
                pivot1 = go;
                go.transform.name += "1";
                Debug.LogWarning($"[SceneSelection] User{remoteUserId} avatar assigned to pivot1 (fallback)");
            }
            else if (pivot2 == null)
            {
                pivot2 = go;
                Debug.LogWarning($"[SceneSelection] User{remoteUserId} avatar assigned to pivot2 (fallback)");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SceneSelection] updateavatar error: {e.Message}");
        }
    }

    public void setPssThroughAll()
    {
        setPassthrogh(0);
        setPassthrogh(1);
        setPassthrogh(2);
    }
    void setPassthrogh(int type)
    {
        // aug object���� visualize ��Ű��


        GameObject go = houses.transform.GetChild(type).gameObject;
        var objects = go.transform.FindChildRecursive("Object");
        foreach (var renderer in objects.GetComponentsInChildren<MeshRenderer>())
        {
            renderer.enabled = false;
        }
        var walls = go.transform.FindChildRecursive("Walls");
        foreach (var renderer in walls.GetComponentsInChildren<MeshRenderer>())
        {
            renderer.enabled = false;
        }


    }

    public bool isAllUserConnected()
    {
        if (pivot1 != null && pivot2 != null)
        {
            return true;
        }
        else
            return false;
    }

    public void activateAugObjs()
    {
        GameObject continueRender = GameObject.Find("augmentedObjs");
        //Debug.Log(continueRender.name);


        var augobjs = continueRender.transform.GetComponentsInChildren<MeshRenderer>();
        foreach (var renderer in augobjs)
        {
            renderer.enabled = true;
        }

    }


    public bool IsHeadsetOff()
    {
        // simulator�� �����ִ°� ��� ����?
        //bool isSimulatorOn = simula

        //return cameraRig.transform.GetChild(0).transform.localPosition == Vector3.zero;
        return false;
    }



    public void ApplyRoomMovement(Vector3 movement, int movedPlayerType)
    {
        // movedPlayerType: ������ ����� type (0, 1, 2)
        GameObject room = arrangeMRHouse.GetComponent<Arrange_Walkin>().houses[movedPlayerType];
        room.transform.position -= movement;
    }


    // ===================== A,B 모드 함수 (RPC에서 호출됨) ===================== //
    
    // [FUSION - 추가] A키 눌렀을 때 실행되는 함수 (freeze mode)
    public void OnModeA()
    {
        Debug.Log("[SceneSelection] OnModeA called - freeze mode");
        modeB = false;

        // End modeB in ModeBCalculator
        if (ModeBCalculator.Instance != null)
        {
            ModeBCalculator.Instance.OnModeBEnd();
        }

        // 필요하면 여기에 추가 로직 (예: 집 원래 위치로 복원 등)
    }

    // [FUSION - 추가] B키 눌렀을 때 실행되는 함수 (거리계산 mode)
    public void OnModeB()
    {
        Debug.Log("[SceneSelection] OnModeB called - distance mode");
        modeB = true;

        // Start modeB in ModeBCalculator
        if (ModeBCalculator.Instance != null)
        {
            ModeBCalculator.Instance.OnModeBStart();
        }

        // deactivate traversezone
        foreach (GameObject obj in GameObject.FindObjectsOfType<GameObject>())
        {
            if (obj.name.Contains("traverseZone"))
            {
                obj.SetActive(false);
            }
        }
    }

    // ===================== SIMPLE HOUSE TRANSFORM SYSTEM ===================== //
    // Instead of complex centroid calculations, we simply use the house's Transform
    // to convert remote avatar positions. The house already has correct position/rotation.

    // [OPTIMIZATION OFFSET] LateUpdate runs AFTER NetworkTransform has updated positions
    // Use house Transform.TransformPoint to properly convert positions with rotation
    private int lateUpdateCounter = 0;
    void LateUpdate()
    {
        // When modeB is active, use ModeBCalculator instead of normal offset logic
        if (modeB && ModeBCalculator.Instance != null && ModeBCalculator.Instance.isModeB)
        {
            ModeBCalculator.Instance.ApplyModeBPositioning();
            return;
        }

        if (!useOptimizationOffset) return;

        // bool shouldLog = (lateUpdateCounter++ % 60 == 0);  // Log every second

        GameObject remote1 = GameObject.Find("RemoteAvatar1");
        GameObject remote = GameObject.Find("RemoteAvatar");

        // // Debug: Log what we found
        // if (shouldLog)
        // {
        //     Debug.Log($"[LateUpdate] Found: remote1={remote1 != null}, remote={remote != null}, remoteHouseTransform={remoteHouseTransform != null}, remote1HouseTransform={remote1HouseTransform != null}");
        // }

        // Transform RemoteAvatar1 using += approach (works with NetworkTransform)
        // Use Joint Chest position as reference (the actual visual bone position)
        if (remote1 != null && remote1HouseTransform != null)
        {
            // Get Joint Chest - the actual visual position in Meta avatar hierarchy
            Transform jointChest = AvatarJointHelper.FindJointChest(remote1.transform);

            // Use Joint Chest position if available, otherwise use root
            Vector3 networkPos = (jointChest != null) ? jointChest.position : remote1.transform.position;

            // Apply rotation to network position
            Vector3 rotatedPos = remote1HouseTransform.rotation * networkPos;
            // Calculate rotation offset (how much rotation moved the point)
            Vector3 rotationOffset = rotatedPos - networkPos;
            // Translation offset is the house position
            Vector3 translationOffset = remote1HouseTransform.position;
            // Apply both offsets using += to root (child4 moves with root)
            remote1.transform.position += new Vector3(rotationOffset.x + translationOffset.x, 0, rotationOffset.z + translationOffset.z);

            // Save jointChest's correct world position BEFORE rotating root
            Vector3 jointChestCorrectPos = (jointChest != null) ? jointChest.position : remote1.transform.position;

            // Apply rotation offset to root (changes facing direction)
            remote1.transform.rotation = remote1HouseTransform.rotation * remote1.transform.rotation;

            // Compensate: jointChest orbited around root, move root back so jointChest returns to correct position
            if (jointChest != null)
            {
                Vector3 jointChestCurrentPos = jointChest.position;
                Vector3 compensation = jointChestCorrectPos - jointChestCurrentPos;
                remote1.transform.position += compensation;
            }

            // if (shouldLog)
            // {
            //     Debug.Log($"[LateUpdate] Remote1: child4={child4 != null}, networkPos={networkPos}, rotOffset={rotationOffset}, transOffset={translationOffset}, final={remote1.transform.position}");
            // }
        }

        // Transform RemoteAvatar using += approach
        if (remote != null && remoteHouseTransform != null)
        {
            // Get Joint Chest - the actual visual position in Meta avatar hierarchy
            Transform jointChest = AvatarJointHelper.FindJointChest(remote.transform);

            // Use Joint Chest position if available, otherwise use root
            Vector3 networkPos = (jointChest != null) ? jointChest.position : remote.transform.position;

            Vector3 rotatedPos = remoteHouseTransform.rotation * networkPos;
            Vector3 rotationOffset = rotatedPos - networkPos;
            Vector3 translationOffset = remoteHouseTransform.position;
            remote.transform.position += new Vector3(rotationOffset.x + translationOffset.x, 0, rotationOffset.z + translationOffset.z);

            // Save jointChest's correct world position BEFORE rotating root
            Vector3 jointChestCorrectPos = (jointChest != null) ? jointChest.position : remote.transform.position;

            // Apply rotation offset to root (changes facing direction)
            remote.transform.rotation = remoteHouseTransform.rotation * remote.transform.rotation;

            // Compensate: jointChest orbited around root, move root back so jointChest returns to correct position
            if (jointChest != null)
            {
                Vector3 jointChestCurrentPos = jointChest.position;
                Vector3 compensation = jointChestCorrectPos - jointChestCurrentPos;
                remote.transform.position += compensation;
            }

            // if (shouldLog)
            // {
            //     Debug.Log($"[LateUpdate] Remote: child4={child4 != null}, networkPos={networkPos}, rotOffset={rotationOffset}, transOffset={translationOffset}, final={remote.transform.position}");
            // }
        }
    }

    // [OPTIMIZATION OFFSET] SIMPLE: Just store house Transform references
    // The house already has the correct position and rotation from arrangement
    public void SetupRotationAwareTransform(OffsetCalculator offsetCalc, List<PythonEachHouse> pythonData)
    {
        // Use singleton if parameter is null
        var calc = offsetCalc ?? OffsetCalculator.Instance;
        if (calc == null) return;

        int myId = calc.id;

        // Get remote indices
        calc.GetRemoteIndices(out int remoteIdx, out int remote1Idx);
        remoteIndex = remoteIdx;
        remote1Index = remote1Idx;

        // Get house positions from Arrange_WalkIn's houses list and calculate fixed offsets
        var housesList = arrangeMRHouse?.GetComponent<Arrange_Walkin>()?.houses;

        if (housesList != null && housesList.Count > 0)
        {
            // Calculate fixed offset = house position (where the house is in my scene)
            // This offset gets ADDED to the network position every frame
            if (remoteIndex >= 0 && remoteIndex < housesList.Count)
            {
                remoteHouseTransform = housesList[remoteIndex].transform;
                remoteFixedOffset = remoteHouseTransform.position;
                Debug.Log($"[OptOffset] remoteFixedOffset (house {remoteIndex}): {remoteFixedOffset}");
            }

            if (remote1Index >= 0 && remote1Index < housesList.Count)
            {
                remote1HouseTransform = housesList[remote1Index].transform;
                remote1FixedOffset = remote1HouseTransform.position;
                Debug.Log($"[OptOffset] remote1FixedOffset (house {remote1Index}): {remote1FixedOffset}");
            }
        }
        else
        {
            Debug.LogError("[OptOffset] Cannot get houses from Arrange_WalkIn!");
        }

        useOptimizationOffset = true;
        Debug.Log($"[OptOffset] Simple house transform enabled for id={myId}");
    }

    // [OPTIMIZATION OFFSET] Legacy method - kept for compatibility but now calls rotation-aware version
    public void SetOptimizationOffsets(Vector3 remote1TargetPos, Vector3 remoteTargetPos)
    {
        // This is now just a fallback - the rotation-aware method should be used instead
        GameObject remote1 = GameObject.Find("RemoteAvatar1");
        GameObject remote = GameObject.Find("RemoteAvatar");

        if (remote1 != null)
        {
            remote1Offset = remote1TargetPos - remote1.transform.position;
            Debug.Log($"[OptOffset] Remote1 legacy offset set: {remote1Offset}");
        }

        if (remote != null)
        {
            remoteOffset = remoteTargetPos - remote.transform.position;
            Debug.Log($"[OptOffset] Remote legacy offset set: {remoteOffset}");
        }

        // Note: useOptimizationOffset should be set by SetupRotationAwareTransform
        Debug.Log("[OptOffset] Legacy SetOptimizationOffsets called - prefer SetupRotationAwareTransform");
    }

    // [OPTIMIZATION OFFSET] Call this to clear offsets (e.g., when returning to normal mode)
    public void ClearOptimizationOffsets()
    {
        useOptimizationOffset = false;
        remote1Offset = Vector3.zero;
        remoteOffset = Vector3.zero;
        localAvatarOffset = Vector3.zero;
        remoteIndex = -1;
        remote1Index = -1;
        remoteHouseTransform = null;
        remote1HouseTransform = null;
        Debug.Log("[OptOffset] Optimization offsets cleared");
    }
}
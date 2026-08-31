using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;
using System.Threading.Tasks;
// [NETCODE - 주석처리]
// using Unity.Netcode;
// using Unity.Netcode.Components;
// using Meta.XR.MultiplayerBlocks.NGO;
using Newtonsoft.Json;
using UnityEngine.TextCore.Text;
// [FUSION - 추가]
using Fusion;
using NetworkTransform = Fusion.NetworkTransform;
using Oculus.Skinning.GpuSkinning;


// [NETCODE - 주석처리] public class TransferManager : NetworkBehaviour
// [FUSION - 추가] NetworkBehaviour 사용 (RPC를 위해 SimulationBehaviour 대신)
public class TransferManager : NetworkBehaviour
{
    // [FUSION - 추가] NetworkRunner 참조
    public NetworkRunner FusionRunner => Runner;

    // this is for opt result sending
    public GameObject arrangeMR;
    public GameObject regionsObj;
    private List<PythonEachHouse> receivedInfo;
    private List<PythonEachHouse> pythonAllHouse;

    // [FUSION - 추가] 청크 전송을 위한 변수들 (RPC 512바이트 제한 우회)
    private const int CHUNK_SIZE = 400; // 안전하게 400바이트로 설정
    private Dictionary<int, List<string>> receivedChunks = new Dictionary<int, List<string>>();

    // [FUSION - 추가] 아바타 인덱스 할당을 위한 변수
    private int[] avatarIndexForUsers = new int[3];


    // this is for pivot change
    public GameObject sceneSelection;
    public bool startSendingVec = false;
    public Vector3 lastReceivedVector;
    public Vector3 receivedFromClient1;
    public Vector3 receivedFromClient2;


    // related to mode shifts
    private bool walkin = false;

    // [OFFSET CALCULATOR] Now uses singleton: OffsetCalculator.Instance


    void Start()
    {


    }


    void Update()
    {
        // Arrange_Walkin의 isServer 사용
        bool isServer = arrangeMR.GetComponent<Arrange_Walkin>().isServer;

        // [DEBUG] 서버 상태 확인용
        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.B))
        {
            string key = Input.GetKeyDown(KeyCode.A) ? "A" : "B";
        }

        // you can change the mode here
        // A: 방 freeze mode
        // B: 위치전송 mode
        // [FUSION - 수정] 서버에서만 A,B 키 처리하고 RPC로 클라이언트에게 전송
        if (isServer)
        {
            if (Input.GetKeyDown(KeyCode.A))
            {
                Debug.Log("[TransferManager] Server pressed A key - triggering freeze mode");
                SetModeAndBroadcast(true); // walkin = true (freeze mode)
            }

            if (Input.GetKeyDown(KeyCode.B))
            {
                Debug.Log("[TransferManager] Server pressed B key - triggering distance mode");
                SetModeAndBroadcast(false); // walkin = false (위치전송 mode)
            }
        }

        // update user pos (with OneEuroFilter for smooth zone boundaries)
        if (arrangeMR.GetComponent<Arrange_Walkin>().characterInitialized == true)
        {
            var regions = regionsObj.GetComponent<Regions>();
            for (int i = 0; i < regions.characters.Count; i++)
            {
                if (walkin == false)
                {
                    Vector3 charPos = regions.characters[i].transform.position;
                    // Use filtered method for smooth zone boundaries
                    regions.SetUserPosition(i, charPos);
                }

            }
        }




        // if pressed, start sending
        if (Input.GetKeyDown(KeyCode.B))
        {
            startSendingVec = true;
        }


        if (startSendingVec && sceneSelection.GetComponent<SceneSelection>().modeB == true)
        {
            Vector3 sendingVec = sceneSelection.GetComponent<SceneSelection>().diffChange;
            int myType = sceneSelection.GetComponent<SceneSelection>().type;

            if (sendingVec.sqrMagnitude > 0.0001f)
            {
                // [FUSION - 추가]
                bool isServerVec = arrangeMR.GetComponent<Arrange_Walkin>().isServer;
                if (isServerVec)
                {
                    RPC_VecFromServerToClient(sendingVec, myType);
                }
                else
                {
                    RPC_VecFromClientToServer(sendingVec, myType);
                }
            }
        }


    }


    // ------------------------- sending vector ------------------------- //

    // [FUSION - 추가] Networked 속성 사용
    [Networked] public Vector3 ServerRandomVector { get; set; }


    // ------------------------- Rpc functions ------------------------- //

    // [FUSION - 추가] RPC 함수들
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_VecFromServerToClient(Vector3 fromServerVector, int movedPlayerType)
    {
        int myType = sceneSelection.GetComponent<SceneSelection>().type;

        if (myType != movedPlayerType)
        {
            lastReceivedVector = fromServerVector;
            sceneSelection.GetComponent<SceneSelection>().ApplyRoomMovement(fromServerVector, movedPlayerType);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_VecFromClientToServer(Vector3 fromClientVector, int movedPlayerType, RpcInfo info = default)
    {
        int myType = sceneSelection.GetComponent<SceneSelection>().type;

        if (myType != movedPlayerType)
        {
            sceneSelection.GetComponent<SceneSelection>().ApplyRoomMovement(fromClientVector, movedPlayerType);
        }

        // 다른 클라이언트들에게 브로드캐스트 (서버 제외 - 위에서 이미 처리함)
        RPC_BroadcastVecToClientsOnly(fromClientVector, movedPlayerType);
    }

    // [FUSION - 추가] 클라이언트들에게만 브로드캐스트 (서버는 이미 처리했으므로 제외)
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastVecToClientsOnly(Vector3 fromClientVector, int movedPlayerType)
    {
        bool isServer = arrangeMR.GetComponent<Arrange_Walkin>().isServer;

        // 서버는 RPC_VecFromClientToServer에서 이미 처리했으므로 무시
        if (isServer) return;

        int myType = sceneSelection.GetComponent<SceneSelection>().type;

        if (myType != movedPlayerType)
        {
            lastReceivedVector = fromClientVector;
            sceneSelection.GetComponent<SceneSelection>().ApplyRoomMovement(fromClientVector, movedPlayerType);
        }
    }


    void MoveRemoteAvatar(int playerType, Vector3 movement)
    {
        // SceneSelection의 pivot1, pivot2 참조
        GameObject pivot1 = sceneSelection.GetComponent<SceneSelection>().pivot1;
        GameObject pivot2 = sceneSelection.GetComponent<SceneSelection>().pivot2;

        // pivot1 확인
        if (pivot1 != null)
        {
            int pivot1Type = GetPivotType(pivot1);
            if (pivot1Type == playerType)
            {
                if (sceneSelection.GetComponent<SceneSelection>().modeB && startSendingVec)
                {
                    // [FUSION - 추가]
                    var nt1 = pivot1.GetComponent<NetworkTransform>();
                    if (nt1 != null) nt1.enabled = false;
                }
                pivot1.transform.position += movement;
            }
        }

        // pivot2 확인
        if (pivot2 != null)
        {
            int pivot2Type = GetPivotType(pivot2);
            if (pivot2Type == playerType)
            {
                if (sceneSelection.GetComponent<SceneSelection>().modeB && startSendingVec)
                {
                    // [FUSION - 추가]
                    var nt2 = pivot2.GetComponent<NetworkTransform>();
                    if (nt2 != null) nt2.enabled = false;
                }
                pivot2.transform.position += movement;
            }
        }
    }

    // pivot의 type을 알아내는 함수
    int GetPivotType(GameObject pivot)
    {
        int myType = sceneSelection.GetComponent<SceneSelection>().type;
        GameObject pivot1 = sceneSelection.GetComponent<SceneSelection>().pivot1;
        GameObject pivot2 = sceneSelection.GetComponent<SceneSelection>().pivot2;

        // 3인 상황에서 가정
        if (myType == 0) // 유저 A
        {
            if (pivot == pivot1) return 1; // 첫 번째 pivot = B
            if (pivot == pivot2) return 2; // 두 번째 pivot = C
        }
        else if (myType == 1) // 유저 B
        {
            if (pivot == pivot1) return 0; // 첫 번째 pivot = A
            if (pivot == pivot2) return 2; // 두 번째 pivot = C
        }
        else // 유저 C
        {
            if (pivot == pivot1) return 0; // 첫 번째 pivot = A
            if (pivot == pivot2) return 1; // 두 번째 pivot = B
        }

        return -1; // 오류
    }



    // ------------------------- propagate opt result ------------------------- //

    // [FUSION - 추가] 대용량 데이터를 청크로 분할해서 전송
    public void RPC_OptResultToClient(string sendingOptString)
    {
        // 데이터를 청크로 분할해서 전송
        int totalChunks = (sendingOptString.Length + CHUNK_SIZE - 1) / CHUNK_SIZE;
        int messageId = UnityEngine.Random.Range(0, 100000); // 메시지 식별자

        for (int i = 0; i < totalChunks; i++)
        {
            int startIndex = i * CHUNK_SIZE;
            int length = Mathf.Min(CHUNK_SIZE, sendingOptString.Length - startIndex);
            string chunk = sendingOptString.Substring(startIndex, length);
            RPC_SendOptChunk(messageId, i, totalChunks, chunk);
        }
    }

    // [FUSION - 추가] 청크를 전송하는 RPC
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_SendOptChunk(int messageId, int chunkIndex, int totalChunks, string chunkData)
    {
        // 청크 수집
        if (!receivedChunks.ContainsKey(messageId))
        {
            receivedChunks[messageId] = new List<string>(new string[totalChunks]);
        }

        receivedChunks[messageId][chunkIndex] = chunkData;

        // 모든 청크가 도착했는지 확인
        bool allReceived = true;
        foreach (var chunk in receivedChunks[messageId])
        {
            if (chunk == null)
            {
                allReceived = false;
                break;
            }
        }

        if (allReceived)
        {
            // 모든 청크 결합
            string fullData = string.Join("", receivedChunks[messageId]);
            receivedChunks.Remove(messageId);

            // 실제 처리 수행
            ProcessOptResult(fullData);
        }
    }

    // ── Z키 최적화 결과 동기화 ──────────────────────────────────────────────
    // 역할: LocalOptimizationRunner(신규 C# 최적화 파이프라인, Z키/컨트롤러 트리거)의
    //       결과를 모든 클라이언트에 전달합니다. 위 RPC_OptResultToClient/
    //       RPC_SendOptChunk는 구 Y키/Python-JSON 파이프라인 전용이라 데이터 포맷이
    //       전혀 달라 별도의 dictionary/RPC로 분리했습니다.
    // 의존: LocalOptimizationRunner.ApplyReceivedOptimizationResult
    // 참고: RpcSources.All이라 host(StateAuthority)가 아닌 클라이언트가 Z를 눌러도
    //       그대로 전체에 브로드캐스트됩니다 -- 위 Y키 경로가 isServer(=house0)일 때만
    //       동작하는 것과 의도적으로 다른 부분입니다.
    // ─────────────────────────────────────────────────────────────────────────
    private Dictionary<int, List<string>> receivedZOptChunks = new Dictionary<int, List<string>>();

    public void RPC_BroadcastZOptResult(string payload)
    {
        int totalChunks = (payload.Length + CHUNK_SIZE - 1) / CHUNK_SIZE;
        int messageId = UnityEngine.Random.Range(0, 100000);

        for (int i = 0; i < totalChunks; i++)
        {
            int startIndex = i * CHUNK_SIZE;
            int length = Mathf.Min(CHUNK_SIZE, payload.Length - startIndex);
            string chunk = payload.Substring(startIndex, length);
            RPC_SendZOptChunk(messageId, i, totalChunks, chunk);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_SendZOptChunk(int messageId, int chunkIndex, int totalChunks, string chunkData)
    {
        if (!receivedZOptChunks.ContainsKey(messageId))
        {
            receivedZOptChunks[messageId] = new List<string>(new string[totalChunks]);
        }

        receivedZOptChunks[messageId][chunkIndex] = chunkData;

        foreach (var chunk in receivedZOptChunks[messageId])
        {
            if (chunk == null) return; // 아직 모든 청크가 도착하지 않음
        }

        string fullData = string.Join("", receivedZOptChunks[messageId]);
        receivedZOptChunks.Remove(messageId);

        var runner = FindObjectOfType<LocalOptimizationRunner>();
        if (runner != null)
        {
            runner.ApplyReceivedOptimizationResult(fullData);
        }
        else
        {
            Debug.LogWarning("[TransferManager] Received Z-key optimization result but no LocalOptimizationRunner found in scene to apply it.");
        }
    }

    /// <summary>
    /// Transform a point from Python's optimized space to local Unity space.
    /// Same logic as OffsetCalculator.TransformToLocalSpace
    /// </summary>
    Vector3 TransformToLocalSpace(Vector3 optimizedPoint, Vector3 myPolygonFinalCentroid, float myRotation)
    {
        // Step 1: Translate so my house center is at origin
        Vector3 translated = optimizedPoint - myPolygonFinalCentroid;

        // Step 2: Rotate by +myRotation around Y axis (Python CCW = Unity negative, so we use positive to undo)
        Quaternion inverseRotation = Quaternion.Euler(0, myRotation, 0);
        Vector3 localPoint = inverseRotation * translated;

        return localPoint;
    }

    /// <summary>
    /// Transform an array of points from optimized space to world space.
    /// My house prefab is at origin, so my freespace center is at myLocalCentroid.
    /// </summary>
    Vector3[] TransformPointsToLocalSpace(Vector3[] points, Vector3 myPolygonFinalCentroid, float myRotation, Vector3 myLocalCentroid)
    {
        Quaternion inverseRotation = Quaternion.Euler(0, myRotation, 0);

        Vector3[] transformedPoints = new Vector3[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            // Note: points have Y=0.15f for visibility, we need to handle this
            Vector3 optimizedPoint = new Vector3(points[i].x, 0, points[i].z);
            Vector3 translated = optimizedPoint - myPolygonFinalCentroid;
            Vector3 relativePoint = inverseRotation * translated;
            // Add myLocalCentroid to convert from relative-to-freespace-center to world position
            Vector3 worldPoint = myLocalCentroid + relativePoint;
            transformedPoints[i] = new Vector3(worldPoint.x, points[i].y, worldPoint.z);
        }
        return transformedPoints;
    }

    // [FUSION - 추가] 실제 opt 결과를 처리하는 함수
    private void ProcessOptResult(string sendingOptString)
    {

        GameObject Characters = GameObject.Find("Characters");
        GameObject myCharacter = GameObject.Find("LocalAvatar");

        // 클라이언트의 type 가져오기 (SceneSelection에서)
        int myType = sceneSelection.GetComponent<SceneSelection>().type;


        // [FUSION - 추가] Arrange_Walkin.isServer 사용
        bool isServerOpt = arrangeMR.GetComponent<Arrange_Walkin>().isServer;
        if (!isServerOpt)
        {
            // Y키 처리(재최적화) 시 B모드 해제 - houses가 계속 이동하는 것을 방지
            sceneSelection.GetComponent<SceneSelection>().modeB = false;
            startSendingVec = false;

            // return to original position
            for (int i = 0; i < arrangeMR.GetComponent<Arrange_Walkin>().houses.Count; i++)
            {
                arrangeMR.GetComponent<Arrange_Walkin>().houses[i].transform.position = Vector3.zero;
                arrangeMR.GetComponent<Arrange_Walkin>().houses[i].transform.rotation = Quaternion.identity;
            }

            // delete previous traverse zones
            GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>();
            foreach (GameObject obj in allObjects)
            {
                if (obj.name.Contains("traverseZone"))
                {
                    Destroy(obj);
                }
            }

            // deactivate sync for remote avatars
            DisableRemoteAvatarSync();


            receivedInfo = JsonConvert.DeserializeObject<List<PythonEachHouse>>(sendingOptString);
            pythonAllHouse = receivedInfo;


            // Get my reference point (my house's final centroid and rotation in optimized space)
            Vector3 myPolygonFinalCentroid = new Vector3(
                pythonAllHouse[myType].polygon.final_centroid.x,
                0,
                pythonAllHouse[myType].polygon.final_centroid.y
            );
            float myRotation = pythonAllHouse[myType].polygon.rotation;

            // My freespace center is at myLocalCentroid in world space (since prefab is at origin)
            Vector3 myLocalCentroid = new Vector3(
                pythonAllHouse[myType].polygon.centroid.x,
                0,
                pythonAllHouse[myType].polygon.centroid.y
            );

            // Apply perspective-based house arrangement
            ApplyHouseArrangementForClient(pythonAllHouse, myType, myPolygonFinalCentroid, myRotation);

            // Apply avatar positions
            ApplyAvatarPositionsForClient(pythonAllHouse, myType, myPolygonFinalCentroid, myRotation, myLocalCentroid, myCharacter, Characters);

            // draw traverse zone - client's myType 기준으로 그림
            DrawTraverseZoneForClient(pythonAllHouse, myType, myPolygonFinalCentroid, myRotation, myLocalCentroid);

            // boundary/ROI outlines, colored per house index -- same convention as
            // LocalOptimizationRunner.cs's C#-only pipeline (mine=cyan, house1=red,
            // house2=blue), so both pipelines look consistent.
            DrawBoundaryAndRoiCirclesForClient(pythonAllHouse, myType, myPolygonFinalCentroid, myRotation, myLocalCentroid);

            // each house's real scanned-room outline, kept alongside (not instead of)
            // the circles above. Reuses Arrange_Walkin.DrawHouseOutlines() (added for
            // the Y-key/server path) since houses[i].transform was just positioned by
            // ApplyHouseArrangementForClient above, the same precondition that method needs.
            arrangeMR.GetComponent<Arrange_Walkin>().DrawHouseOutlines();

            // [OFFSET CALCULATOR] Apply perspective-based offsets for clients
            if (OffsetCalculator.Instance != null && pythonAllHouse != null)
            {
                OffsetCalculator.Instance.ApplyOffsetsFromPythonData(pythonAllHouse);

                // [SCENE SELECTION OFFSET] Enable rotation-aware transform in SceneSelection
                if (sceneSelection != null)
                {
                    sceneSelection.GetComponent<SceneSelection>().SetupRotationAwareTransform(
                        OffsetCalculator.Instance, pythonAllHouse
                    );
                }
            }

        }
    }

    /// <summary>
    /// Disable network sync for remote avatars
    /// </summary>
    void DisableRemoteAvatarSync()
    {
        GameObject remote1 = GameObject.Find("RemoteAvatar1");
        if (remote1 != null)
        {
            var nt1 = remote1.GetComponent<NetworkTransform>();
            if (nt1 != null) nt1.enabled = false;
        }

        GameObject remote = GameObject.Find("RemoteAvatar");
        if (remote != null)
        {
            var nt = remote.GetComponent<NetworkTransform>();
            if (nt != null) nt.enabled = false;
        }
    }

    /// <summary>
    /// Apply perspective-based house arrangement for client.
    /// My house (myType) stays at origin, others are positioned relative to me.
    /// Accounts for centroid offset between prefab pivot and freespace center.
    /// </summary>
    void ApplyHouseArrangementForClient(List<PythonEachHouse> pythonAllHouse, int myType, Vector3 myPolygonFinalCentroid, float myRotation)
    {
        var houses = arrangeMR.GetComponent<Arrange_Walkin>().houses;

        // Get my house's LOCAL centroid (offset from prefab pivot to freespace center)
        // My house prefab stays at (0,0,0), so my freespace center is at myLocalCentroid in world space
        Vector3 myLocalCentroid = new Vector3(
            pythonAllHouse[myType].polygon.centroid.x,
            0,
            pythonAllHouse[myType].polygon.centroid.y
        );

        for (int i = 0; i < pythonAllHouse.Count; i++)
        {
            if (i == myType)
            {
                // My house: prefab at origin, no rotation
                houses[i].transform.position = Vector3.zero;
                houses[i].transform.rotation = Quaternion.identity;
                continue;
            }

            // Get this house's final centroid in optimized space
            Vector3 houseFinalCentroid = new Vector3(
                pythonAllHouse[i].polygon.final_centroid.x,
                0,
                pythonAllHouse[i].polygon.final_centroid.y
            );
            float houseRotation = pythonAllHouse[i].polygon.rotation;

            // Transform to my local space (where this house's freespace center should be)
            Vector3 localPosition = TransformToLocalSpace(houseFinalCentroid, myPolygonFinalCentroid, myRotation);

            // Calculate relative rotation
            float localRotation = houseRotation - myRotation;

            // Get this house's LOCAL centroid (offset from prefab pivot to freespace center)
            Vector3 localCentroid = new Vector3(
                pythonAllHouse[i].polygon.centroid.x,
                0,
                pythonAllHouse[i].polygon.centroid.y
            );

            // Apply rotation first
            // Use -localRotation to match Python's coordinate system (Python CCW = Unity negative Y-rotation)
            // This ensures consistency with Arrange_WalkIn.cs
            houses[i].transform.rotation = Quaternion.Euler(0, -localRotation, 0);

            // The local centroid rotates with the prefab
            Vector3 rotatedLocalCentroid = houses[i].transform.rotation * localCentroid;

            // Target world position for this house's freespace center:
            // My freespace center is at myLocalCentroid (since my prefab is at origin)
            // This house's freespace center should be at: myLocalCentroid + localPosition (relative offset)
            Vector3 targetWorldCentroid = myLocalCentroid + localPosition;

            // Position prefab so that its rotated centroid ends up at targetWorldCentroid
            houses[i].transform.position = targetWorldCentroid - rotatedLocalCentroid;
        }
    }

    /// <summary>
    /// Apply avatar positions for client using correct coordinate transformation.
    /// My house prefab is at origin, so my freespace center is at myLocalCentroid.
    /// </summary>
    void ApplyAvatarPositionsForClient(List<PythonEachHouse> pythonAllHouse, int myType, Vector3 myPolygonFinalCentroid, float myRotation, Vector3 myLocalCentroid, GameObject myCharacter, GameObject Characters)
    {
        Quaternion inverseRotation = Quaternion.Euler(0, myRotation, 0);

        // Find remote avatars
        GameObject remote1 = GameObject.Find("RemoteAvatar1");
        GameObject remote = GameObject.Find("RemoteAvatar");

        // Get remote indices based on my type (matches OffsetCalculator.GetRemoteIndices)
        // RemoteAvatar1 = first to connect, RemoteAvatar = second to connect
        int remoteIndex, remote1Index;
        switch (myType)
        {
            case 0:
                remoteIndex = 2;   // RemoteAvatar = id2 (second to connect)
                remote1Index = 1;  // RemoteAvatar1 = id1 (first to connect)
                break;
            case 1:
                remoteIndex = 2;   // RemoteAvatar = id2 (second to connect)
                remote1Index = 0;  // RemoteAvatar1 = id0/server (first to connect)
                break;
            case 2:
            default:
                remoteIndex = 1;   // RemoteAvatar = id1 (second to connect)
                remote1Index = 0;  // RemoteAvatar1 = id0/server (first to connect)
                break;
        }

        for (int i = 0; i < pythonAllHouse.Count; i++)
        {
            // Get avatar position in optimized space
            Vector3 avatarOptimizedPos = new Vector3(
                pythonAllHouse[i].boundary.final_centroid.x,
                0,
                pythonAllHouse[i].boundary.final_centroid.y
            );

            // Transform to position relative to my freespace center
            Vector3 translated = avatarOptimizedPos - myPolygonFinalCentroid;
            Vector3 relativePosition = inverseRotation * translated;

            // Convert to world position (add myLocalCentroid offset)
            Vector3 worldPosition = myLocalCentroid + relativePosition;

            if (i == myType)
            {
                // Position my local avatar
                if (myCharacter != null)
                {
                    myCharacter.transform.position = worldPosition;
                }

                // Use direct method for Y key initialization (no filtering)
                regionsObj.GetComponent<Regions>().SetUserPositionDirect(i, worldPosition);
            }
            else
            {
                // Position remote avatars based on mapping
                if (i == remoteIndex && remote != null)
                {
                    remote.transform.position = worldPosition;
                }
                else if (i == remote1Index && remote1 != null)
                {
                    remote1.transform.position = worldPosition;
                }

                // Also update the Character placeholder
                if (Characters != null && i < Characters.transform.childCount)
                {
                    Characters.transform.GetChild(i).transform.position = worldPosition;
                }

                // Use direct method for Y key initialization (no filtering)
                regionsObj.GetComponent<Regions>().SetUserPositionDirect(i, worldPosition);
            }
        }
    }

    /// <summary>
    /// Draw traverse zone for client with proper coordinate transformation.
    /// My house prefab is at origin, so my freespace center is at myLocalCentroid.
    /// </summary>
    void DrawTraverseZoneForClient(List<PythonEachHouse> pythonAllHouse, int myType, Vector3 myPolygonFinalCentroid, float myRotation, Vector3 myLocalCentroid)
    {
        // draw traverse zone for my house
        if (pythonAllHouse[myType].traverse.IsNested)
        {
            for (int j = 0; j < pythonAllHouse[myType].traverse.NestedCoords.Count; j++)
            {
                // traverse zone object
                GameObject traverseZone = new GameObject($"traverseZone_{j}");
                Vector3[] points = ConvertToVector3Array(pythonAllHouse[myType].traverse.NestedCoords[j]);

                // Transform points to world space
                points = TransformPointsToLocalSpace(points, myPolygonFinalCentroid, myRotation, myLocalCentroid);

                DrawZone(traverseZone, points);
            }
        }
        else
        {
            // connected zone
            GameObject traverseZone = new GameObject($"traverseZone");
            Vector3[] points = ConvertToVector3Array(pythonAllHouse[myType].traverse.SimpleCoords);

            // Transform points to world space
            points = TransformPointsToLocalSpace(points, myPolygonFinalCentroid, myRotation, myLocalCentroid);

            DrawZone(traverseZone, points);
        }
    }

    /// <summary>
    /// Convert 2D coords to 3D Vector3 array
    /// </summary>
    Vector3[] ConvertToVector3Array(IReadOnlyList<float[]> coords, float height = 0.15f)
    {
        Vector3[] points = new Vector3[coords.Count];
        for (int i = 0; i < coords.Count; i++)
        {
            points[i] = new Vector3(coords[i][0], height, coords[i][1]);
        }
        return points;
    }

    /// <summary>
    /// Draw zone using LineRenderer
    /// </summary>
    void DrawZone(GameObject addToWhichGameobj, Vector3[] points, Color color = default)
    {
        if (color == default) color = Color.green;

        LineRenderer lineRenderer = addToWhichGameobj.AddComponent<LineRenderer>();

        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
        lineRenderer.widthMultiplier = 0.015f;
        lineRenderer.positionCount = points.Length;

        lineRenderer.SetPositions(points);
    }

    /// <summary>
    /// Which color a house's outlines (boundary circle + ROI circle) get, by
    /// absolute house index -- matches LocalOptimizationRunner.cs's HouseOutlineColor
    /// so the C#-only and Python pipelines look the same.
    /// </summary>
    static Color HouseOutlineColor(int i)
    {
        if (i == 1) return Color.red;
        if (i == 2) return Color.blue;
        return Color.cyan;
    }

    /// <summary>
    /// Builds a circle of points (in the same "final"/optimized space as
    /// polygon.final_centroid) approximating a circle -- used for the ROI circle,
    /// which Python doesn't send explicitly (it's always a fixed 0.5m circle
    /// concentric with the boundary circle, same as CSharpPort/CircleShape.cs's
    /// convention), so we can build it locally instead of needing a Python-side change.
    /// </summary>
    Vector3[] GenerateCirclePoints(float centerX, float centerY, float radius, float height, int numPoints = 32)
    {
        Vector3[] points = new Vector3[numPoints + 1];
        for (int i = 0; i <= numPoints; i++)
        {
            float theta = i * (2f * Mathf.PI / numPoints);
            points[i] = new Vector3(centerX + Mathf.Cos(theta) * radius, height, centerY + Mathf.Sin(theta) * radius);
        }
        return points;
    }

    const float RoiRadius = 0.5f; // per the paper, matches CSharpPort/LocalOptimizationRunner.cs

    /// <summary>
    /// Draws each house's boundary-circle outline (from Python's boundary.coords)
    /// and its ROI circle (built locally, see GenerateCirclePoints), colored per
    /// house index. This is the Python-pipeline equivalent of
    /// LocalOptimizationRunner.cs's DrawBoundaryCirclesForMe/DrawROICirclesForMe --
    /// was previously missing here, so only the green traverse zone was ever visible.
    /// </summary>
    void DrawBoundaryAndRoiCirclesForClient(List<PythonEachHouse> pythonAllHouse, int myType, Vector3 myPolygonFinalCentroid, float myRotation, Vector3 myLocalCentroid)
    {
        foreach (GameObject obj in GameObject.FindObjectsOfType<GameObject>())
        {
            if (obj.name.Contains("boundaryCircle") || obj.name.Contains("roiCircle")) Destroy(obj);
        }

        for (int i = 0; i < pythonAllHouse.Count; i++)
        {
            Color color = HouseOutlineColor(i);

            Vector3[] boundaryPoints = ConvertToVector3Array(pythonAllHouse[i].boundary.coords, 0.16f);
            boundaryPoints = TransformPointsToLocalSpace(boundaryPoints, myPolygonFinalCentroid, myRotation, myLocalCentroid);
            GameObject boundaryObj = new GameObject(i == myType ? "boundaryCircle_mine_" + i : "boundaryCircle_" + i);
            DrawZone(boundaryObj, boundaryPoints, color);

            Vector3[] roiPoints = GenerateCirclePoints(
                pythonAllHouse[i].boundary.final_centroid.x, pythonAllHouse[i].boundary.final_centroid.y, RoiRadius, 0.17f);
            roiPoints = TransformPointsToLocalSpace(roiPoints, myPolygonFinalCentroid, myRotation, myLocalCentroid);
            GameObject roiObj = new GameObject(i == myType ? "roiCircle_mine_" + i : "roiCircle_" + i);
            DrawZone(roiObj, roiPoints, color);
        }
    }


    // ===================== AVATAR INDEX RPC ===================== //

    // [FUSION - 추가] 서버가 클라이언트들에게 아바타 인덱스를 설정하도록 요청하는 RPC
    // 서버에서 호출: SetAvatarIndexForClients(new int[] {0, 5, 10}) -> 유저0은 인덱스0, 유저1은 인덱스5, 유저2는 인덱스10
    public void SetAvatarIndexForClients(int[] indices)
    {
        bool isServerAvatar = arrangeMR.GetComponent<Arrange_Walkin>().isServer;
        if (!isServerAvatar) return;

        avatarIndexForUsers = indices;
        // 모든 클라이언트에게 브로드캐스트
        RPC_BroadcastAvatarIndices(indices[0], indices[1], indices[2]);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastAvatarIndices(int index0, int index1, int index2)
    {
        // SceneSelection의 type을 가져와서 내 인덱스 결정
        var sceneSelComp = sceneSelection.GetComponent<SceneSelection>();
        int myType = sceneSelComp.type;
        int myIndex = myType == 0 ? index0 : (myType == 1 ? index1 : index2);

        // 내 LocalAvatar의 인덱스 설정
        GameObject localAvatar = GameObject.Find("LocalAvatar");
        if (localAvatar != null)
        {
            var avatarBehaviour = localAvatar.GetComponent<Meta.XR.MultiplayerBlocks.Fusion.AvatarBehaviourFusion>();
            if (avatarBehaviour != null && avatarBehaviour.Object.HasStateAuthority)
            {
                avatarBehaviour.LocalAvatarIndex = myIndex;
            }
        }
    }


    // ===================== MODE SYNC RPC (A,B 키) ===================== //

    // [FUSION - 추가] 서버에서 모드 변경 후 모든 클라이언트에게 브로드캐스트
    public void SetModeAndBroadcast(bool isWalkinMode)
    {
        bool isServer = arrangeMR.GetComponent<Arrange_Walkin>().isServer;
        if (!isServer) return;

        // 서버 자신도 직접 적용
        ApplyMode(isWalkinMode);

        // 클라이언트들에게 RPC로 전송
        RPC_SyncMode(isWalkinMode);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_SyncMode(bool isWalkinMode)
    {
        bool isServer = arrangeMR.GetComponent<Arrange_Walkin>().isServer;

        // 서버는 이미 SetModeAndBroadcast에서 적용했으므로 클라이언트만 처리
        if (isServer) return;

        ApplyMode(isWalkinMode);
    }

    // 실제 모드 적용 함수
    private void ApplyMode(bool isWalkinMode)
    {
        // walkin 모드 설정 (TransferManager)
        walkin = isWalkinMode;

        // B 모드일 때 startSendingVec도 설정 (기존에는 B키 직접 누를 때 설정됨)
        if (!isWalkinMode)
        {
            startSendingVec = true;
        }

        // SceneSelection의 OnModeA() 또는 OnModeB() 호출
        var sceneSelComp = sceneSelection.GetComponent<SceneSelection>();
        if (sceneSelComp != null)
        {
            if (isWalkinMode)
            {
                sceneSelComp.OnModeA();  // A키: freeze mode
            }
            else
            {
                sceneSelComp.OnModeB();  // B키: 거리계산 mode
            }
        }
    }

}

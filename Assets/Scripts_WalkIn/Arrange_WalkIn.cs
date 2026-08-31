using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Linq;
using AvatarBehaviourFusion = Meta.XR.MultiplayerBlocks.Fusion.AvatarBehaviourFusion;
using Meta.XR.MultiplayerBlocks.Fusion;
using Oculus.Skinning.GpuSkinning;
// [NETCODE - 주석처리] using Meta.XR.MultiplayerBlocks.NGO;
// [FUSION - 추가]
using Fusion;
using NetworkTransform = Fusion.NetworkTransform;
using NetTopologySuite.Geometries;

public class Arrange_Walkin : MonoBehaviour
{
    bool IsValueempted = true;
    public List<GameObject> houses;
    public int chooseHouseNum;

    // users to populate
    public List<GameObject> users;


    // get voronoi region from Regions
    public GameObject regionsObj;

    // check anchor polygon points fed
    public bool characterInitialized = false;

    // AvatarCullingModule
    //public AvatarCullingModule avatarCullingModule;

    public GameObject sceneSelection;

    // ====================== added for walkin ======================
    private List<PythonEachHouse> pythonAllHouse;
    public List<List<Vector3>> selectedZones = new List<List<Vector3>>();
    public bool receivedZones = false;
    private List<PythonEachHouse> receivedInfo;

    public Sender sender;
    public string jsonPath;
    public bool visUpdated = false;
    public string sendingOptString = "";


    // added for circle
    public float radius = 0.9f;
    public int segments = 360;
    public float lineWidth = 0.01f;
    public Color lineColor = Color.green;
    private LineRenderer lineRenderer;

    private bool circleDrawn = false;

    public GameObject transfer;
    public bool isServer = false;

    // [OFFSET CALCULATOR] Now uses singleton: OffsetCalculator.Instance

    void Awake()
    {
        // Runtime-attached (no scene wiring needed): derives our house index from
        // Fusion join order. Does not hide or change anything visually -- see
        // HouseJoinOrderAssigner's own header comment.
        var assigner = gameObject.AddComponent<HouseJoinOrderAssigner>();
        assigner.arrangeWalkin = this;
    }

    void Start()
    {
        // =====================================================
        // INITIAL STATE:
        // - Read JSON for shader setup (zone boundaries for Voronoi rendering)
        // - But keep all houses at (0,0,0) with no transforms
        // - Avatars spawn at (0,0,0)
        // =====================================================

        jsonPath = Application.dataPath + "/Tayoptoutput/polygon_transformations.json";

        if (File.Exists(jsonPath))
        {
            string jsonToRead = File.ReadAllText(jsonPath);
            receivedInfo = JsonConvert.DeserializeObject<List<PythonEachHouse>>(jsonToRead);
            pythonAllHouse = receivedInfo;

            // DON'T apply house transforms at startup
            // Just ensure all houses are at origin
            for (int i = 0; i < houses.Count; i++)
            {
                houses[i].transform.position = Vector3.zero;
                houses[i].transform.rotation = Quaternion.identity;
            }

            // Build selected zones for shader (Voronoi visibility)
            BuildSelectedZones(pythonAllHouse);
        }

        receivedZones = true;


        GameObject Characters = new GameObject("Characters");
        Characters.transform.position = Vector3.zero;
        Characters.transform.parent = this.transform;


        // Create character placeholders at origin
        for (int i = 0; i < users.Count; i++)
        {
            GameObject copyedCharacter = GameObject.Instantiate(users[i].gameObject);
            copyedCharacter.transform.position = Vector3.zero;  // All at origin initially
            copyedCharacter.transform.parent = Characters.transform;
        }

        // Initialize Regions userPosVec4 with all users at origin
        if (regionsObj != null)
        {
            var regions = regionsObj.GetComponent<Regions>();
            for (int i = 0; i < regions.userPosVec4.Length; i++)
            {
                // Use direct method for initialization (no filtering)
                regions.SetUserPositionDirect(i, Vector3.zero);
            }
        }

        // DON'T apply OffsetCalculator offsets at startup
        // Offsets will be calculated when Y key is pressed (after Python optimization)

        characterInitialized = true;

    }

    /// <summary>
    /// Apply perspective-based house arrangement.
    /// My house (chooseHouseNum) stays at origin, others are positioned relative to me.
    /// Uses OffsetCalculator's coordinate transformation logic.
    /// </summary>
    void ApplyHouseArrangement(List<PythonEachHouse> pythonAllHouse, int myId)
    {
        // Get my reference point (my house's final centroid and rotation in optimized space)
        Vector3 myPolygonFinalCentroid = new Vector3(
            pythonAllHouse[myId].polygon.final_centroid.x,
            0,
            pythonAllHouse[myId].polygon.final_centroid.y
        );
        float myRotation = pythonAllHouse[myId].polygon.rotation;

        // Get my house's LOCAL centroid (offset from prefab pivot to freespace center)
        // My house prefab stays at (0,0,0), so my freespace center is at myLocalCentroid in world space
        Vector3 myLocalCentroid = new Vector3(
            pythonAllHouse[myId].polygon.centroid.x,
            0,
            pythonAllHouse[myId].polygon.centroid.y
        );

        for (int i = 0; i < pythonAllHouse.Count; i++)
        {
            if (i == myId)
            {
                // My house: prefab at origin, no rotation
                houses[i].transform.position = Vector3.zero;
                houses[i].transform.rotation = Quaternion.identity;

                // Draw traverse zone for my house
                DrawTraverseZoneForHouse(pythonAllHouse, i);
                continue;
            }

            // Reset house to origin first
            houses[i].transform.position = Vector3.zero;
            houses[i].transform.rotation = Quaternion.identity;

            // Get this house's final centroid in optimized space
            Vector3 houseFinalCentroid = new Vector3(
                pythonAllHouse[i].polygon.final_centroid.x,
                0,
                pythonAllHouse[i].polygon.final_centroid.y
            );
            float houseRotation = pythonAllHouse[i].polygon.rotation;

            // Transform to my local space:
            // 1. Translate so my house center is at origin
            Vector3 translated = houseFinalCentroid - myPolygonFinalCentroid;

            // 2. Rotate by +myRotation around Y axis (Python CCW = Unity negative, so we use positive to undo)
            Quaternion inverseRotation = Quaternion.Euler(0, myRotation, 0);
            Vector3 localPosition = inverseRotation * translated;

            // 3. Calculate relative rotation
            float localRotation = houseRotation - myRotation;

            // Get this house's LOCAL centroid (offset from prefab pivot to freespace center)
            Vector3 localCentroid = new Vector3(
                pythonAllHouse[i].polygon.centroid.x,
                0,
                pythonAllHouse[i].polygon.centroid.y
            );

            // Apply rotation first
            // Use localRotation (not -localRotation) to match the perspective transform direction
            // The traverse zone points are rotated by -myRotation, so the house should also appear
            // rotated by (houseRotation - myRotation) in the same direction
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
    /// Draw traverse zone for a specific house
    /// </summary>
    void DrawTraverseZoneForHouse(List<PythonEachHouse> pythonAllHouse, int houseIndex)
    {
        if (pythonAllHouse[houseIndex].traverse.IsNested)
        {
            for (int j = 0; j < pythonAllHouse[houseIndex].traverse.NestedCoords.Count; j++)
            {
                GameObject traverseZone = new GameObject($"traverseZone_{j}");
                Vector3[] points = ConvertToVector3Array(pythonAllHouse[houseIndex].traverse.NestedCoords[j]);

                // Transform traverse zone coordinates to local space
                points = TransformPointsToLocalSpace(points, pythonAllHouse, chooseHouseNum);

                DrawZone(traverseZone, points);
            }
        }
        else
        {
            GameObject traverseZone = new GameObject($"traverseZone");
            Vector3[] points = ConvertToVector3Array(pythonAllHouse[houseIndex].traverse.SimpleCoords);

            // Transform traverse zone coordinates to local space
            points = TransformPointsToLocalSpace(points, pythonAllHouse, chooseHouseNum);

            DrawZone(traverseZone, points);
        }
    }

    /// <summary>
    /// Transform an array of points from optimized space to world space.
    /// My house prefab is at origin, so my freespace center is at myLocalCentroid.
    /// Points are transformed relative to my freespace center, then offset to world space.
    /// </summary>
    Vector3[] TransformPointsToLocalSpace(Vector3[] points, List<PythonEachHouse> pythonAllHouse, int myId)
    {
        Vector3 myPolygonFinalCentroid = new Vector3(
            pythonAllHouse[myId].polygon.final_centroid.x,
            0,
            pythonAllHouse[myId].polygon.final_centroid.y
        );
        float myRotation = pythonAllHouse[myId].polygon.rotation;
        Quaternion inverseRotation = Quaternion.Euler(0, myRotation, 0);

        // My freespace center is at myLocalCentroid in world space (since prefab is at origin)
        Vector3 myLocalCentroid = new Vector3(
            pythonAllHouse[myId].polygon.centroid.x,
            0,
            pythonAllHouse[myId].polygon.centroid.y
        );

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

    /// <summary>
    /// Build selected zones list for visualization
    /// </summary>
    void BuildSelectedZones(List<PythonEachHouse> pythonAllHouse)
    {
        selectedZones.Clear();
        for (int i = 0; i < pythonAllHouse.Count; i++)
        {
            List<Vector3> eachSelectedZones = new List<Vector3>();
            for (int j = 0; j < pythonAllHouse[i].boundary.coords.Length; j++)
            {
                float x = pythonAllHouse[i].boundary.coords[j][0];
                float z = pythonAllHouse[i].boundary.coords[j][1];
                Vector3 zonePoints = new Vector3(x, 0, z);
                eachSelectedZones.Add(zonePoints);
            }
            selectedZones.Add(eachSelectedZones);
        }
    }


    // Update is called once per frame
    void Update()
    {

        GameObject Characters = GameObject.Find("Characters");
        GameObject myCharacter = GameObject.Find("LocalAvatar");


        if (Input.GetKeyDown(KeyCode.Y) && visUpdated == false)
        {
            IsValueempted = false;

            // Reset modeB when Y is pressed (new optimization resets the mode)
            if (sceneSelection != null)
            {
                sceneSelection.GetComponent<SceneSelection>().modeB = false;
                if (ModeBCalculator.Instance != null)
                {
                    ModeBCalculator.Instance.OnModeBEnd();
                }
            }

            circleDrawn = false;
            regionsObj.GetComponent<Regions>().receiveFromPython = true;
            sender.SendToPython("Y");

            string jsonContent = File.ReadAllText(jsonPath);
            sendingOptString = jsonContent;



            // Reset all houses to default pos + rot first
            for (int i = 0; i < houses.Count; i++)
            {
                houses[i].transform.position = Vector3.zero;
                houses[i].transform.rotation = Quaternion.identity;
            }



            // delete previous traverse zone lines
            GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>();
            foreach (GameObject obj in allObjects)
            {
                if (obj.name.Contains("traverseZone"))
                {
                    DestroyImmediate(obj);
                }
            }

            // deactivate network sync for remote avatars
            DisableRemoteAvatarSync();



            // ======================================= reflect opt result ======================================= //
            receivedInfo = JsonConvert.DeserializeObject<List<PythonEachHouse>>(jsonContent);
            pythonAllHouse = receivedInfo;

            // Apply perspective-based house arrangement
            ApplyHouseArrangement(pythonAllHouse, chooseHouseNum);

            // Apply avatar positions using OffsetCalculator
            ApplyAvatarPositions(pythonAllHouse, chooseHouseNum, myCharacter, Characters);

            // Draw traverse zone
            Vector3 debugFinalCentroid = new Vector3(pythonAllHouse[chooseHouseNum].polygon.final_centroid.x, 0, pythonAllHouse[chooseHouseNum].polygon.final_centroid.y);
            Vector3 debugLocalCentroid = new Vector3(pythonAllHouse[chooseHouseNum].polygon.centroid.x, 0, pythonAllHouse[chooseHouseNum].polygon.centroid.y);
            drawTraverseZone(pythonAllHouse, chooseHouseNum);

            // colored boundary/ROI circle outlines for all 3 houses (mine=cyan,
            // house1=red, house2=blue) -- see DrawBoundaryAndRoiCircles for why
            // this was missing here
            DrawBoundaryAndRoiCircles(pythonAllHouse, chooseHouseNum);

            // each house's real scanned-room outline, kept alongside (not instead
            // of) the circles above -- see DrawHouseOutlines
            DrawHouseOutlines();


            // propagate to clients
            if (isServer == true)
            {
                // [FUSION - 추가]
                transfer.GetComponent<TransferManager>().RPC_OptResultToClient(sendingOptString);

                // Auto-freeze after optimization to lock Voronoi anchors at JSON positions
                // This prevents live position updates from overwriting the optimized positions
                transfer.GetComponent<TransferManager>().SetModeAndBroadcast(true);
            }

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
                else
                {
                    Debug.LogError("[Arrange_WalkIn] sceneSelection is NULL! Cannot set optimization offsets.");
                }
            }
            else
            {
                Debug.LogError($"[Arrange_WalkIn] Cannot apply offsets: OffsetCalculator.Instance={OffsetCalculator.Instance != null}, pythonAllHouse={pythonAllHouse != null}");
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
    /// Apply avatar positions using coordinate transformation.
    /// My house prefab is at origin, so my freespace center is at myLocalCentroid.
    /// Avatar positions are transformed relative to my freespace center, then offset to world space.
    /// </summary>
    void ApplyAvatarPositions(List<PythonEachHouse> pythonAllHouse, int myId, GameObject myCharacter, GameObject Characters)
    {
        // Get my reference point
        Vector3 myPolygonFinalCentroid = new Vector3(
            pythonAllHouse[myId].polygon.final_centroid.x,
            0,
            pythonAllHouse[myId].polygon.final_centroid.y
        );
        float myRotation = pythonAllHouse[myId].polygon.rotation;
        Quaternion inverseRotation = Quaternion.Euler(0, myRotation, 0);

        // My freespace center is at myLocalCentroid in world space (since prefab is at origin)
        Vector3 myLocalCentroid = new Vector3(
            pythonAllHouse[myId].polygon.centroid.x,
            0,
            pythonAllHouse[myId].polygon.centroid.y
        );

        // Find remote avatars
        GameObject remote1 = GameObject.Find("RemoteAvatar1");
        GameObject remote = GameObject.Find("RemoteAvatar");

        // Get remote indices based on my id (matches OffsetCalculator.GetRemoteIndices)
        // RemoteAvatar1 = first to connect, RemoteAvatar = second to connect
        int remoteIndex, remote1Index;
        switch (myId)
        {
            case 0:
            default:
                remoteIndex = 2;   // RemoteAvatar = id2 (second to connect)
                remote1Index = 1;  // RemoteAvatar1 = id1 (first to connect)
                break;
            case 1:
                remoteIndex = 2;   // RemoteAvatar = id2 (second to connect)
                remote1Index = 0;  // RemoteAvatar1 = id0/server (first to connect)
                break;
            case 2:    
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

            if (i == myId)
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


    public Vector3[] ConvertToVector3Array(IReadOnlyList<float[]> coords, float height = 0.1f)
    {
        Vector3[] points = new Vector3[coords.Count];
        for (int i = 0; i < coords.Count; i++)
        {
            // Assuming coords[i] contains [x, y]. Using y for z to convert 2D to 3D
            points[i] = new Vector3(coords[i][0], height, coords[i][1]);
        }
        return points;
    }


    public SharedSpacePoints DrawZone(GameObject addToWhichGameobj, Vector3[] points, Color color = default, float width = 0.015f)
    {
        if (color == default) color = Color.green;

        SharedSpacePoints sharedspacePoints = new SharedSpacePoints();
        LineRenderer lineRenderer = new LineRenderer();
        lineRenderer = addToWhichGameobj.AddComponent<LineRenderer>();

        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
        lineRenderer.widthMultiplier = width;
        lineRenderer.positionCount = points.Length;

        lineRenderer.SetPositions(points);
        sharedspacePoints.exteriorPoints = points.ToList();

        return sharedspacePoints;
    }

    /// <summary>
    /// Which color a house's outlines (boundary circle + ROI circle) get, by
    /// absolute house index -- matches TransferManager.cs's/LocalOptimizationRunner.cs's
    /// HouseOutlineColor so all three drawing paths agree on which house is which.
    /// </summary>
    static Color HouseOutlineColor(int i)
    {
        if (i == 1) return Color.red;
        if (i == 2) return Color.blue;
        return Color.cyan;
    }

    const float RoiRadius = 0.5f; // per the paper, matches CSharpPort/LocalOptimizationRunner.cs

    /// <summary>
    /// Builds a circle of points approximating the fixed 0.5m ROI circle concentric
    /// with the boundary circle -- same convention as TransferManager.cs's
    /// GenerateCirclePoints (Python doesn't send this circle explicitly).
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

    /// <summary>
    /// Draws each house's boundary-circle outline (from Python's boundary.coords)
    /// and its ROI circle, colored per house index (mine=cyan, house1=red,
    /// house2=blue). This is the server-side/whoever-pressed-Y equivalent of
    /// TransferManager.cs's DrawBoundaryAndRoiCirclesForClient -- that one only
    /// runs for clients receiving the RPC'd opt result, so the person who actually
    /// pressed Y never got these outlines drawn locally, only the green traverse
    /// zone from drawTraverseZone().
    /// </summary>
    public void DrawBoundaryAndRoiCircles(List<PythonEachHouse> pythonAllHouse, int myId)
    {
        foreach (GameObject obj in GameObject.FindObjectsOfType<GameObject>())
        {
            if (obj.name.Contains("boundaryCircle") || obj.name.Contains("roiCircle")) DestroyImmediate(obj);
        }

        for (int i = 0; i < pythonAllHouse.Count; i++)
        {
            Color color = HouseOutlineColor(i);

            Vector3[] boundaryPoints = ConvertToVector3Array(pythonAllHouse[i].boundary.coords, 0.16f);
            boundaryPoints = TransformPointsToLocalSpace(boundaryPoints, pythonAllHouse, myId);
            GameObject boundaryObj = new GameObject(i == myId ? "boundaryCircle_mine_" + i : "boundaryCircle_" + i);
            DrawZone(boundaryObj, boundaryPoints, color);

            Vector3[] roiPoints = GenerateCirclePoints(
                pythonAllHouse[i].boundary.final_centroid.x, pythonAllHouse[i].boundary.final_centroid.y, RoiRadius, 0.17f);
            roiPoints = TransformPointsToLocalSpace(roiPoints, pythonAllHouse, myId);
            GameObject roiObj = new GameObject(i == myId ? "roiCircle_mine_" + i : "roiCircle_" + i);
            DrawZone(roiObj, roiPoints, color);
        }
    }

    const string HouseOutlineRoomId = "2"; // matches CSharpPort/LocalOptimizationRunner.cs's RoomId
    const float HouseOutlineWidth = 0.035f; // thicker than the default 0.015f line width, per user request

    /// <summary>
    /// Draws each house's actual scanned-room shape (wall-derived outline, no
    /// furniture cutouts -- same "one boundary line" scope as drawTraverseZone),
    /// colored per house index. Drawn IN ADDITION to the boundary/ROI circles
    /// above, not a replacement -- those are a fixed-radius stand-in for the
    /// individual area, this is what the room actually looks like.
    /// Python's JSON never sends raw room-polygon coordinates (only boundary's
    /// circle-approximation coords -- confirmed by reading differential.py),
    /// so this is computed locally with the same CSharpPort/RoomPolygonExtractor
    /// used by the Z-key pipeline, then placed with houses[i].transform.TransformPoint
    /// -- since ApplyHouseArrangement already positioned that transform correctly,
    /// this guarantees the outline lines up with wherever the real house prefab is,
    /// without re-deriving Python's rotation/translation math a second time.
    /// </summary>
    public void DrawHouseOutlines()
    {
        foreach (GameObject obj in GameObject.FindObjectsOfType<GameObject>())
        {
            if (obj.name.Contains("houseOutline")) DestroyImmediate(obj);
        }

        for (int i = 0; i < houses.Count; i++)
        {
            HouseData house = HouseLoader.LoadHouseByIndex(i);
            Polygon wallPolygon = RoomPolygonExtractor.GetPolygonWithWalls(house, HouseOutlineRoomId);
            if (wallPolygon == null) continue;

            Coordinate[] ring = wallPolygon.ExteriorRing.Coordinates;
            var points = new Vector3[ring.Length];
            for (int k = 0; k < ring.Length; k++)
            {
                Vector3 localPoint = new Vector3((float)ring[k].X, 0, (float)ring[k].Y);
                Vector3 worldPoint = houses[i].transform.TransformPoint(localPoint);
                // above boundary (0.16f) and ROI (0.17f) circles so all three don't z-fight
                points[k] = new Vector3(worldPoint.x, 0.18f, worldPoint.z);
            }

            GameObject outlineObj = new GameObject(i == chooseHouseNum ? "houseOutline_mine_" + i : "houseOutline_" + i);
            DrawZone(outlineObj, points, HouseOutlineColor(i), HouseOutlineWidth);
        }
    }



    public void drawTraverseZone(List<PythonEachHouse> pythonAllHouse, int forWhichHouse)
    {
        // Get my reference for transformation
        Vector3 myPolygonFinalCentroid = new Vector3(
            pythonAllHouse[chooseHouseNum].polygon.final_centroid.x,
            0,
            pythonAllHouse[chooseHouseNum].polygon.final_centroid.y
        );
        float myRotation = pythonAllHouse[chooseHouseNum].polygon.rotation;

        // draw traverse zone
        if (pythonAllHouse[forWhichHouse].traverse.IsNested)
        {
            for (int j = 0; j < pythonAllHouse[forWhichHouse].traverse.NestedCoords.Count; j++)
            {
                // traverse zone object
                GameObject traverseZone = new GameObject($"traverseZone_{j}");
                Vector3[] points = ConvertToVector3Array(pythonAllHouse[forWhichHouse].traverse.NestedCoords[j]);

                // Transform points to local space
                points = TransformPointsToLocalSpace(points, pythonAllHouse, chooseHouseNum);

                DrawZone(traverseZone, points);
            }


        }
        else
        {
            // connected zone
            GameObject traverseZone = new GameObject($"traverseZone");
            Vector3[] points = ConvertToVector3Array(pythonAllHouse[forWhichHouse].traverse.SimpleCoords);

            // Transform points to local space
            points = TransformPointsToLocalSpace(points, pythonAllHouse, chooseHouseNum);

            DrawZone(traverseZone, points);
        }
    }



}

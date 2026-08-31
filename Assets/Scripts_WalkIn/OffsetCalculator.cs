using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// OffsetCalculator: Calculates and applies perspective-based transforms for multi-user VR system.
///
/// Core Concept:
/// - Each participant sees their own house at Unity origin (0,0,0) with no rotation
/// - All other positions (houses, avatars) are transformed relative to the local user's perspective
/// - The Python optimization outputs absolute positions in "optimized space"
/// - We transform these to each participant's local space
///
/// Coordinate Transform:
/// optimizedPoint -> localPoint:
///   1. Translate: point - myPolygonFinalCentroid (makes my house center at origin)
///   2. Rotate: by +myRotation (Python CCW = Unity negative, so positive undoes the rotation)
///
/// ID Mapping (based on chooseHouseNum from Arrange_WalkIn):
/// - id==0 (server): local=house0, sees house1 and house2 as remote
/// - id==1: local=house1, sees house0 and house2 as remote
/// - id==2: local=house2, sees house0 and house1 as remote
/// </summary>
public class OffsetCalculator : MonoBehaviour
{
    // Singleton instance
    public static OffsetCalculator Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[OffsetCalculator] Multiple instances detected. Destroying duplicate.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    [Header("Identity")]
    [Tooltip("Set from Arrange_WalkIn.chooseHouseNum - identifies which house/user this is")]
    public int id = 0;

    [Header("References")]
    public GameObject arrangeMR;  // Reference to Arrange object
    public GameObject sceneSelection;  // Reference to SceneSelection

    [Header("Debug - My Reference Point")]
    public Vector3 myPolygonFinalCentroid = Vector3.zero;  // My house center in optimized space
    public float myRotation = 0f;  // My house rotation in optimized space
    public Vector3 myLocalCentroid = Vector3.zero;  // My freespace center in world space (prefab at origin)

    [Header("Debug - Calculated Target Positions")]
    public Vector3 localAvatarTarget = Vector3.zero;  // Where my avatar should be
    public Vector3 remoteAvatarTarget = Vector3.zero;  // Where first remote avatar should be
    public Vector3 remote1AvatarTarget = Vector3.zero;  // Where second remote avatar should be

    [Header("Debug - Fixed Offsets (added each frame)")]
    public Vector3 remoteOffset = Vector3.zero;  // Fixed offset for remote avatar
    public Vector3 remote1Offset = Vector3.zero;  // Fixed offset for remote1 avatar

    // Remote avatar references
    private GameObject remote1;
    private GameObject remote;

    // Cached house data
    private List<PythonEachHouse> pythonData;

    // Flag to indicate offsets have been calculated
    private bool offsetsCalculated = false;

    void Start()
    {
        // Get id from Arrange_WalkIn.chooseHouseNum
        if (arrangeMR != null)
        {
            id = arrangeMR.GetComponent<Arrange_Walkin>().chooseHouseNum;
        }

        // Initially no offsets - everything starts at (0,0,0)
        // Offsets will be calculated when Y key is pressed (after Python optimization)
        offsetsCalculated = false;
    }

    // Called by HouseJoinOrderAssigner once join order is known, since Start() above
    // runs (and caches id from the still-default chooseHouseNum) before the Fusion
    // connection completes.
    public void SetId(int newId)
    {
        id = newId;
    }

    /// <summary>
    /// Get the remote avatar indices based on local id
    /// RemoteAvatar1 = first to connect (gets "1" suffix in updateavatar)
    /// RemoteAvatar = second to connect
    /// </summary>
    public void GetRemoteIndices(out int remoteIndex, out int remote1Index)
    {
        // Based on connection order naming:
        // id==0 (server): remote1=id1 (first to connect), remote=id2 (second to connect)
        // id==1 (client1): remote1=id0 (server first), remote=id2 (second to connect)
        // id==2 (client2): remote1=id0 (server first), remote=id1 (second to connect)
        switch (id)
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
    }

    /// <summary>
    /// Transform a point from Python's optimized space to world space.
    /// My house prefab is at origin, so my freespace center is at myLocalCentroid.
    ///
    /// Steps:
    /// 1. Translate: subtract my house's final centroid (gets relative position)
    /// 2. Rotate: by -myRotation around Y axis (aligns with my perspective)
    /// 3. Add myLocalCentroid: converts to world position
    /// </summary>
    public Vector3 TransformToLocalSpace(Vector3 optimizedPoint)
    {
        // Step 1: Translate to get position relative to my freespace center
        Vector3 translated = optimizedPoint - myPolygonFinalCentroid;

        // Step 2: Rotate by +myRotation around Y axis (Python CCW = Unity negative, so we use positive to undo)
        Quaternion inverseRotation = Quaternion.Euler(0, myRotation, 0);
        Vector3 relativePoint = inverseRotation * translated;

        // Step 3: Add myLocalCentroid to convert to world position
        Vector3 worldPoint = myLocalCentroid + relativePoint;

        return worldPoint;
    }

    /// <summary>
    /// Transform a point from a sender's local Unity space to optimized space.
    /// This is the inverse of TransformToLocalSpace.
    ///
    /// The sender's position in their Unity is relative to their house at origin.
    /// We need to convert it to optimized space coordinates.
    /// </summary>
    public Vector3 TransformFromSenderLocalToOptimized(Vector3 senderLocalPoint, int senderIndex)
    {
        if (pythonData == null || senderIndex >= pythonData.Count) return senderLocalPoint;

        // Get sender's reference data
        Vector3 senderLocalCentroid = new Vector3(
            pythonData[senderIndex].polygon.centroid.x,
            0,
            pythonData[senderIndex].polygon.centroid.y
        );
        Vector3 senderFinalCentroid = new Vector3(
            pythonData[senderIndex].polygon.final_centroid.x,
            0,
            pythonData[senderIndex].polygon.final_centroid.y
        );
        float senderRotation = pythonData[senderIndex].polygon.rotation;

        // Inverse of TransformToLocalSpace:
        // Step 1: Subtract sender's local centroid (inverse of step 3)
        Vector3 relative = senderLocalPoint - senderLocalCentroid;

        // Step 2: Rotate by -senderRotation (inverse of +senderRotation in step 2)
        Quaternion inverseRotation = Quaternion.Euler(0, -senderRotation, 0);
        Vector3 rotated = inverseRotation * relative;

        // Step 3: Add sender's final centroid (inverse of step 1)
        Vector3 optimized = rotated + senderFinalCentroid;

        return optimized;
    }

    /// <summary>
    /// Transform a point from sender's local space to my local space.
    /// Chain: sender's local → optimized → my local
    /// This is used for real-time avatar position transformation.
    /// </summary>
    public Vector3 TransformFromSenderToMyLocal(Vector3 senderLocalPoint, int senderIndex)
    {
        // First transform to optimized space
        Vector3 optimized = TransformFromSenderLocalToOptimized(senderLocalPoint, senderIndex);

        // Then transform to my local space
        Vector3 myLocal = TransformToLocalSpace(optimized);

        return myLocal;
    }


    /// <summary>
    /// Calculate my reference point from Python data.
    /// This establishes the transform from optimized space to my local space.
    /// My house prefab is at origin, so my freespace center is at myLocalCentroid in world space.
    /// </summary>
    public void CalculateMyReference(List<PythonEachHouse> pythonAllHouse)
    {
        pythonData = pythonAllHouse;

        // My house's final centroid in optimized space
        myPolygonFinalCentroid = new Vector3(
            pythonAllHouse[id].polygon.final_centroid.x,
            0,
            pythonAllHouse[id].polygon.final_centroid.y
        );

        // My house's rotation in optimized space (I counter-rotate by this)
        myRotation = pythonAllHouse[id].polygon.rotation;

        // My freespace center in world space (since my prefab is at origin)
        myLocalCentroid = new Vector3(
            pythonAllHouse[id].polygon.centroid.x,
            0,
            pythonAllHouse[id].polygon.centroid.y
        );
    }

    /// <summary>
    /// Calculate avatar target position in local space for a given house index.
    /// Uses boundary.final_centroid (the user's position in optimized space).
    /// </summary>
    public Vector3 CalculateAvatarTargetPosition(int houseIndex, List<PythonEachHouse> pythonAllHouse)
    {
        // Avatar position in optimized space
        Vector3 avatarOptimizedPos = new Vector3(
            pythonAllHouse[houseIndex].boundary.final_centroid.x,
            0,
            pythonAllHouse[houseIndex].boundary.final_centroid.y
        );

        // Transform to my local space
        Vector3 localPosition = TransformToLocalSpace(avatarOptimizedPos);

        return localPosition;
    }

    /// <summary>
    /// Calculate house target position and rotation in local space.
    /// Uses polygon.final_centroid (the freespace center in optimized space).
    /// Returns (position, rotation) tuple.
    /// </summary>
    public (Vector3 position, float rotation) CalculateHouseTransform(int houseIndex, List<PythonEachHouse> pythonAllHouse)
    {
        if (houseIndex == id)
        {
            // My house stays at origin with no rotation
            return (Vector3.zero, 0f);
        }

        // House center in optimized space
        Vector3 houseOptimizedPos = new Vector3(
            pythonAllHouse[houseIndex].polygon.final_centroid.x,
            0,
            pythonAllHouse[houseIndex].polygon.final_centroid.y
        );

        // Transform position to my local space
        Vector3 localPosition = TransformToLocalSpace(houseOptimizedPos);

        // Calculate relative rotation
        float houseRotation = pythonAllHouse[houseIndex].polygon.rotation;
        float localRotation = houseRotation - myRotation;

        return (localPosition, localRotation);
    }

    /// <summary>
    /// Main entry point: Calculate all offsets from Python optimization data.
    /// Call this after receiving optimization result (Start or Y key).
    /// </summary>
    public void ApplyOffsetsFromPythonData(List<PythonEachHouse> pythonAllHouse)
    {
        // Step 1: Establish my reference point
        CalculateMyReference(pythonAllHouse);

        // Step 2: Get remote indices based on my id
        GetRemoteIndices(out int remoteIndex, out int remote1Index);

        // Step 3: Calculate target positions for all avatars
        localAvatarTarget = CalculateAvatarTargetPosition(id, pythonAllHouse);
        remoteAvatarTarget = CalculateAvatarTargetPosition(remoteIndex, pythonAllHouse);
        remote1AvatarTarget = CalculateAvatarTargetPosition(remote1Index, pythonAllHouse);

        // Step 4: Find remote avatar GameObjects and calculate FIXED offsets
        remote = GameObject.Find("RemoteAvatar");
        remote1 = GameObject.Find("RemoteAvatar1");

        // Calculate fixed offsets: target position - current network position
        // These offsets will be ADDED each frame in LateUpdate
        if (remote != null)
        {
            remoteOffset = remoteAvatarTarget - remote.transform.position;
        }

        if (remote1 != null)
        {
            remote1Offset = remote1AvatarTarget - remote1.transform.position;
        }

        offsetsCalculated = true;
    }

    /// <summary>
    /// Apply fixed offsets to remote avatars using += operator.
    /// Call this in LateUpdate to add offset after NetworkTransform sets positions.
    ///
    /// This approach works because:
    /// 1. NetworkTransform sets position to sender's absolute position
    /// 2. We ADD our fixed offset to move it to the correct relative position
    /// 3. Next frame, NetworkTransform resets it, we add offset again
    /// </summary>
    public void ApplyStoredOffsets()
    {
        if (!offsetsCalculated) return;

        // Re-find if null (avatars might spawn later)
        if (remote == null)
            remote = GameObject.Find("RemoteAvatar");
        if (remote1 == null)
            remote1 = GameObject.Find("RemoteAvatar1");

        // Apply offsets using += (the proven working approach)
        if (remote != null && remoteOffset != Vector3.zero)
        {
            remote.transform.position += remoteOffset;
        }

        if (remote1 != null && remote1Offset != Vector3.zero)
        {
            remote1.transform.position += remote1Offset;
        }
    }

    /// <summary>
    /// Get the local avatar's target position.
    /// Use this to position the LocalAvatar.
    /// </summary>
    public Vector3 GetLocalAvatarTarget()
    {
        return localAvatarTarget;
    }

    /// <summary>
    /// Clear all offsets (call when switching modes or resetting)
    /// </summary>
    public void ClearOffsets()
    {
        myPolygonFinalCentroid = Vector3.zero;
        myRotation = 0f;
        myLocalCentroid = Vector3.zero;
        localAvatarTarget = Vector3.zero;
        remoteAvatarTarget = Vector3.zero;
        remote1AvatarTarget = Vector3.zero;
        remoteOffset = Vector3.zero;
        remote1Offset = Vector3.zero;
        offsetsCalculated = false;
    }

    // NOTE: LateUpdate offset application is now handled by SceneSelection.cs
    // to ensure reliable execution. OffsetCalculator is used only for calculations.
    // void LateUpdate()
    // {
    //     ApplyStoredOffsets();
    // }

    /// <summary>
    /// Debug visualization in Scene view
    /// </summary>
    void OnDrawGizmos()
    {
        if (!offsetsCalculated) return;

        // // Draw my avatar target (green)
        // Gizmos.color = Color.green;
        // Gizmos.DrawWireSphere(localAvatarTarget + Vector3.up * 0.1f, 0.3f);

        // // Draw remote avatar target (blue)
        // Gizmos.color = Color.blue;
        // Gizmos.DrawWireSphere(remoteAvatarTarget + Vector3.up * 0.1f, 0.3f);

        // // Draw remote1 avatar target (red)
        // Gizmos.color = Color.red;
        // Gizmos.DrawWireSphere(remote1AvatarTarget + Vector3.up * 0.1f, 0.3f);
    }
}

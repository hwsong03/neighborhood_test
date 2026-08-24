using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ModeBCalculator : MonoBehaviour
{

    // Singleton instance
    public static ModeBCalculator Instance { get; private set; }

    // ModeB state
    public bool isModeB = false;

    [Header("OneEuroFilter Settings")]
    [Tooltip("Filter frequency - should match VR refresh rate (Quest=90Hz)")]
    public float filterFrequency = 90.0f;

    [Tooltip("Minimum cutoff frequency - lower = smoother but more lag")]
    public float filterMinCutoff = 1.0f;

    [Tooltip("Speed coefficient - higher = faster response to quick movements")]
    public float filterBeta = 0.0f;

    [Tooltip("Derivative cutoff frequency")]
    public float filterDCutoff = 1.0f;

    [Tooltip("Enable filtering for smoother movement")]
    public bool enableFiltering = true;

    public bool debug = false;

    // OneEuroFilter instances
    private OneEuroFilter<Vector3> localPosFilter;       // Option B: filter local position
    private OneEuroFilter<Vector3> remote1TargetFilter;  // Option A: filter remote1 target
    private OneEuroFilter<Vector3> remoteTargetFilter;   // Option A: filter remote target

    // Saved state when modeB starts
    private Vector3 localStartPos;
    private Vector3 remote1StartPos;
    private Vector3 remoteStartPos;

    // Relative vectors from local to remotes (position offset only)
    private Vector3 remote1RelativeVec;  // remote1StartPos - localStartPos
    private Vector3 remoteRelativeVec;   // remoteStartPos - localStartPos

    // Previous frame's local position for frame-by-frame diff
    private Vector3 localPreviousPos;

    // Accumulated position difference (rotation removed)
    private Vector3 accumPosDiff;

    // Cached GameObject references
    private GameObject localAvatar;
    private GameObject remote1;
    private GameObject remote;

    // Cached Joint Chest Transform references for Meta Avatar visual position
    private Transform localJointChest;
    private Transform remote1JointChest;
    private Transform remoteJointChest;

    // House references
    private GameObject arrangeMRHouse;
    private Transform remote1House;
    private Transform remoteHouse;
    private int myType;

    // House relative positions (captured at modeB start, relative to avatar root)
    private Vector3 remote1HouseRelativePos;
    private Vector3 remoteHouseRelativePos;

    // Initial house and avatar positions for dynamic house calculation
    private Vector3 remote1HouseInitialPos;
    private Vector3 remote1JointChestInitialPos;
    private Vector3 remoteHouseInitialPos;
    private Vector3 remoteJointChestInitialPos;

    // House rotations (captured at modeB start, for rotating the inverse offset)
    private float remote1HouseRotationY;
    private float remoteHouseRotationY;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
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



    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }

    // Called when modeB starts (B key pressed)
    public void OnModeBStart()
    {
        isModeB = true;
        isFirstModeBFrame = true;
        Debug.Log("[ModeBCalculator] ModeB started");

        // Reset accumulated position difference
        accumPosDiff = Vector3.zero;

        // Initialize OneEuroFilters with current settings
        localPosFilter = new OneEuroFilter<Vector3>(filterFrequency, filterMinCutoff, filterBeta, filterDCutoff);
        remote1TargetFilter = new OneEuroFilter<Vector3>(filterFrequency, filterMinCutoff, filterBeta, filterDCutoff);
        remoteTargetFilter = new OneEuroFilter<Vector3>(filterFrequency, filterMinCutoff, filterBeta, filterDCutoff);
        Debug.Log($"[ModeBCalculator] Filters initialized: freq={filterFrequency}, minCutoff={filterMinCutoff}, beta={filterBeta}, dCutoff={filterDCutoff}");

        // Cache GameObject references
        localAvatar = GameObject.Find("LocalAvatar");
        remote1 = GameObject.Find("RemoteAvatar1");
        remote = GameObject.Find("RemoteAvatar");

        // Save LocalAvatar start state (using Joint Chest for visual position)
        if (localAvatar != null)
        {
            localJointChest = AvatarJointHelper.FindJointChest(localAvatar.transform);
            Vector3 visualPos = (localJointChest != null) ? localJointChest.position : localAvatar.transform.position;
            localStartPos = visualPos;
            localPreviousPos = localStartPos;
        }

        // Save RemoteAvatar1 start state (using Joint Chest for visual position)
        if (remote1 != null)
        {
            remote1JointChest = AvatarJointHelper.FindJointChest(remote1.transform);
            Vector3 networkPos = (remote1JointChest != null) ? remote1JointChest.position : remote1.transform.position;

            remote1StartPos = networkPos;
            // Calculate relative vector from local to remote1 (position only)
            remote1RelativeVec = remote1StartPos - localStartPos;
        }

        // Save RemoteAvatar start state (using Joint Chest for visual position)
        if (remote != null)
        {
            remoteJointChest = AvatarJointHelper.FindJointChest(remote.transform);
            Vector3 visualPos = (remoteJointChest != null) ? remoteJointChest.position : remote.transform.position;
            remoteStartPos = visualPos;
            // Calculate relative vector from local to remote (position only)
            remoteRelativeVec = remoteStartPos - localStartPos;
        }

        // Get house references using OffsetCalculator
        if (OffsetCalculator.Instance != null)
        {
            var offsetCalc = OffsetCalculator.Instance;
            myType = offsetCalc.id;

            // Get remote indices from OffsetCalculator
            offsetCalc.GetRemoteIndices(out int remoteIndex, out int remote1Index);

            // Get houses from Arrange_WalkIn
            if (offsetCalc.arrangeMR != null)
            {
                var arrangeWalkin = offsetCalc.arrangeMR.GetComponent<Arrange_Walkin>();
                if (arrangeWalkin != null && arrangeWalkin.houses != null)
                {
                    var houses = arrangeWalkin.houses;

                    if (remote1Index < houses.Count)
                    {
                        remote1House = houses[remote1Index].transform;
                        // Capture house position relative to avatar ROOT (not jointChest)
                        if (remote1 != null)
                        {
                            remote1HouseRelativePos = remote1House.position - remote1.transform.position;
                        }
                        // Capture house initial position and rotation
                        remote1HouseInitialPos = remote1House.position;
                        remote1HouseRotationY = remote1House.eulerAngles.y;
                        Debug.Log($"[ModeBCalculator] OnModeBStart remote1: houseInitialPos={remote1HouseInitialPos}, houseRotY={remote1HouseRotationY}");
                    }
                    if (remoteIndex < houses.Count)
                    {
                        remoteHouse = houses[remoteIndex].transform;
                        // Capture house position relative to avatar ROOT
                        if (remote != null)
                        {
                            remoteHouseRelativePos = remoteHouse.position - remote.transform.position;
                        }
                        // Capture house initial position and rotation
                        remoteHouseInitialPos = remoteHouse.position;
                        remoteHouseRotationY = remoteHouse.eulerAngles.y;
                        Debug.Log($"[ModeBCalculator] OnModeBStart remote: houseInitialPos={remoteHouseInitialPos}, houseRotY={remoteHouseRotationY}");
                    }

                }
            }
        }
        else
        {
            Debug.LogWarning("[ModeBCalculator] OffsetCalculator.Instance is null!");
        }
    }

    // Called when modeB ends (A key pressed)
    public void OnModeBEnd()
    {
        isModeB = false;
        Debug.Log("[ModeBCalculator] ModeB ended");
    }

    // Called from SceneSelection.LateUpdate when modeB is active
    // This replaces the normal offset calculation
    private int frameCounter = 0;
    private bool isFirstModeBFrame = true;
    public void ApplyModeBPositioning()
    {
        if (localJointChest == null)
        {
            Debug.LogWarning("[ModeBCalculator] ApplyModeBPositioning: localJointChest is null!");
            return;
        }

        // Get LocalAvatar current transformation from child(4)
        Vector3 localCurrentPos = localJointChest.position;

        // Option B: Apply OneEuroFilter to local position
        Vector3 filteredLocalPos = localCurrentPos;
        if (enableFiltering && localPosFilter != null)
        {
            filteredLocalPos = localPosFilter.Filter(localCurrentPos);
        }

        // Calculate frame-by-frame position difference (rotation removed)
        Vector3 localPosDiff = filteredLocalPos - localPreviousPos;

        // Accumulate position difference only (no rotation)
        accumPosDiff += localPosDiff;

        // Update previous for next frame
        localPreviousPos = filteredLocalPos;


        // Apply to RemoteAvatar1: position-only mode (rotation removed)
        // Remote follows local avatar position changes only
        // We READ from child(4) but WRITE to root transform (like SceneSelection does)
        if (remote1 != null && remote1JointChest != null)
        {
            // Get network positions BEFORE we modify
            // Joint Chest position for avatar targeting
            Vector3 networkJointChestPos = remote1JointChest.position;
            // ROOT position for house calculation (network sends root position)
            Vector3 networkRootPos = remote1.transform.position;

            // Capture initial position on first frame (RAW network position, before any offset)
            // This fixes the mismatch where OnModeBStart captured post-modeA-offset position
            if (isFirstModeBFrame)
            {
                remote1JointChestInitialPos = networkJointChestPos;
                Debug.Log($"[ModeBCalculator] First frame capture remote1: jointChestPos={remote1JointChestInitialPos}, housePos={remote1HouseInitialPos}");
            }

            // Position only - no rotation (removed sun-earth model)
            // Target position = filtered local position + original relative vector
            Vector3 targetPos = filteredLocalPos + remote1RelativeVec;

            // Option A: Apply OneEuroFilter to target position for additional smoothing
            Vector3 filteredTargetPos = targetPos;
            if (enableFiltering && remote1TargetFilter != null)
            {
                filteredTargetPos = remote1TargetFilter.Filter(targetPos);
            }

            Vector3 offset = filteredTargetPos - networkJointChestPos;

            // Apply offset to root transform (jointChest follows)
            remote1.transform.position += new Vector3(offset.x, 0, offset.z);

            // Apply rotation offset (same logic as SceneSelection.LateUpdate)
            // Save jointChest's correct world position BEFORE rotating root
            Vector3 jointChestCorrectPos = remote1JointChest.position;

            // Apply rotation offset to root (changes facing direction)
            Quaternion avatarRotation = Quaternion.Euler(0, remote1HouseRotationY, 0);
            remote1.transform.rotation = avatarRotation * remote1.transform.rotation;

            // Compensate: jointChest orbited around root, move root back so jointChest returns to correct position
            Vector3 jointChestCurrentPos = remote1JointChest.position;
            Vector3 compensation = jointChestCorrectPos - jointChestCurrentPos;
            remote1.transform.position += compensation;

            // Set house position using inverse of network position
            // In remote user's local space: house at origin (0,0,0), avatar jointChest at networkJointChestPos
            // So house relative to avatar = -networkJointChestPos
            // But the house has been rotated by modeA offset, so we need to rotate this vector too
            if (remote1House != null)
            {
                // Inverse of avatar's absolute position in their local space (un-rotated)
                Vector3 unrotatedRelative = new Vector3(-networkJointChestPos.x, 0, -networkJointChestPos.z);

                // Rotate by house's rotation from modeA offset
                Quaternion houseRotation = Quaternion.Euler(0, remote1HouseRotationY, 0);
                Vector3 houseRelativeToAvatar = houseRotation * unrotatedRelative;

                // Use filtered target position for house as well
                Vector3 houseTargetPos = filteredTargetPos + houseRelativeToAvatar;
                remote1House.position = new Vector3(houseTargetPos.x, remote1House.position.y, houseTargetPos.z);

                // Debug: log house calculation details
                if (debug && frameCounter % 60 == 0)
                {
                    Debug.Log($"[ModeBCalculator] Remote1 House: networkJointChestPos={networkJointChestPos}, filteredTargetPos={filteredTargetPos}, houseRotY={remote1HouseRotationY}");
                    Debug.Log($"[ModeBCalculator] Remote1 House: unrotatedRelative={unrotatedRelative}, houseRelativeToAvatar={houseRelativeToAvatar}, houseTargetPos={houseTargetPos}");
                }
            }

        }

        // Apply to RemoteAvatar: position-only mode (rotation removed)
        if (remote != null && remoteJointChest != null)
        {
            // Get network positions BEFORE we modify
            Vector3 networkJointChestPos = remoteJointChest.position;
            Vector3 networkRootPos = remote.transform.position;

            // Capture initial position on first frame (RAW network position, before any offset)
            if (isFirstModeBFrame)
            {
                remoteJointChestInitialPos = networkJointChestPos;
                Debug.Log($"[ModeBCalculator] First frame capture remote: jointChestPos={remoteJointChestInitialPos}, housePos={remoteHouseInitialPos}");
            }

            // Position only - no rotation (removed sun-earth model)
            // Target position = filtered local position + original relative vector
            Vector3 targetPos = filteredLocalPos + remoteRelativeVec;

            // Option A: Apply OneEuroFilter to target position for additional smoothing
            Vector3 filteredTargetPos = targetPos;
            if (enableFiltering && remoteTargetFilter != null)
            {
                filteredTargetPos = remoteTargetFilter.Filter(targetPos);
            }

            Vector3 offset = filteredTargetPos - networkJointChestPos;

            // Apply offset to root transform (jointChest follows)
            remote.transform.position += new Vector3(offset.x, 0, offset.z);

            // Apply rotation offset (same logic as SceneSelection.LateUpdate)
            // Save jointChest's correct world position BEFORE rotating root
            Vector3 jointChestCorrectPos = remoteJointChest.position;

            // Apply rotation offset to root (changes facing direction)
            Quaternion avatarRotation = Quaternion.Euler(0, remoteHouseRotationY, 0);
            remote.transform.rotation = avatarRotation * remote.transform.rotation;

            // Compensate: jointChest orbited around root, move root back so jointChest returns to correct position
            Vector3 jointChestCurrentPos = remoteJointChest.position;
            Vector3 compensation = jointChestCorrectPos - jointChestCurrentPos;
            remote.transform.position += compensation;

            // Set house position using inverse of network position
            // In remote user's local space: house at origin (0,0,0), avatar jointChest at networkJointChestPos
            // So house relative to avatar = -networkJointChestPos
            // But the house has been rotated by modeA offset, so we need to rotate this vector too
            if (remoteHouse != null)
            {
                // Inverse of avatar's absolute position in their local space (un-rotated)
                Vector3 unrotatedRelative = new Vector3(-networkJointChestPos.x, 0, -networkJointChestPos.z);

                // Rotate by house's rotation from modeA offset
                Quaternion houseRotation = Quaternion.Euler(0, remoteHouseRotationY, 0);
                Vector3 houseRelativeToAvatar = houseRotation * unrotatedRelative;

                // Use filtered target position for house as well
                Vector3 houseTargetPos = filteredTargetPos + houseRelativeToAvatar;
                remoteHouse.position = new Vector3(houseTargetPos.x, remoteHouse.position.y, houseTargetPos.z);

                // Debug: log house calculation details
                if (debug && frameCounter % 60 == 0)
                {
                    Debug.Log($"[ModeBCalculator] Remote House: networkJointChestPos={networkJointChestPos}, filteredTargetPos={filteredTargetPos}, houseRotY={remoteHouseRotationY}");
                    Debug.Log($"[ModeBCalculator] Remote House: unrotatedRelative={unrotatedRelative}, houseRelativeToAvatar={houseRelativeToAvatar}, houseTargetPos={houseTargetPos}");
                }
            }
        }

        // Reset first frame flag after processing both remotes
        if (isFirstModeBFrame)
        {
            isFirstModeBFrame = false;
        }

        frameCounter++;
    }
}

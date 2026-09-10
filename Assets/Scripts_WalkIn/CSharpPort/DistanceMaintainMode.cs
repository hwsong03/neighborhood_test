using UnityEngine;

// "거리유지모드 (패닝모드)" -- toggled by B, synced across every computer in the session:
// pressing B on ANY house's computer turns it on/off everywhere at once (via
// TransferManager's Fusion RPC channel), not just locally.
//
// While active, tracks how far MY OWN avatar has moved (x/z only) since the
// mode turned on, and moves OTHER houses' transforms by that same delta every
// frame. Because avatars are positioned relative to their house transform
// (via SceneSelection.LateUpdate), and ROI/boundary circles track those avatars
// (via CircleFollowAvatar), and the shader anchor follows the avatar's real
// position (via Regions.SetUserPosition in CircleFollowAvatar), all three --
// house, avatar, circles, shader zone -- move together as one glued set.
// My own house is never touched.
//
// Auto-installs itself (RuntimeInitializeOnLoadMethod, same convention as
// HeadsetHUD.cs) so no scene wiring is needed.
public class DistanceMaintainMode : MonoBehaviour
{
    public static DistanceMaintainMode Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var go = new GameObject("DistanceMaintainMode");
        go.AddComponent<DistanceMaintainMode>();

        // Safety: zero out any leftover _LocalOffset from a previous Editor session.
        Shader.SetGlobalVector(LocalOffsetId, Vector4.zero);
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public bool IsActive { get; private set; }

    // Same Simulator keyboard->controller relay issue CameraController has --
    // a single physical B tap can satisfy OVRInput.GetDown(...) and
    // Input.GetKeyDown(KeyCode.B) on two different frames.
    const float ToggleCooldownSeconds = 0.3f;
    float lastToggleTime = -999f;

    GameObject localAvatar;
    Transform jointHead;
    Vector3? myHeadPosAtModeStart;

    Arrange_Walkin arrangeWalkin;
    int myType = -1;
    Vector3[] houseBaselinePositions;

    static readonly int LocalOffsetId = Shader.PropertyToID("_LocalOffset");

    void Update()
    {
        bool togglePressed = OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch) || Input.GetKeyDown(KeyCode.B);
        if (togglePressed && Time.unscaledTime - lastToggleTime >= ToggleCooldownSeconds)
        {
            lastToggleTime = Time.unscaledTime;
            RequestToggle();
        }

        if (!IsActive) return;

        if (jointHead == null)
        {
            if (localAvatar == null) localAvatar = GameObject.Find("LocalAvatar");
            if (localAvatar != null) jointHead = AvatarJointHelper.FindJointHead(localAvatar.transform);
        }

        if (jointHead != null)
        {
            if (!myHeadPosAtModeStart.HasValue) myHeadPosAtModeStart = jointHead.position;

            if (TryGetDeltaXZ(out Vector2 delta))
                ApplyDeltaToOtherHouses(delta);
        }
    }

    void EnsureArrangeWalkin()
    {
        if (arrangeWalkin != null) return;
        arrangeWalkin = FindFirstObjectByType<Arrange_Walkin>();
    }

    int ResolveMyType()
    {
        EnsureArrangeWalkin();
        if (arrangeWalkin == null) return 0;
        var sceneSel = arrangeWalkin.sceneSelection?.GetComponent<SceneSelection>();
        return sceneSel != null ? sceneSel.type : 0;
    }

    void CaptureHouseBaselines()
    {
        EnsureArrangeWalkin();
        if (arrangeWalkin == null) return;
        myType = ResolveMyType();
        houseBaselinePositions = new Vector3[arrangeWalkin.houses.Count];
        for (int i = 0; i < arrangeWalkin.houses.Count; i++)
            houseBaselinePositions[i] = arrangeWalkin.houses[i].transform.position;
    }

    void ApplyDeltaToOtherHouses(Vector2 delta)
    {
        if (arrangeWalkin == null || houseBaselinePositions == null) return;
        for (int i = 0; i < arrangeWalkin.houses.Count && i < houseBaselinePositions.Length; i++)
        {
            if (i == myType || arrangeWalkin.houses[i] == null) continue;
            arrangeWalkin.houses[i].transform.position = houseBaselinePositions[i] + new Vector3(delta.x, 0f, delta.y);
        }
    }

    void RestoreHousesToBaseline()
    {
        if (arrangeWalkin == null || houseBaselinePositions == null) return;
        for (int i = 0; i < arrangeWalkin.houses.Count && i < houseBaselinePositions.Length; i++)
        {
            if (i == myType || arrangeWalkin.houses[i] == null) continue;
            arrangeWalkin.houses[i].transform.position = houseBaselinePositions[i];
        }
        houseBaselinePositions = null;
    }

    void RequestToggle()
    {
        var transferManager = FindFirstObjectByType<TransferManager>();
        if (transferManager == null)
        {
            Debug.LogWarning("[DistanceMaintainMode] No TransferManager found -- toggling locally only.");
            ApplyNetworkedState(!IsActive);
            return;
        }
        transferManager.RPC_SetDistanceMaintainMode(!IsActive);
    }

    public void ApplyNetworkedState(bool active)
    {
        if (IsActive == active) return;
        IsActive = active;
        if (active)
        {
            myHeadPosAtModeStart = null;
            CaptureHouseBaselines();
        }
        else
        {
            RestoreHousesToBaseline();
        }
        Debug.Log($"[DistanceMaintainMode] {(IsActive ? "enabled" : "disabled")}.");
    }

    public void Disable()
    {
        Shader.SetGlobalVector(LocalOffsetId, Vector4.zero);
        ApplyNetworkedState(false);
    }

    public bool TryGetDeltaXZ(out Vector2 deltaXZ)
    {
        deltaXZ = Vector2.zero;
        if (!IsActive || jointHead == null || !myHeadPosAtModeStart.HasValue) return false;

        Vector3 cur = jointHead.position;
        Vector3 start = myHeadPosAtModeStart.Value;
        deltaXZ = new Vector2(cur.x - start.x, cur.z - start.z);
        return true;
    }
}

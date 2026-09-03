using UnityEngine;

// "거리유지모드" -- toggled by B, synced across every computer in the session:
// pressing B on ANY house's computer turns it on/off everywhere at once (via
// TransferManager's Fusion RPC channel), not just locally.
//
// While active, tracks how far MY OWN avatar has moved (x/z only) since the
// mode turned on, and feeds that single delta to two places so they always
// move together as one set:
//  - CircleFollowAvatar, which offsets OTHER houses' ROI/boundary rings by it
//    on top of that house's own real avatar position.
//  - the _LocalOffset shader global (see Standard_WithZones.shader), which
//    carries the whole rendered Remote-zone cutout by the same amount.
// In both cases the thing being shown/revealed is still decided entirely by
// that house's own real avatar position -- this delta only ever changes WHERE
// it renders on THIS client's screen, never what's revealed or anything
// networked, so another house's own view of their own space is untouched.
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

        // _LocalOffset is a raw Shader.SetGlobalVector value -- it lives at
        // the graphics-device level, not the scene, so it is NOT reset just
        // because Play was stopped and started again (confirmed live: a
        // stale value from a manual test earlier in the same Editor session
        // was still sitting there on a fresh Play run where this mode had
        // never even been turned on yet, visibly detaching the debug rings
        // from the shader-revealed geometry they must always match). Force
        // it to zero exactly once here, the moment this component installs,
        // so a real session always starts from a known-clean value no matter
        // what any previous run left behind.
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
            {
                Shader.SetGlobalVector(LocalOffsetId, new Vector4(delta.x, 0f, delta.y, 0f));
            }
        }
    }

    // Broadcasts the new state to every client (including this one) via
    // TransferManager's Fusion RPC channel -- RpcSources.All lets ANY client,
    // not just the server, trigger it, same pattern as RPC_BroadcastZOptResult.
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

    // The single place IsActive actually changes -- called by
    // TransferManager.RPC_SetDistanceMaintainMode on every client (including
    // whichever one pressed B), and locally by Disable() below.
    public void ApplyNetworkedState(bool active)
    {
        if (IsActive == active) return;
        IsActive = active;
        if (active) myHeadPosAtModeStart = null; // fresh reference point every time this turns on
        Debug.Log($"[DistanceMaintainMode] {(IsActive ? "enabled" : "disabled")}.");
    }

    // Called by LocalOptimizationRunner right before applying a Z/M optimization
    // result -- a result should be seen with houses/circles at their true
    // optimized target, not shifted by whatever distance-maintain state was
    // active. Runs locally on every client as a side effect of each of them
    // independently applying the same optimization event, so no RPC is needed.
    public void Disable()
    {
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

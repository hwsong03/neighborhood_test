using UnityEngine;

// "거리유지모드" -- toggled by B, synced across every computer in the session:
// pressing B on ANY house's computer turns it on/off everywhere at once (via
// TransferManager's Fusion RPC channel), not just locally. Completely
// independent of CameraController's spectator camera (Display 2) -- has
// nothing to do with any camera, it's purely "are the ROI/boundary circles
// currently following their houses' avatars."
//
// Drives ONLY the debug boundary/ROI circles (CircleFollowAvatar), never the
// actual shader-driven zone-clipping geometry (Regions.userPosVec4) -- another
// house's real rendered space must never change because of this feature.
//
// CircleFollowAvatar needs no "how far have I moved" delta: each house's
// circle just continuously tracks that house's OWN avatar's real (Fusion
// network-synced) position while this mode is active. Avatar position is the
// only thing that matters -- the circle has no position logic of its own, and
// my own movement never affects another house's circle directly; it only
// looks that way when it's really that house's own avatar moving.
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

    void Update()
    {
        bool togglePressed = OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch) || Input.GetKeyDown(KeyCode.B);
        if (togglePressed && Time.unscaledTime - lastToggleTime >= ToggleCooldownSeconds)
        {
            lastToggleTime = Time.unscaledTime;
            RequestToggle();
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
        Debug.Log($"[DistanceMaintainMode] {(IsActive ? "enabled" : "disabled")}.");
    }

    // Called by LocalOptimizationRunner right before applying a Z/M optimization
    // result -- same reasoning as CameraController.DisablePanningView(): a
    // result should be seen with houses/circles at their true optimized target,
    // not shifted by whatever distance-maintain state was active. This runs
    // locally on every client as a side effect of each of them independently
    // applying the same optimization event, so no RPC is needed here.
    public void Disable()
    {
        ApplyNetworkedState(false);
    }
}

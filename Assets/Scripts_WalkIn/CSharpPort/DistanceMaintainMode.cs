using UnityEngine;

// "거리유지모드" -- toggled by B, completely independent of CameraController's
// spectator camera (Display 2). Earlier this was implemented by reusing that
// camera's enabled flag as a stand-in boolean, which was confusing to talk
// about since this mode has nothing to do with any camera -- it's purely
// "is the other-houses-follow-my-movement behavior active right now."
//
// Owns its own B-key detection (same Simulator keyboard->controller relay
// debounce CameraController needs, since this also checks both input sources)
// and its own head-position tracking, so CircleFollowAvatar/
// DistanceMaintainZoneOffset no longer need to go through CameraController at all.
//
// Auto-installs itself (RuntimeInitializeOnLoadMethod, same convention as
// DistanceMaintainZoneOffset.cs/HeadsetHUD.cs) so no scene wiring is needed.
public class DistanceMaintainMode : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var go = new GameObject("DistanceMaintainMode");
        go.AddComponent<DistanceMaintainMode>();
    }

    public bool IsActive { get; private set; }

    const float ToggleCooldownSeconds = 0.3f;
    float lastToggleTime = -999f;

    GameObject localAvatar;
    Transform jointHead;
    Vector3? myHeadPosAtModeStart;

    void Update()
    {
        bool togglePressed = OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch) || Input.GetKeyDown(KeyCode.B);
        if (togglePressed && Time.unscaledTime - lastToggleTime >= ToggleCooldownSeconds)
        {
            lastToggleTime = Time.unscaledTime;
            IsActive = !IsActive;
            myHeadPosAtModeStart = null; // fresh reference point every time this turns on
            Debug.Log($"[DistanceMaintainMode] {(IsActive ? "enabled" : "disabled")}.");
        }

        if (!IsActive) return;

        if (jointHead == null)
        {
            if (localAvatar == null) localAvatar = GameObject.Find("LocalAvatar");
            if (localAvatar != null) jointHead = AvatarJointHelper.FindJointHead(localAvatar.transform);
        }

        if (jointHead != null && !myHeadPosAtModeStart.HasValue)
        {
            myHeadPosAtModeStart = jointHead.position;
        }
    }

    // Called by LocalOptimizationRunner right before applying a Z/M optimization
    // result -- same reasoning as CameraController.DisablePanningView(): a
    // result should be seen with houses at their true target, not shifted by
    // whatever distance-maintain offset happened to be active.
    public void Disable()
    {
        if (!IsActive) return;
        IsActive = false;
        myHeadPosAtModeStart = null;
        Debug.Log("[DistanceMaintainMode] disabled (forced off for optimization).");
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

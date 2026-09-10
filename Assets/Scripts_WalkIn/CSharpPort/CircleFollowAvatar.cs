using UnityEngine;

// Keeps a boundary/ROI circle centered on its house's avatar every frame
// (X/Z only, fixed height -- never rotates with the avatar's facing), but
// ONLY while DistanceMaintainMode (거리유지모드/패닝모드, toggled by B, synced across
// every computer) is active. The rest of the time the circle stays wherever
// it was originally drawn (the optimizer's fixed target).
//
// DistanceMaintainMode moves the OTHER houses' transforms by my movement delta,
// so SceneSelection.LateUpdate() naturally positions each remote avatar at
// houseTransform.position + networkOffset + rotationOffset -- the delta is
// already baked into the house transform. Reading jointChest.position here
// therefore already includes the delta; no manual delta addition is needed.
//
// For isMine (my own house's circle, target == my own avatar): the ring tracks
// me directly. _Users[myType] must stay frozen at the optimization-time value --
// never feed it from my live moving position (see comment in original file).
public class CircleFollowAvatar : MonoBehaviour
{
    public Transform target;
    public float height;
    public int houseIndex = -1;

    // True only for the local player's own house's circle.
    public bool isMine;

    DistanceMaintainMode mode;

    void LateUpdate()
    {
        if (target == null) return;

        if (mode == null) mode = FindFirstObjectByType<DistanceMaintainMode>();
        if (mode == null || !mode.IsActive) return;

        Transform jointChest = AvatarJointHelper.FindJointChest(target);
        Vector3 anchorPos = jointChest != null ? jointChest.position : target.position;

        // Shader content anchor -- the avatar's real current position (which
        // already includes the house delta from DistanceMaintainMode). Never
        // for isMine -- _Users[myType] must stay frozen at optimization time.
        if (!isMine && houseIndex >= 0 && Regions.Instance != null)
        {
            Regions.Instance.SetUserPosition(houseIndex, anchorPos);
        }

        transform.position = new Vector3(anchorPos.x, height, anchorPos.z);
    }
}

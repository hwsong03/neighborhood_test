using UnityEngine;

// Keeps a boundary/ROI circle centered on its house's avatar every frame
// (X/Z only, fixed height -- never rotates with the avatar's facing), but
// ONLY while DistanceMaintainMode (거리유지모드, toggled by B) is active. The
// rest of the time the circle stays wherever it was originally drawn (the
// optimizer's fixed target) -- per request, this following behavior is
// specifically a 거리유지모드 thing, not an always-on one, and has nothing to
// do with the separate spectator camera (CameraController) despite both
// happening to be bound to the same B key.
//
// `target` is the avatar ROOT, but the root transform does not reflect the
// avatar's real visual position -- same reason BuildOptimizationInputs/
// ApplyAvatarPositions/CameraController all read the Joint Chest bone instead
// (SceneSelection.LateUpdate's own rotation compensation moves the root away
// from Joint Chest to keep the CHEST correct under rotation, which left this
// circle -- when it tracked root directly -- visibly detached from the avatar
// body itself). Re-resolve the joint every frame (not cached) since it can
// change if the avatar respawns.
public class CircleFollowAvatar : MonoBehaviour
{
    public Transform target;
    public float height;

    // True only for the local player's own house's circle. That one already
    // tracks ME directly (target == my own avatar), so it must NOT also get the
    // "shift by how far I've moved" offset below -- that offset exists so an
    // OTHER house's circle keeps a constant distance from me as I walk, which is
    // meaningless applied to my own circle (already centered on me at all times).
    public bool isMine;

    DistanceMaintainMode mode;

    void LateUpdate()
    {
        if (target == null) return;

        if (mode == null) mode = FindFirstObjectByType<DistanceMaintainMode>();
        if (mode == null || !mode.IsActive) return;

        Transform jointChest = AvatarJointHelper.FindJointChest(target);
        Vector3 pos = jointChest != null ? jointChest.position : target.position;

        // Per request: while 거리유지모드 is on, an OTHER house's circle should
        // move exactly as far as I have (X/Z only, no rotation), so our relative
        // distance stays constant -- on top of wherever its own target (a real
        // avatar, or the static Characters dummy for an empty house) already is.
        if (!isMine && mode.TryGetDeltaXZ(out Vector2 delta))
        {
            pos.x += delta.x;
            pos.z += delta.y;
        }

        transform.position = new Vector3(pos.x, height, pos.z);
    }
}

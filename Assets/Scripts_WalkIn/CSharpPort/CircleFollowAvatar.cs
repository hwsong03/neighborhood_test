using UnityEngine;

// Keeps a boundary/ROI circle centered on its house's avatar every frame
// (X/Z only, fixed height -- never rotates with the avatar's facing), but
// ONLY while DistanceMaintainMode (거리유지모드, toggled by B, synced across
// every computer) is active. The rest of the time the circle stays wherever
// it was originally drawn (the optimizer's fixed target).
//
// Deliberately just "read target's current position" with no extra offset
// math: `target` is the same avatar Transform Fusion already keeps
// network-synced on every client, so a house's own avatar moving is already
// enough to move its circle everywhere, on its own -- no delta needed. An
// earlier version derived a synthetic X/Z offset from how far the LOCAL
// player had moved and applied it to other houses' circles; that was wrong,
// per request -- it simulated another house's avatar moving because of MY
// movement, when only that house's own actual avatar moving should ever move
// its own circle. We only ever care about avatar position, never the
// circle's own position.
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

    // Which house this circle belongs to -- also used to feed the SAME live
    // position into Regions' shader zone-clip marker for this house (see
    // below), so the debug circle/outline and the actual visible-through-the-
    // shader space of that house are always one set, never just the circle.
    public int houseIndex = -1;

    DistanceMaintainMode mode;

    void LateUpdate()
    {
        if (target == null) return;

        if (mode == null) mode = FindFirstObjectByType<DistanceMaintainMode>();
        if (mode == null || !mode.IsActive) return;

        Transform jointChest = AvatarJointHelper.FindJointChest(target);
        Vector3 pos = jointChest != null ? jointChest.position : target.position;

        transform.position = new Vector3(pos.x, height, pos.z);

        // Regions.Update() re-broadcasts userPosVec4 to the shader every frame
        // regardless, so writing it here just keeps this house's real visible
        // area locked to the same position as its circle -- gated by the same
        // mode.IsActive check as the circle itself, so it's frozen whenever the
        // circle is (never active pre-optimization, never active for a house
        // whose own avatar hasn't moved), unlike the old always-on feed that
        // used to drag the visible area around unintentionally.
        if (houseIndex >= 0 && Regions.Instance != null)
        {
            Regions.Instance.SetUserPosition(houseIndex, pos);
        }
    }
}

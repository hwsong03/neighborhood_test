using UnityEngine;

// Keeps a boundary/ROI circle centered on its house's avatar every frame
// (X/Z only, fixed height -- never rotates with the avatar's facing), but
// ONLY while the panning/spectator view (CameraController) is active. The
// rest of the time the circle stays wherever it was originally drawn (the
// optimizer's fixed target) -- per request, this following behavior is
// specifically a panning-view thing, not an always-on one.
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

    CameraController panningView;

    void LateUpdate()
    {
        if (target == null) return;

        if (panningView == null) panningView = FindFirstObjectByType<CameraController>();
        if (panningView == null || !panningView.IsPanningActive) return;

        Transform jointChest = AvatarJointHelper.FindJointChest(target);
        Vector3 pos = jointChest != null ? jointChest.position : target.position;
        transform.position = new Vector3(pos.x, height, pos.z);
    }
}

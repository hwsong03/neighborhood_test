using UnityEngine;

// Keeps a boundary/ROI circle centered on its house's avatar every frame
// (X/Z only, fixed height -- never rotates with the avatar's facing), but
// ONLY while DistanceMaintainMode (거리유지모드, toggled by B, synced across
// every computer) is active. The rest of the time the circle stays wherever
// it was originally drawn (the optimizer's fixed target).
//
// Two different positions matter here, per explicit request:
//  - The SHADER's content anchor (Regions.SetUserPosition) must always be
//    that house's own real avatar position -- deciding WHAT is revealed, and
//    never shifted by anything the local viewer does.
//  - This ring's own on-screen placement, and the shader's rendered placement
//    (_LocalOffset -- see Standard_WithZones.shader), both additionally carry
//    the LOCAL viewer's own movement delta on top of that anchor. That delta
//    is purely a per-client visual placement (this computer's rendering
//    only), so the ring and the actual revealed geometry always move
//    together as one set, without ever changing what is revealed or
//    touching another house's own view of their own space.
//
// For isMine (my own house's circle, target == my own avatar): the ring's
// own transform still tracks me directly below, same as always, but it must
// NEVER feed Regions.SetUserPosition. _Users[myType] also acts as a THIRD
// competing point inside Standard_WithZones.shader's own Voronoi tie-break
// for house1/house2's Remote content (calculateZoneClip's ZoneMode==2 branch,
// which this feature must never touch) -- confirmed live: continuously
// feeding it from my real, moving position let my own presence "steal"
// slices of house1's/house2's ROI purely by walking near their true spot,
// even though their own _Users entries never moved. _Users[myType] must stay
// exactly what LocalOptimizationRunner/Arrange_Walkin set it to once, at
// optimization time -- frozen, like it always was before this feature existed.
public class CircleFollowAvatar : MonoBehaviour
{
    public Transform target;
    public float height;
    public int houseIndex = -1;

    // True only for the local player's own house's circle -- that one is
    // already centered on me, so the "shift by how far I've moved" delta
    // below is meaningless for it (would double up on the fact I'm already
    // the one moving).
    public bool isMine;

    DistanceMaintainMode mode;

    void LateUpdate()
    {
        if (target == null) return;

        if (mode == null) mode = FindFirstObjectByType<DistanceMaintainMode>();
        if (mode == null || !mode.IsActive) return;

        Transform jointChest = AvatarJointHelper.FindJointChest(target);
        Vector3 anchorPos = jointChest != null ? jointChest.position : target.position;

        // Content anchor -- always that house's own real avatar position.
        // Never for isMine -- see class comment above.
        if (!isMine && houseIndex >= 0 && Regions.Instance != null)
        {
            Regions.Instance.SetUserPosition(houseIndex, anchorPos);
        }

        // Display placement -- anchor plus my own local movement delta.
        Vector3 displayPos = anchorPos;
        if (!isMine && mode.TryGetDeltaXZ(out Vector2 delta))
        {
            displayPos.x += delta.x;
            displayPos.z += delta.y;
        }

        transform.position = new Vector3(displayPos.x, height, displayPos.z);
    }
}

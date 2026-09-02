using UnityEngine;

// The other half of the "other houses stay a constant distance from me while
// panning" request -- CircleFollowAvatar handles the debug boundary/ROI
// circles, this handles the actual floor zone-clipping boundary (the
// Voronoi-style split between houses that Regions.cs feeds to the shader via
// userPosVec4 every frame). Both are meant to move together as "one set", per
// request, so this mirrors CircleFollowAvatar's approach: shift every OTHER
// house's zone marker (X/Z only, never my own) by exactly how far I've moved
// since panning turned on.
//
// Auto-installs itself (RuntimeInitializeOnLoadMethod, same convention as
// HeadsetHUD.cs/ConsoleNoiseFilter.cs) so no scene wiring is needed.
public class PanningWorldOffset : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var go = new GameObject("PanningWorldOffset");
        go.AddComponent<PanningWorldOffset>();
    }

    CameraController panningView;

    // Snapshot of every house's zone marker taken the moment panning turns on --
    // each frame's value is this baseline PLUS the current delta, not the
    // previous frame's value plus a new delta, so the shift never compounds/drifts
    // and cleanly resets whenever panning restarts (see hasBaseline below).
    Vector4[] baselineUserPosVec4;
    bool hasBaseline;

    void LateUpdate()
    {
        if (panningView == null) panningView = FindFirstObjectByType<CameraController>();

        var regions = Regions.Instance;
        if (panningView == null || regions == null || regions.userPosVec4 == null)
        {
            hasBaseline = false;
            return;
        }

        if (!panningView.TryGetPanningDeltaXZ(out Vector2 delta))
        {
            hasBaseline = false; // panning is off (or just turned off) -- stop tracking, leave markers where they are
            return;
        }

        if (!hasBaseline)
        {
            baselineUserPosVec4 = (Vector4[])regions.userPosVec4.Clone();
            hasBaseline = true;
        }

        int myType = regions.chooseHouseNum;
        for (int i = 0; i < regions.userPosVec4.Length && i < baselineUserPosVec4.Length; i++)
        {
            if (i == myType) continue; // my own zone marker is untouched by this -- only OTHER houses shift with me
            Vector4 b = baselineUserPosVec4[i];
            regions.userPosVec4[i] = new Vector4(b.x + delta.x, 0f, b.z + delta.y, 0f);
        }
    }
}

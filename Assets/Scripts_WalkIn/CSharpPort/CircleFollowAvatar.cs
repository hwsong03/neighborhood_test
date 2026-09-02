using UnityEngine;

// Keeps a boundary/ROI circle centered on its house's avatar every frame
// (X/Z only, fixed height -- never rotates with the avatar's facing), instead
// of staying fixed at the original post-optimization target. Per request:
// since the avatar itself is expected to visually shift as SceneSelection's
// optimization-offset system re-projects wherever the real person is
// currently standing, the circle should track that same, deliberate shift
// rather than being left behind.
public class CircleFollowAvatar : MonoBehaviour
{
    public Transform target;
    public float height;

    void LateUpdate()
    {
        if (target == null) return;
        Vector3 pos = target.position;
        transform.position = new Vector3(pos.x, height, pos.z);
    }
}

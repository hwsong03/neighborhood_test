using UnityEngine;

// The spectator/panning camera view (Display 2) this used to toggle on and off
// via keyboard P / right controller B has been removed -- everything now goes
// through the existing keyboard B (distance mode, see TransferManager.cs)
// instead of a second, separate toggle. This component just keeps the Display 2
// camera permanently off.
public class CameraController : MonoBehaviour
{
    void Start()
    {
        var cam = GetComponent<Camera>();
        if (cam != null) cam.enabled = false;
    }
}

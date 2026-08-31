using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    private Camera cam;
    public GameObject localAvatarNeck;

    [Header("Camera Initial Settings")]
    private Vector3 initialPosition = new Vector3(0, 2.3f, 0); // only need to modify x, z
    private Vector3 initialRotation = new Vector3(28, 0, 0); // only need to modify y -> localAvatarNeck forward direction
    private float near = 0.3f;
    private float fieldOfView = 70f;
    private float size = 1.0f;
    private float yRotationOffset = -90f;

    [Header("Perspective Camera Offset")]
    public float cameraDistance = 0.77f;  // distance behind target
    public float heightOffset = 1f;    // height above target

    // need to be orthographic camera and set display2

    //[Header("OneEuroFilter Settings")]
    private float filterFrequency = 90.0f;
    private float filterMinCutoff = 3.0f;
    private float filterBeta = 0.0f;
    private float filterDCutoff = 0.5f;

    private OneEuroFilter<Vector3> positionFilter;
    private OneEuroFilter<Quaternion> rotationFilter;

    // Initial pitch rotation (looking down)
    private Quaternion initialPitchRotation;

    bool isFound = false;



    // Start is called before the first frame update
    void Start()
    {
        cam = GetComponent<Camera>();
        //cam.orthographic = true;
        cam.targetDisplay = 1; // Display 2
        cam.nearClipPlane = near;
        cam.fieldOfView = fieldOfView;
        //cam.orthographicSize = size;

        this.transform.localPosition = initialPosition;
        this.transform.localEulerAngles = initialRotation;

        // Store the initial pitch rotation (X-axis tilt for looking down)
        initialPitchRotation = Quaternion.Euler(initialRotation.x, 0, initialRotation.z);

        positionFilter = new OneEuroFilter<Vector3>(filterFrequency, filterMinCutoff, filterBeta, filterDCutoff);
        rotationFilter = new OneEuroFilter<Quaternion>(filterFrequency, filterMinCutoff, filterBeta, filterDCutoff);
    }

    // Update is called once per frame
    void Update()
    {
        // Right controller B button toggles this panning/spectator view on and off --
        // for headset-only testing where there's no keyboard within reach. P key does
        // the same for desktop/simulator testing -- chosen because X/Y/Z/A/B are all
        // already bound to other things in this project (Regions.cs/Sender.cs/
        // Arrange_WalkIn.cs/TransferManager.cs/LocalOptimizationRunner.cs).
        if (OVRInput.GetDown(OVRInput.Button.Two) || Input.GetKeyDown(KeyCode.P))
        {
            cam.enabled = !cam.enabled;
            Debug.Log($"[CameraController] Panning view {(cam.enabled ? "enabled" : "disabled")}.");
        }
        if (!cam.enabled) return;

        // Check if localAvatarNeck was destroyed
        if (localAvatarNeck == null)
        {
            isFound = false;
        }

        if (!isFound)
        {
            GameObject localAvatar = GameObject.Find("LocalAvatar");
            if (localAvatar != null)
            {
                Transform jointHead = AvatarJointHelper.FindJointHead(localAvatar.transform);
                if (jointHead != null)
                {
                    localAvatarNeck = jointHead.gameObject;
                    isFound = true;
                }
            }
        }
        else
        {
            Vector3 neckPos = localAvatarNeck.transform.position;

            // Get neck's Y rotation as quaternion and apply offset
            Quaternion neckYRotation = Quaternion.Euler(0, localAvatarNeck.transform.eulerAngles.y, 0);
            Quaternion yRotationOffsetQuat = Quaternion.Euler(0, yRotationOffset, 0);
            Quaternion targetYRotation = neckYRotation * yRotationOffsetQuat;

            // Calculate camera position offset based on target's Y rotation
            Vector3 offset = targetYRotation * new Vector3(0, heightOffset, -cameraDistance);
            Vector3 targetPos = neckPos + offset;

            Vector3 filteredPos = positionFilter.Filter(targetPos);
            this.transform.position = filteredPos;

            // Combine Y rotation with initial pitch (X-axis tilt)
            Quaternion targetRotation = targetYRotation * initialPitchRotation;
            Quaternion filteredRotation = rotationFilter.Filter(targetRotation);
            this.transform.rotation = filteredRotation;
        }
    }
}

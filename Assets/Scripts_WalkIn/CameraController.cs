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

        // Panning starts OFF -- the scene's serialized default for this Camera
        // component was enabled, so without this it was on the moment Play
        // started, before anyone had touched B at all.
        cam.enabled = false;

        this.transform.localPosition = initialPosition;
        this.transform.localEulerAngles = initialRotation;

        // Store the initial pitch rotation (X-axis tilt for looking down)
        initialPitchRotation = Quaternion.Euler(initialRotation.x, 0, initialRotation.z);

        positionFilter = new OneEuroFilter<Vector3>(filterFrequency, filterMinCutoff, filterBeta, filterDCutoff);
        rotationFilter = new OneEuroFilter<Quaternion>(filterFrequency, filterMinCutoff, filterBeta, filterDCutoff);
    }

    // Meta XR Simulator relays a keyboard press into a simulated controller press
    // too, one frame apart -- so a single physical B tap can satisfy
    // OVRInput.GetDown(...) and Input.GetKeyDown(KeyCode.B) on two DIFFERENT
    // frames, toggling twice (enabled immediately followed by disabled) instead
    // of once. TransferManager's B handler (keyboard-only) doesn't have this
    // problem; this one checks both sources, so it needs its own debounce.
    // Same class of issue as LocalOptimizationRunner's TriggerHoldSeconds, but a
    // short cooldown here instead of a hold-to-confirm, since this is a toggle
    // meant to respond to a single tap, not a held button.
    const float ToggleCooldownSeconds = 0.3f;
    float lastToggleTime = -999f;

    // Update is called once per frame
    void Update()
    {
        // Right controller B button specifically (NOT left Y -- OVRInput.Button.Two
        // alone fires from either controller, so the controller mask is required to
        // exclude left Y) and keyboard B toggle this panning/spectator view on and
        // off -- press-again-to-turn-off, same as any toggle.
        bool togglePressed = OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch) || Input.GetKeyDown(KeyCode.B);
        if (togglePressed && Time.unscaledTime - lastToggleTime >= ToggleCooldownSeconds)
        {
            lastToggleTime = Time.unscaledTime;
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

    // Called by LocalOptimizationRunner right before applying a Z/M optimization
    // result (whether run locally or received from another client) -- the result
    // is only meaningful to look at from the normal avatar viewpoint, not this
    // spectator display, so force back to the normal view instead of leaving
    // panning on and having the result go unseen.
    public void DisablePanningView()
    {
        if (cam != null && cam.enabled)
        {
            cam.enabled = false;
            Debug.Log("[CameraController] Panning view disabled (forced off for optimization).");
        }
    }
}

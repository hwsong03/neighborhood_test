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

    void Update() { }
}

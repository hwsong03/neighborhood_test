using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LocalROI : MonoBehaviour
{
    public Transform LocalAvatarRoot;
    public bool isFound = false;

    private Transform localROI;

    private SceneSelection sceneSelection;
    private Renderer localROIRenderer;

    // Start is called before the first frame update
    void Start()
    {
        localROI = this.transform;

        // Try to get Renderer from this object or children
        localROIRenderer = GetComponent<Renderer>();
        if (localROIRenderer == null)
        {
            localROIRenderer = GetComponentInChildren<Renderer>();
        }

        sceneSelection = FindObjectOfType<SceneSelection>();
    }

    // Update is called once per frame
    void Update()
    {
        if (localROIRenderer != null)
        {
            // Check if optimization has happened by looking for traverseZone objects
            // (created after Y key on server, or after RPC on client)
            bool optimizationHappened = GameObject.Find("traverseZone") != null
                                     || GameObject.Find("traverseZone_0") != null;

            if (optimizationHappened)
            {
                // After optimization: follow the new DistanceMaintainMode
                // (거리유지모드), not the legacy SceneSelection.modeB -- modeB is
                // permanently false now that B was disconnected from the old
                // ModeBCalculator path (it directly moved other players' real
                // avatar/house transforms, which conflicted with this feature).
                localROIRenderer.enabled = DistanceMaintainMode.Instance != null && DistanceMaintainMode.Instance.IsActive;
            }
            else
            {
                // TEMP: was "always show" before optimization -- hidden for the
                // 2-headset test along with the other debug outlines (LocalOptimizationRunner.cs).
                localROIRenderer.enabled = false;
            }
        }

        if (LocalAvatarRoot == null)
        {
            isFound = false;
        }

        if (!isFound)
        {
            GameObject localAvatar = GameObject.Find("LocalAvatar");
            if (localAvatar != null)
            {
                LocalAvatarRoot = AvatarJointHelper.FindJointChest(localAvatar.transform);
                if (LocalAvatarRoot != null)
                {
                    isFound = true;
                }
            }
        }
        else
        {
            // only apply x and z position, keep y position unchanged
            localROI.position = new Vector3(LocalAvatarRoot.position.x, localROI.position.y, LocalAvatarRoot.position.z);
        }
    }
}

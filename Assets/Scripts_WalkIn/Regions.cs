using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Linq;
using UnityEditor;

public class Regions : MonoBehaviour
{
    // Singleton instance
    public static Regions Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[Regions] Multiple instances detected. Destroying duplicate.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public List<GameObject> houses = new List<GameObject>();

    private List<List<Vector3>> selectedZones = new List<List<Vector3>>();

    // track user pos
    private GameObject arrangeHousesObj;
    public bool initialCharactersIn = false;
    public List<GameObject> characters = new List<GameObject>();
    public Vector4[] userPosVec4;

    [Header("OneEuroFilter Settings for Zone Boundaries")]
    [Tooltip("Filter frequency - should match update rate")]
    public float zoneFilterFrequency = 90.0f;

    [Tooltip("Minimum cutoff frequency - lower = smoother but more lag")]
    public float zoneFilterMinCutoff = 1.0f;

    [Tooltip("Speed coefficient - higher = faster response to quick movements")]
    public float zoneFilterBeta = 0.0f;

    [Tooltip("Derivative cutoff frequency")]
    public float zoneFilterDCutoff = 1.0f;

    [Tooltip("Enable filtering for smoother zone boundaries")]
    public bool enableZoneFiltering = true;

    // OneEuroFilter instances for each user position
    private OneEuroFilter<Vector3>[] userPosFilters;
    private bool filtersInitialized = false;

    // which house as main user
    public int chooseHouseNum;

    public GameObject myHouse;
    public GameObject otherHouse1;
    public GameObject otherHouse2;

    // sending to python
    public Sender sender;
    public bool receiveFromPython = false;
    private string userPosString = "";


    // ZoneMode enum 
    public enum ZoneMode
    {
        Off = 0,        // Zone 
        Local = 1,      // LocalZone
        Remote = 2      // RemoteZone
    }

    void Start()
    {

        userPosVec4 = new Vector4[houses.Count];

        arrangeHousesObj = GameObject.Find("Arrange");

        // Initialize filters for each user
        InitializeFilters();
    }

    /// <summary>
    /// Initialize OneEuroFilters for user positions
    /// </summary>
    private void InitializeFilters()
    {
        if (houses.Count > 0)
        {
            userPosFilters = new OneEuroFilter<Vector3>[houses.Count];
            for (int i = 0; i < houses.Count; i++)
            {
                userPosFilters[i] = new OneEuroFilter<Vector3>(
                    zoneFilterFrequency,
                    zoneFilterMinCutoff,
                    zoneFilterBeta,
                    zoneFilterDCutoff
                );
            }
            filtersInitialized = true;
            Debug.Log($"[Regions] Zone filters initialized: count={houses.Count}, freq={zoneFilterFrequency}, minCutoff={zoneFilterMinCutoff}, beta={zoneFilterBeta}");
        }
    }

    /// <summary>
    /// Reinitialize filters (call this if filter parameters change at runtime)
    /// </summary>
    public void ReinitializeFilters()
    {
        InitializeFilters();
    }

    /// <summary>
    /// Set user position with optional filtering for smooth zone boundaries
    /// Call this instead of directly setting userPosVec4[i]
    /// </summary>
    public void SetUserPosition(int index, Vector3 position)
    {
        if (index < 0 || index >= userPosVec4.Length) return;

        Vector3 finalPos = position;

        // Apply OneEuroFilter if enabled
        if (enableZoneFiltering && filtersInitialized && userPosFilters != null && index < userPosFilters.Length)
        {
            finalPos = userPosFilters[index].Filter(position);
        }

        userPosVec4[index] = new Vector4(finalPos.x, 0, finalPos.z, 0);
    }

    /// <summary>
    /// Set user position directly without filtering (for initialization or Y key reset)
    /// </summary>
    public void SetUserPositionDirect(int index, Vector3 position)
    {
        if (index < 0 || index >= userPosVec4.Length) return;
        userPosVec4[index] = new Vector4(position.x, 0, position.z, 0);
    }



    // Update is called once per frame
    void Update()
    {

        // character initial pos
        if (initialCharactersIn == false && arrangeHousesObj.GetComponent<Arrange_Walkin>().characterInitialized == true)
        {
            Transform level1 = arrangeHousesObj.transform.GetChild(0);
            foreach (Transform users in level1)
            {
                //Debug.Log(users.gameObject.name);

                // here you have to add the hierarchy?
                
                characters.Add(users.gameObject);

            }

            initialCharactersIn = true;

        }


        // python communication 
        if (Input.GetKeyDown(KeyCode.X))
        {
            userPosString = "";
            for (int i = 0; i < characters.Count; i++)
            {
                

                Vector3 posAtOrigin = getTransAtOrigin(houses[i], characters[i]);
                //Debug.Log(posAtOrigin);

                if (i == 0)
                {

                    userPosString = userPosString + "X((" + posAtOrigin.x.ToString() + ", " + posAtOrigin.z.ToString() + "), ";
                }
                else if (i == characters.Count - 1)
                {
                    userPosString = userPosString + "(" + posAtOrigin.x.ToString() + ", " + posAtOrigin.z.ToString() + "))";
                }
                else
                {
                    userPosString = userPosString + "(" + posAtOrigin.x.ToString() + ", " + posAtOrigin.z.ToString() + "), ";

                }


            }



            Debug.Log(userPosString); // ((1.5, 0), (1.5, 0), (2.47, 3.26), (0.8, 1.5))

            receiveFromPython = false;
            sender.SendToPython(userPosString);

        }



        
        // visualize traverse zones

        selectedZones = arrangeHousesObj.GetComponent<Arrange_Walkin>().selectedZones;

        Vector4[] zone1Vec4;
        Vector4[] zone2Vec4;

        if (chooseHouseNum == 0)
        {
            zone1Vec4 = new Vector4[selectedZones[1].Count];
            for (int i = 0; i < selectedZones[1].Count; i++)
            {
                zone1Vec4[i] = new Vector4(selectedZones[1][i].x, selectedZones[1][i].y, selectedZones[1][i].z, 0);
            }

            zone2Vec4 = new Vector4[selectedZones[2].Count];
            for (int i = 0; i < selectedZones[2].Count; i++)
            {
                zone2Vec4[i] = new Vector4(selectedZones[2][i].x, selectedZones[2][i].y, selectedZones[2][i].z, 0);
            }

            feedZone(myHouse, userPosVec4, 0, 0, ZoneMode.Local);
            feedZone(otherHouse1, userPosVec4, 0, 1, ZoneMode.Remote);
            feedZone(otherHouse2, userPosVec4, 0, 2, ZoneMode.Remote);


        }
        else if (chooseHouseNum == 1)
        {
            zone1Vec4 = new Vector4[selectedZones[0].Count];
            for (int i = 0; i < selectedZones[0].Count; i++)
            {
                zone1Vec4[i] = new Vector4(selectedZones[0][i].x, selectedZones[0][i].y, selectedZones[0][i].z, 0);
            }

            zone2Vec4 = new Vector4[selectedZones[2].Count];
            for (int i = 0; i < selectedZones[2].Count; i++)
            {
                zone2Vec4[i] = new Vector4(selectedZones[2][i].x, selectedZones[2][i].y, selectedZones[2][i].z, 0);
            }

            //Debug.Log(userPosVec4[1]);


            feedZone(myHouse, userPosVec4, 1, 1, ZoneMode.Local);

            feedZone(otherHouse1, userPosVec4, 1, 0, ZoneMode.Remote);
            feedZone(otherHouse2, userPosVec4, 1, 2, ZoneMode.Remote);


        }
        else if (chooseHouseNum == 2)
        {

            zone1Vec4 = new Vector4[selectedZones[0].Count];
            for (int i = 0; i < selectedZones[0].Count; i++)
            {
                zone1Vec4[i] = new Vector4(selectedZones[0][i].x, selectedZones[0][i].y, selectedZones[0][i].z, 0);
            }


            zone2Vec4 = new Vector4[selectedZones[1].Count];
            for (int i = 0; i < selectedZones[1].Count; i++)
            {
                zone2Vec4[i] = new Vector4(selectedZones[1][i].x, selectedZones[1][i].y, selectedZones[1][i].z, 0);
            }

            feedZone(myHouse, userPosVec4, 2, 2, ZoneMode.Local);
            feedZone(otherHouse1, userPosVec4, 2, 0, ZoneMode.Remote);
            feedZone(otherHouse2, userPosVec4, 2, 1, ZoneMode.Remote);
        }
        



    }









    void feedZone(GameObject whichPrefab, Vector4[] userPosVec4, int baseRegion, int whichRegion, ZoneMode zoneMode, float zoneRadius = 1.2f)
    {
        // Broadcast as a global shader property instead of a per-material array --
        // Material.SetVectorArray on a property that has no Properties{} block entry
        // (as _Users doesn't) isn't reliably readable/applied per-material; the
        // same _Users data is identical for every house's material anyway, so a
        // global is both more correct and cheaper than re-setting it per-renderer.
        Shader.SetGlobalVectorArray("_Users", userPosVec4);

        Renderer[] renderers = whichPrefab.GetComponentsInChildren<Renderer>();
        foreach (Renderer rend in renderers)
        {
            if (rend.name.StartsWith("Ceiling_"))
            {
                for (int i = 0; i < rend.sharedMaterials.Length; i++)
                {
                    var material = rend.sharedMaterials[i];
                }
            }
            else
            {
                for (int i = 0; i < rend.sharedMaterials.Length; i++)
                {
                    var material = rend.sharedMaterials[i];

                    // user information
                    material.SetInt("_Length", userPosVec4.Length);
                    material.SetInt("_BaseRegion", baseRegion);
                    material.SetInt("_WhichRegion", whichRegion);


                    material.SetInt("_ZoneMode", (int)zoneMode);
                    material.SetFloat("_ZoneRadius", zoneRadius);
                    material.SetFloat("_EnableZoneClipping", 1.0f); //
                    material.SetFloat("_P", 2.0f); // Voronoi distance parameter
                }
            }
        }
    }



    Vector3 getTransAtOrigin(GameObject parent, GameObject child)
    {
        // Get A's current transformation matrix
        Matrix4x4 worldToLocalMatrix = parent.transform.worldToLocalMatrix;

        // Calculate B's position relative to A's original position
        Vector3 posAtOrigin = worldToLocalMatrix.MultiplyPoint3x4(child.transform.position);


        return posAtOrigin;
    }


}

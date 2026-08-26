using System.Collections.Generic;
using UnityEngine;

// Keeps other players' houses/avatars invisible (still fully functional/networked
// underneath) from the moment this client's house index is known until the Y-key
// optimization arranges everyone into their real relative positions.
public class PersonalSpaceGate : MonoBehaviour
{
    public static PersonalSpaceGate Instance { get; private set; }

    public Arrange_Walkin arrangeWalkin;

    private bool gating = false;
    private bool revealed = false;
    private readonly HashSet<GameObject> hiddenObjects = new HashSet<GameObject>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Called once HouseJoinOrderAssigner knows this client's real house index.
    public void BeginGating()
    {
        if (revealed) return;
        gating = true;
    }

    void Update()
    {
        if (!gating || revealed || arrangeWalkin == null || arrangeWalkin.houses == null) return;

        int myIndex = arrangeWalkin.chooseHouseNum;
        for (int i = 0; i < arrangeWalkin.houses.Count; i++)
        {
            if (i == myIndex) continue;
            HideRenderers(arrangeWalkin.houses[i]);
        }

        HideRenderers(GameObject.Find("RemoteAvatar"));
        HideRenderers(GameObject.Find("RemoteAvatar1"));
    }

    void HideRenderers(GameObject go)
    {
        if (go == null || hiddenObjects.Contains(go)) return;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            r.enabled = false;
        }
        hiddenObjects.Add(go);
    }

    // Called when Y-key optimization arranges houses/avatars into their real positions.
    public void Reveal()
    {
        gating = false;
        revealed = true;
        foreach (var go in hiddenObjects)
        {
            if (go == null) continue;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = true;
            }
        }
        hiddenObjects.Clear();
    }
}

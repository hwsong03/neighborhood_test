using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Two small always-on VR HUD elements, parented to whichever OVRCameraRig's
// CenterEyeAnchor exists on this client, so they move with the headset view like
// a real heads-up display:
//
// 1. A persistent bottom-left legend listing which key/controller button maps to
//    which action -- static, identical on every client, no networking involved.
// 2. A transient ~3-second notification ("houseN pressed ... (ALGO optimization)")
//    shown on EVERY headset (not just whoever pressed) when anyone starts a Z/M
//    optimization -- fed via TransferManager.RPC_BroadcastTriggerNotification,
//    called from LocalOptimizationRunner right as the key/trigger fires.
//
// Auto-installs itself (RuntimeInitializeOnLoadMethod, same convention as
// ConsoleNoiseFilter.cs) so no scene wiring is needed -- one GameObject gets
// created the moment Play starts, and it finds the headset rig on its own once
// Fusion/Meta Avatar spawn it in.
public class HeadsetHUD : MonoBehaviour
{
    public static HeadsetHUD Instance { get; private set; }

    // Distance in front of the headset the HUD sits at, and how far left/down
    // (from center) the persistent legend is offset. Untested visually (no Play
    // Mode access) -- if this looks too close/far or too big/small once tried in
    // the headset, these four numbers are the ones to adjust first.
    const float Distance = 1.2f;
    const float LegendOffsetX = -0.35f;
    const float LegendOffsetY = -0.25f;
    const float NotificationDuration = 3f;

    Transform centerEye;
    GameObject notificationRoot;
    Text notificationText;
    Coroutine hideRoutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (Instance != null) return;
        var go = new GameObject("HeadsetHUD");
        go.AddComponent<HeadsetHUD>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        // Meta Avatar/Fusion spawn the rig at runtime, after this component
        // already exists -- keep checking every frame until it shows up, same
        // pattern LocalROI.cs uses for LocalAvatarRoot.
        if (centerEye == null)
        {
            var rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig != null && rig.centerEyeAnchor != null)
            {
                centerEye = rig.centerEyeAnchor;
                BuildUI();
            }
        }
    }

    // Semi-transparent black backing behind both HUD blocks, so the (opaque
    // white) text stays readable against a bright real-world passthrough
    // background instead of just floating unbacked over whatever's behind it.
    static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.6f);
    const float TextPadding = 24f; // inset between the panel edge and the text, in canvas units

    void BuildUI()
    {
        var legendCanvas = CreateWorldSpaceCanvas("HUD_Legend", new Vector3(LegendOffsetX, LegendOffsetY, Distance), 0.001f, new Vector2(900, 260));
        CreateBackgroundPanel(legendCanvas.transform);
        var legendText = CreateText(legendCanvas.transform, TextAnchor.LowerLeft, 30);
        // Controller-only -- this is only ever seen inside the headset, so the
        // keyboard-only equivalents (Z/M/P, and the server-only A/B freeze/distance
        // mode toggle, which has no controller mapping at all -- see TransferManager.
        // Update()) are left off. What needed spelling out was the two TRIGGERS
        // (one per hand, two different functions) -- panning is a single button,
        // shown as one line.
        legendText.text =
            "Right Trigger: run DE optimization\n" +
            "Left Trigger: run DIRECT optimization\n" +
            "B: toggle panning view";

        var notifCanvas = CreateWorldSpaceCanvas("HUD_Notification", new Vector3(0f, 0.18f, Distance), 0.0012f, new Vector2(850, 220));
        notificationRoot = notifCanvas.gameObject;
        CreateBackgroundPanel(notifCanvas.transform);
        notificationText = CreateText(notifCanvas.transform, TextAnchor.MiddleCenter, 40);
        notificationRoot.SetActive(false);
    }

    Canvas CreateWorldSpaceCanvas(string name, Vector3 localOffset, float scale, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(centerEye, false);
        go.transform.localPosition = localOffset;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * scale;

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;

        go.AddComponent<CanvasScaler>();
        return canvas;
    }

    // Plain flat-color Image filling the canvas -- added BEFORE the Text sibling
    // (below, per BuildUI's call order) so it renders behind it, not in front.
    void CreateBackgroundPanel(Transform parent)
    {
        var go = new GameObject("Background", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var image = go.AddComponent<Image>();
        image.color = PanelColor;
    }

    Text CreateText(Transform parent, TextAnchor anchor, int fontSize)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(TextPadding, TextPadding);
        rt.offsetMax = new Vector2(-TextPadding, -TextPadding);

        var text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.alignment = anchor;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        return text;
    }

    // Called locally on every client that receives TransferManager's broadcast
    // RPC (including the one who pressed the key/trigger, per the request that
    // this shows "on house0's own headset too").
    public void ShowNotification(string message)
    {
        if (notificationText == null) return; // rig/UI not built yet on this client
        notificationText.text = message;
        notificationRoot.SetActive(true);

        if (hideRoutine != null) StopCoroutine(hideRoutine);
        hideRoutine = StartCoroutine(HideAfterDelay());
    }

    IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(NotificationDuration);
        if (notificationRoot != null) notificationRoot.SetActive(false);
        hideRoutine = null;
    }
}

using System.Collections;
using System.Linq;
using UnityEngine;
using Fusion;
using Fusion.Sockets;

// Derives this client's house index (0/1/2) from Photon Fusion join order and
// pushes it into SceneSelection/Arrange_Walkin/Regions/OffsetCalculator.
// Replaces the old workflow of hand-setting type/chooseHouseNum per machine,
// which breaks the moment the same build/scene runs on every computer.
//
// Does NOT hide or gate anything -- houses/avatars stay visible exactly as
// before (full for my own house, clipped to the individual-area circle for
// others, via Regions.cs's existing zone-clip shader feed). This component's
// only job is making sure every client's chooseHouseNum agrees on who is
// house0/1/2, consistently, by Fusion join order.
public class HouseJoinOrderAssigner : MonoBehaviour, INetworkRunnerCallbacks
{
    public Arrange_Walkin arrangeWalkin;

    private bool assigned = false;

    // Rank-vs-time-stable tracking, used to make the index immune to Photon's
    // room-lifetime actor numbering (see TryAssign below) while still tolerating
    // a brief window where different clients haven't yet converged on the same
    // ActivePlayers view during simultaneous connects.
    private int lastComputedIdx = -1;
    private float idxStableSince = -1f;
    private const float StabilitySeconds = 1f;

    void Start()
    {
        StartCoroutine(WaitForRunner());
    }

    IEnumerator WaitForRunner()
    {
        NetworkRunner runner = null;
        while (runner == null || !runner.IsRunning)
        {
            runner = FindFirstObjectByType<NetworkRunner>();
            yield return null;
        }

        runner.AddCallbacks(this);

        // Keep retrying every frame until assignment succeeds. A single attempt
        // right after IsRunning becomes true can fire before LocalPlayer.IsRealPlayer
        // flips true, and with no retry this client's house index would never get
        // assigned at all.
        while (!assigned)
        {
            TryAssign(runner);
            yield return null;
        }
    }

    void TryAssign(NetworkRunner runner)
    {
        if (assigned || runner == null || !runner.IsRunning) return;
        if (!runner.LocalPlayer.IsRealPlayer) return;

        // Rank the local player among CURRENTLY connected players (sorted by
        // PlayerId ascending) instead of using raw PlayerId magnitude. Raw
        // PlayerId reflects the room's cumulative join history -- Photon actor
        // numbers are never reused within a room's lifetime, even after someone
        // disconnects -- and DefaultRoomName is a hardcoded fixed string (so lab
        // machines auto-join without typing a room code), so the same room can
        // persist across separate test runs and leave stale actor numbers
        // allocated. A client that is really the 2nd person connected right now
        // could still get PlayerId==3 (house2) instead of PlayerId==2 (house1)
        // if the room has a leftover gap from an earlier session. Ranking within
        // ActivePlayers sidesteps that: it's always 0/1/2 for however many
        // people are actually connected right now, regardless of the room's history.
        //
        // An earlier version of this feature used this same ActivePlayers-ranking
        // approach and moved away from it (see git history) because different
        // clients can briefly see different ActivePlayers snapshots of each other
        // while a session is still connecting. Instead of computing this once and
        // locking immediately, keep recomputing every frame and only commit once
        // the computed index has held steady for StabilitySeconds -- long enough
        // for a connecting session's player list to converge everywhere, short
        // enough not to meaningfully delay startup once it has.
        var sortedPlayers = runner.ActivePlayers.OrderBy(p => p.PlayerId).ToList();
        int idx = sortedPlayers.IndexOf(runner.LocalPlayer);

        if (idx < 0)
        {
            // Not visible in our own ActivePlayers view yet -- retry next frame.
            lastComputedIdx = -1;
            idxStableSince = -1f;
            return;
        }

        if (idx != lastComputedIdx)
        {
            lastComputedIdx = idx;
            idxStableSince = Time.time;
            return; // value just changed -- wait for it to hold steady before committing
        }

        if (Time.time - idxStableSince < StabilitySeconds) return;

        if (idx > 2)
        {
            Debug.LogWarning($"[HouseJoinOrderAssigner] Join position {idx} exceeds available houses (0-2); clamping to house2.");
            idx = 2;
        }

        if (arrangeWalkin == null)
        {
            Debug.LogError("[HouseJoinOrderAssigner] arrangeWalkin reference not set!");
            return;
        }

        assigned = true;

        arrangeWalkin.chooseHouseNum = idx;
        arrangeWalkin.isServer = (idx == 0);

        var sceneSelection = arrangeWalkin.sceneSelection != null
            ? arrangeWalkin.sceneSelection.GetComponent<SceneSelection>()
            : null;
        sceneSelection?.ApplyType(idx);

        if (Regions.Instance != null) Regions.Instance.chooseHouseNum = idx;
        if (OffsetCalculator.Instance != null) OffsetCalculator.Instance.SetId(idx);
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        TryAssign(runner);
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, System.Collections.Generic.List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, System.Collections.Generic.Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, System.ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}

using System.Collections;
using UnityEngine;
using Fusion;
using Fusion.Sockets;

// Derives this client's house index (0/1/2) from Photon Fusion join order and
// pushes it into SceneSelection/Arrange_Walkin/Regions/OffsetCalculator.
// Replaces the old workflow of hand-setting type/chooseHouseNum per machine,
// which breaks the moment the same build/scene runs on every computer.
public class HouseJoinOrderAssigner : MonoBehaviour, INetworkRunnerCallbacks
{
    public Arrange_Walkin arrangeWalkin;

    private bool assigned = false;

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
        // flips true, and with no retry that meant this client's house index was
        // never assigned at all (SceneSelection.ApplyType/PersonalSpaceGate.BeginGating
        // never ran for it) -- previously observed as "second computer's second
        // avatar's house never configured".
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

        // Use Fusion's own network-authoritative PlayerId directly as the house
        // index, NOT this client's locally-observed position within
        // runner.ActivePlayers. That position depends on which peers THIS client
        // happens to have heard about by the time it computes it -- two clients
        // (or even the same client at two different moments) can see different
        // ActivePlayers snapshots while a session is still connecting, so ranking
        // within that snapshot previously produced a DIFFERENT house index for
        // the same physical player depending on which machine asked (observed:
        // the first-joined computer showed itself as house0 locally, but a
        // second computer saw that same player as house1). PlayerId is assigned
        // once by the server in join order and is identical everywhere the
        // instant any client learns a player exists at all, so indexing by it
        // directly removes the race entirely.
        int idx = runner.LocalPlayer.PlayerId;

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

        PersonalSpaceGate.Instance?.BeginGating();

        Debug.Log($"[HouseJoinOrderAssigner] Local player {runner.LocalPlayer} joined at position {idx} -> house{idx} (isServer={arrangeWalkin.isServer})");
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

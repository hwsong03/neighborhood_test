using System.Collections;
using System.Linq;
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
        TryAssign(runner);
    }

    void TryAssign(NetworkRunner runner)
    {
        if (assigned || runner == null || !runner.IsRunning) return;
        if (!runner.LocalPlayer.IsRealPlayer) return;

        var sorted = runner.ActivePlayers.OrderBy(p => p.PlayerId).ToList();
        int idx = sorted.IndexOf(runner.LocalPlayer);
        if (idx < 0) return;

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

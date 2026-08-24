using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

#if META_AVATAR_SDK_DEFINED
using Oculus.Avatar2;
using Meta.XR.MultiplayerBlocks.Shared;
#endif

/// <summary>
/// 씬에 배치하면 모든 Remote 아바타에 대해 Late Joiner 문제를 자동으로 수정합니다.
/// 프리팹을 수정하지 않고 사용할 수 있습니다.
/// </summary>
public class LateJoinerAvatarFixManager : MonoBehaviour
{
    [Header("Settings")]
    public float checkInterval = 1f;
    public float reloadDelay = 0.5f;
    public int maxRetriesPerAvatar = 5;

#if META_AVATAR_SDK_DEFINED
    private Dictionary<ulong, int> _retryCountMap = new Dictionary<ulong, int>();
    private HashSet<ulong> _loadedAvatars = new HashSet<ulong>();
    private float _lastCheckTime;

    private void Update()
    {
        if (Time.time - _lastCheckTime < checkInterval)
            return;

        _lastCheckTime = Time.time;
        
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient)
            return;

        CheckAllRemoteAvatars();
    }

    private void CheckAllRemoteAvatars()
    {
        var avatars = FindObjectsOfType<AvatarEntity>();
        
        foreach (var avatar in avatars)
        {
            var netObj = avatar.GetComponent<NetworkObject>();
            if (netObj == null || !netObj.IsSpawned)
                continue;

            // Local 아바타는 스킵
            if (netObj.IsOwner)
                continue;

            ulong ownerId = netObj.OwnerClientId;

            // 이미 로드된 아바타는 스킵
            if (_loadedAvatars.Contains(ownerId))
                continue;

            // 아바타가 정상 로드되었는지 확인
            if (avatar.HasJoints && avatar.IsCreated)
            {
                _loadedAvatars.Add(ownerId);
                Debug.Log($"[LateJoinerManager] Avatar loaded for Owner:{ownerId}");
                continue;
            }

            // 재시도 횟수 확인
            if (!_retryCountMap.ContainsKey(ownerId))
                _retryCountMap[ownerId] = 0;

            if (_retryCountMap[ownerId] >= maxRetriesPerAvatar)
                continue;

            _retryCountMap[ownerId]++;

            // OculusId 확인
            var behaviour = avatar.GetComponent<IAvatarBehaviour>();
            if (behaviour != null && behaviour.OculusId != 0)
            {
                Debug.Log($"[LateJoinerManager] Reloading avatar for Owner:{ownerId}, OculusId:{behaviour.OculusId}, Retry:{_retryCountMap[ownerId]}");
                avatar.ReloadAvatarManually();
            }
            else
            {
                Debug.Log($"[LateJoinerManager] Waiting for OculusId, Owner:{ownerId}, Retry:{_retryCountMap[ownerId]}");
            }
        }
    }

    /// <summary>
    /// 수동으로 모든 Remote 아바타 리로드
    /// </summary>
    [ContextMenu("Force Reload All Remote Avatars")]
    public void ForceReloadAllRemoteAvatars()
    {
        var avatars = FindObjectsOfType<AvatarEntity>();
        
        foreach (var avatar in avatars)
        {
            var netObj = avatar.GetComponent<NetworkObject>();
            if (netObj == null || netObj.IsOwner)
                continue;

            Debug.Log($"[LateJoinerManager] Force reloading avatar for Owner:{netObj.OwnerClientId}");
            avatar.ReloadAvatarManually();
        }

        // 카운트 리셋
        _retryCountMap.Clear();
        _loadedAvatars.Clear();
    }
#endif
}

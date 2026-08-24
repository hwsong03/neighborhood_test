using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using Oculus.Avatar2;
using Meta.XR.MultiplayerBlocks.Shared;

/// <summary>
/// 멀티플레이어 아바타 동기화 문제를 디버깅합니다.
/// 이 스크립트를 씬의 빈 GameObject에 추가하세요.
/// </summary>
public class AvatarNetworkDebugger : MonoBehaviour
{
    [Header("Debug Settings")]
    public bool enableDebugLog = true;
    public float debugInterval = 2f;
    public KeyCode debugKey = KeyCode.F1;
    
    private float lastDebugTime;

    private void Update()
    {
        // F1 키를 누르면 즉시 디버그 출력
        if (Input.GetKeyDown(debugKey))
        {
            PrintFullDebugInfo();
        }
        
        // 주기적 디버그
        if (enableDebugLog && Time.time - lastDebugTime > debugInterval)
        {
            lastDebugTime = Time.time;
            PrintAvatarStatus();
        }
    }

    private void PrintAvatarStatus()
    {
        var avatars = FindObjectsOfType<OvrAvatarEntity>();
        Debug.Log($"=== [AvatarDebug] 총 아바타 수: {avatars.Length} ===");
        
        foreach (var avatar in avatars)
        {
            var netObj = avatar.GetComponent<NetworkObject>();
            var avatarLod = avatar.GetComponent<AvatarLOD>();
            
            string ownerInfo = netObj != null ? $"Owner:{netObj.OwnerClientId}" : "No NetworkObject";
            string lodInfo = avatarLod != null ? $"LOD:{avatarLod.Level}" : "No LOD";
            string activeInfo = avatar.EntityActive ? "Active" : "Inactive";
            string localInfo = avatar.IsLocal ? "Local" : "Remote";
            string skeletonInfo = avatar.HasJoints ? "HasSkeleton" : "NoSkeleton";
            
            // 렌더러 상태 확인
            var renderers = avatar.GetComponentsInChildren<Renderer>(true);
            int enabledRenderers = 0;
            foreach (var r in renderers) if (r.enabled) enabledRenderers++;
            string renderInfo = $"Render:{enabledRenderers}/{renderers.Length}";
            
            // 위치 정보
            string posInfo = $"Pos:{avatar.transform.position.ToString("F1")}";
            
            Debug.Log($"[AvatarDebug] {avatar.gameObject.name} | {localInfo} | {ownerInfo} | {lodInfo} | {activeInfo} | {skeletonInfo} | {renderInfo} | {posInfo}");
        }
    }

    private void PrintFullDebugInfo()
    {
        Debug.Log("========== [AvatarDebug] FULL DEBUG INFO ==========");
        
        // NetworkManager 상태
        if (NetworkManager.Singleton != null)
        {
            var nm = NetworkManager.Singleton;
            Debug.Log($"[Network] IsServer:{nm.IsServer} IsClient:{nm.IsClient} IsHost:{nm.IsHost}");
            Debug.Log($"[Network] LocalClientId:{nm.LocalClientId}");
            
            // ConnectedClientsIds는 서버에서만 접근 가능
            if (nm.IsServer || nm.IsHost)
            {
                Debug.Log($"[Network] ConnectedClients:{nm.ConnectedClientsIds.Count}");
                foreach (var clientId in nm.ConnectedClientsIds)
                {
                    Debug.Log($"  - ClientId: {clientId}");
                }
            }
        }
        else
        {
            Debug.LogWarning("[Network] NetworkManager.Singleton is NULL!");
        }

        // AvatarLODManager 상태
        if (AvatarLODManager.hasInstance)
        {
            Debug.Log($"[LODManager] Exists: true");
        }
        else
        {
            Debug.LogWarning("[LODManager] AvatarLODManager not found!");
        }

        // 모든 아바타 상세 정보
        var avatars = FindObjectsOfType<OvrAvatarEntity>();
        Debug.Log($"[Avatars] Total count: {avatars.Length}");
        
        int index = 0;
        foreach (var avatar in avatars)
        {
            Debug.Log($"--- Avatar [{index}]: {avatar.gameObject.name} ---");
            
            // 기본 정보
            Debug.Log($"  IsLocal: {avatar.IsLocal}");
            Debug.Log($"  EntityActive: {avatar.EntityActive}");
            Debug.Log($"  HasJoints: {avatar.HasJoints}");
            Debug.Log($"  IsCreated: {avatar.IsCreated}");
            
            // NetworkObject 정보
            var netObj = avatar.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                Debug.Log($"  NetworkObject.IsSpawned: {netObj.IsSpawned}");
                Debug.Log($"  NetworkObject.OwnerClientId: {netObj.OwnerClientId}");
                Debug.Log($"  NetworkObject.IsOwner: {netObj.IsOwner}");
            }
            else
            {
                Debug.LogWarning($"  NetworkObject: NULL");
            }
            
            // LOD 정보
            var avatarLod = avatar.GetComponent<AvatarLOD>();
            if (avatarLod != null)
            {
                Debug.Log($"  AvatarLOD.Level: {avatarLod.Level}");
                Debug.Log($"  AvatarLOD.overrideLOD: {avatarLod.overrideLOD}");
                Debug.Log($"  AvatarLOD.overrideLevel: {avatarLod.overrideLevel}");
                Debug.Log($"  AvatarLOD.wantedLevel: {avatarLod.wantedLevel}");
            }
            else
            {
                Debug.LogWarning($"  AvatarLOD: NULL");
            }
            
            // AvatarEntity (from Multiplayer Blocks) 정보
            var avatarEntity = avatar as AvatarEntity;
            if (avatarEntity != null)
            {
                var behaviour = avatar.GetComponent<IAvatarBehaviour>();
                if (behaviour != null)
                {
                    Debug.Log($"  IAvatarBehaviour.OculusId: {behaviour.OculusId}");
                    Debug.Log($"  IAvatarBehaviour.LocalAvatarIndex: {behaviour.LocalAvatarIndex}");
                    Debug.Log($"  IAvatarBehaviour.HasInputAuthority: {behaviour.HasInputAuthority}");
                }
            }
            
            // Transform 정보
            Debug.Log($"  Position: {avatar.transform.position}");
            Debug.Log($"  Active in Hierarchy: {avatar.gameObject.activeInHierarchy}");
            
            // 자식 렌더러 확인
            var renderers = avatar.GetComponentsInChildren<Renderer>(true);
            int enabledRenderers = 0;
            foreach (var r in renderers) if (r.enabled) enabledRenderers++;
            Debug.Log($"  Renderers: {enabledRenderers}/{renderers.Length} enabled");
            
            index++;
        }
        
        Debug.Log("========== [AvatarDebug] END ==========");
    }
}

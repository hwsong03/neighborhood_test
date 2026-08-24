using UnityEngine;
using Unity.Netcode;

#if META_AVATAR_SDK_DEFINED
using Oculus.Avatar2;
using Meta.XR.MultiplayerBlocks.Shared;
#endif

/// <summary>
/// Late Joiner 아바타 로딩 문제를 수정합니다.
/// 이 스크립트를 아바타 프리팹에 추가하거나, 
/// 씬에 LateJoinerAvatarFixManager를 추가하세요.
/// 
/// 문제: 늦게 접속한 클라이언트가 기존 플레이어 아바타를 볼 수 없음
/// 원인: OnValueChanged는 값이 변경될 때만 호출되므로, 
///       이미 값이 설정된 상태로 스폰되면 아바타 로딩이 트리거되지 않음
/// </summary>
public class LateJoinerAvatarFix : NetworkBehaviour
{
    [Header("Settings")]
    [Tooltip("아바타 로딩을 재시도할 딜레이 (초)")]
    public float retryDelay = 0.5f;
    
    [Tooltip("최대 재시도 횟수")]
    public int maxRetries = 5;

#if META_AVATAR_SDK_DEFINED
    private AvatarEntity _avatarEntity;
    private IAvatarBehaviour _avatarBehaviour;
    private int _retryCount = 0;
    private bool _isAvatarLoaded = false;

    private void Awake()
    {
        _avatarEntity = GetComponent<AvatarEntity>();
        _avatarBehaviour = GetComponent<IAvatarBehaviour>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Owner가 아닌 경우에만 처리 (Remote 아바타)
        if (!IsOwner)
        {
            Debug.Log($"[LateJoinerFix] Remote avatar spawned, Owner:{OwnerClientId}, checking avatar load...");
            
            // 약간의 딜레이 후 아바타 상태 확인
            Invoke(nameof(CheckAndReloadAvatar), retryDelay);
        }
    }

    private void CheckAndReloadAvatar()
    {
        if (_isAvatarLoaded || _avatarEntity == null)
            return;

        // 아바타가 제대로 로드되었는지 확인
        bool hasValidAvatar = _avatarEntity.HasJoints && _avatarEntity.IsCreated;
        
        if (!hasValidAvatar)
        {
            _retryCount++;
            
            if (_avatarBehaviour != null && _avatarBehaviour.OculusId != 0)
            {
                Debug.Log($"[LateJoinerFix] Reloading avatar for Owner:{OwnerClientId}, OculusId:{_avatarBehaviour.OculusId}, Retry:{_retryCount}");
                
                // 아바타 수동 리로드
                _avatarEntity.ReloadAvatarManually();
            }
            else
            {
                Debug.Log($"[LateJoinerFix] OculusId not yet available for Owner:{OwnerClientId}, Retry:{_retryCount}");
            }
            
            // 아직 재시도 횟수가 남았으면 다시 체크
            if (_retryCount < maxRetries)
            {
                Invoke(nameof(CheckAndReloadAvatar), retryDelay);
            }
            else
            {
                Debug.LogWarning($"[LateJoinerFix] Max retries reached for Owner:{OwnerClientId}");
            }
        }
        else
        {
            _isAvatarLoaded = true;
            Debug.Log($"[LateJoinerFix] Avatar successfully loaded for Owner:{OwnerClientId}");
        }
    }

    private void Update()
    {
        // 아바타가 로드되었는지 계속 확인
        if (!_isAvatarLoaded && _avatarEntity != null && !IsOwner)
        {
            if (_avatarEntity.HasJoints && _avatarEntity.IsCreated)
            {
                _isAvatarLoaded = true;
                CancelInvoke(nameof(CheckAndReloadAvatar));
                Debug.Log($"[LateJoinerFix] Avatar loaded (detected in Update) for Owner:{OwnerClientId}");
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        CancelInvoke(nameof(CheckAndReloadAvatar));
    }
#endif
}

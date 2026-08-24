using UnityEngine;
using Meta.XR.MultiplayerBlocks.Shared;

#if META_AVATAR_SDK_DEFINED
using Oculus.Avatar2;
#endif

/// <summary>
/// 원격 아바타의 LOD 문제를 수정합니다.
/// 이 스크립트를 씬의 빈 GameObject에 추가하세요.
/// </summary>
public class RemoteAvatarLODFix : MonoBehaviour
{
#if META_AVATAR_SDK_DEFINED
    [Header("LOD Settings for Remote Avatars")]
    [Tooltip("원격 아바타에 적용할 LOD 레벨 (0=최고품질, 4=최저품질)")]
    [Range(0, 4)]
    public int remoteLodLevel = 2;
    
    [Tooltip("LOD를 강제로 고정할지 여부")]
    public bool overrideLod = true;
    
    [Tooltip("체크 간격 (초)")]
    public float checkInterval = 1f;
    
    private float lastCheckTime;

    private void Start()
    {
        // AvatarLODManager 기본 설정 조정
        if (AvatarLODManager.Instance != null)
        {
            AvatarLODManager.Instance.MaxActiveAvatarsLod0 = 10;
            AvatarLODManager.Instance.MaxActiveAvatarsLod1 = 10;
            AvatarLODManager.Instance.MaxActiveAvatarsLod2 = 10;
            AvatarLODManager.Instance.MaxActiveAvatarsLod3 = 10;
            AvatarLODManager.Instance.MaxActiveAvatarsLod4 = 10;
            Debug.Log("[RemoteAvatarLODFix] AvatarLODManager 설정 완료");
        }
    }

    private void Update()
    {
        if (Time.time - lastCheckTime < checkInterval)
            return;
            
        lastCheckTime = Time.time;
        FixRemoteAvatarLODs();
    }

    private void FixRemoteAvatarLODs()
    {
        // 모든 AvatarEntity 찾기
        var avatarEntities = FindObjectsOfType<AvatarEntity>();
        
        foreach (var avatar in avatarEntities)
        {
            // 원격 아바타인지 확인 (IsLocal이 false인 경우)
            var ovrEntity = avatar as OvrAvatarEntity;
            if (ovrEntity != null && !ovrEntity.IsLocal)
            {
                var avatarLod = avatar.GetComponent<AvatarLOD>();
                if (avatarLod != null)
                {
                    // LOD가 비활성화된 경우 강제 활성화
                    if (avatarLod.Level < 0 || overrideLod)
                    {
                        avatarLod.overrideLOD = true;
                        avatarLod.overrideLevel = remoteLodLevel;
                        
                        // EntityActive 확인
                        if (!ovrEntity.EntityActive)
                        {
                            Debug.LogWarning($"[RemoteAvatarLODFix] {avatar.gameObject.name} EntityActive가 false입니다.");
                        }
                        
                        Debug.Log($"[RemoteAvatarLODFix] {avatar.gameObject.name} LOD를 {remoteLodLevel}로 설정");
                    }
                }
            }
        }
    }
#endif
}

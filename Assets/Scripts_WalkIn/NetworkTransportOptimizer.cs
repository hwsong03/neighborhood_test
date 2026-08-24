using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

/// <summary>
/// 네트워크 트랜스포트 설정을 최적화합니다.
/// NetworkManager와 같은 GameObject에 추가하거나, 씬 시작 시 실행되도록 하세요.
/// </summary>
public class NetworkTransportOptimizer : MonoBehaviour
{
    [Header("Queue Settings")]
    [Tooltip("수신 큐 크기 (기본 128, 아바타용 512 이상 권장)")]
    public int receiveQueueSize = 512;
    
    [Tooltip("송신 큐 크기")]
    public int sendQueueSize = 512;
    
    [Header("Payload Settings")]
    [Tooltip("최대 페이로드 크기 (아바타 스트리밍용 증가)")]
    public int maxPayloadSize = 6144;

    private void Awake()
    {
        ConfigureTransport();
    }

    private void ConfigureTransport()
    {
        var transport = GetComponent<UnityTransport>();
        if (transport == null)
        {
            transport = FindObjectOfType<UnityTransport>();
        }

        if (transport != null)
        {
            // 네트워크 시작 전에만 설정 가능
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                // Unity Transport 설정
                // 참고: UnityTransport의 일부 설정은 직접 접근이 제한될 수 있음
                Debug.Log($"[NetworkOptimizer] Transport found. Configure in Inspector:");
                Debug.Log($"[NetworkOptimizer] - Max Payload Size: {maxPayloadSize}");
                Debug.Log($"[NetworkOptimizer] - Recommended Receive Queue: {receiveQueueSize}");
            }
            else
            {
                Debug.LogWarning("[NetworkOptimizer] Network already started. Cannot modify transport settings.");
            }
        }
        else
        {
            Debug.LogWarning("[NetworkOptimizer] UnityTransport not found!");
        }
    }

    /// <summary>
    /// Inspector에서 수동으로 적용 버튼용
    /// </summary>
    [ContextMenu("Log Recommended Settings")]
    public void LogRecommendedSettings()
    {
        Debug.Log("=== Recommended Network Settings for Avatar Streaming ===");
        Debug.Log("UnityTransport Settings:");
        Debug.Log("  - Max Payload Size: 6144 (or higher)");
        Debug.Log("  - Receive Queue Size: 512 (increase if 'queue full' errors persist)");
        Debug.Log("  - Send Queue Size: 512");
        Debug.Log("");
        Debug.Log("NetworkManager → UnityTransport 컴포넌트에서 설정하세요!");
    }
}

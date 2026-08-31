using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// ConsoleNoiseFilter
// ─────────────────────────────────────────────────────────────────────────────
// 역할: Play 중 콘솔에 찍히는 정보성(Debug.Log) 메시지를, 실제로 필요하다고 정한
//       카테고리(버튼/트리거 눌림, 최적화 단계 진행, 최적화 소요시간)만 남기고 나머지는
//       전부 막는다. 추가로, 서드파티 SDK가 찍는 것으로 확인되었고 프로젝트 동작에
//       영향이 없다고 확인된 특정 경고(Warning) 메시지도 접두사로 걸러낸다.
//       Debug.LogError/예외, 그리고 화이트리스트/블록리스트에 없는 나머지 경고는
//       이 필터와 무관하게 항상 그대로 통과한다.
// 사용: RuntimeInitializeOnLoadMethod로 Play 시작 시 자동 등록된다 -- 씬에 GameObject를
//       배치하거나 다른 스크립트가 호출할 필요 없음.
// 의존: 없음. UnityEngine.Debug의 기본 ILogHandler를 감싸는 방식이라 다른 로직에 영향 없음.
//
// 왜 필요한가: 서드파티 SDK(Meta Avatar 네이티브 레이어, OVRPlugin, Fusion 등)가 Play를
// 시작할 때마다 수백 건씩 진단성 Info 로그(glTF 메시 로딩, 애니메이션 클립 버전 업그레이드,
// rig 컴파일 시간 등)와, 애니메이션 클립 중복 출력/릭 의존성 그래프 순서 같은 경고를
// 찍어낸다. 그 소스들은 Library/PackageCache 안에 있어 직접 고칠 수 없고 고쳐도 패키지
// 갱신 시 사라지므로, 전역에서 화이트리스트/블록리스트 방식으로 걸러낸다.
// ─────────────────────────────────────────────────────────────────────────────
public static class ConsoleNoiseFilter
{
    // 이 문자열로 "시작하는" 정보성(Log) 메시지만 통과한다. 새로운 "필요한" 로그를
    // 추가하려면 여기에 접두사만 추가하면 됨 (기존 [ClassName] 태그 규칙을 그대로 사용).
    private static readonly string[] AllowedPrefixes =
    {
        "[LocalOptimizationRunner]", // 트리거/키 눌림, 최적화 단계(Stage 1~6), 최종 소요시간
        "[TransferManager] Server pressed", // A/B 키 눌림
        "[CameraController] Panning view", // P 키 눌림
    };

    // 이 문자열로 "시작하는" 경고(Warning)만 막는다. 여기 없는 경고는 (출처가 우리
    // 프로젝트든 SDK든) 항상 그대로 콘솔에 뜬다 -- 진짜 문제일 수도 있는 경고까지
    // 조용히 삼키지 않기 위해, 직접 확인해서 무해하다고 판단한 것만 여기 추가할 것.
    private static readonly string[] BlockedWarningPrefixes =
    {
        "[ovrAvatar2 ", // Meta Avatar SDK 네이티브 레이어: 중복 매니저 인스턴스, 애니메이션 클립/릭 진단 -- 아바타 자체는 정상 동작
        "<color=#73ACE5>[Fusion]</color>", // Photon Fusion 자체 연결/틱레이트 진단(예: TickRate 자동 보정) -- 알아서 처리됨
        "Local Dimming feature is not supported", // 헤드셋 디스플레이 기능 안내, 오류 아님
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        // 이미 우리 핸들러로 감싸져 있으면 중복 설치하지 않음 (도메인 리로드 없이
        // 다시 이 메서드가 불릴 가능성에 대비).
        if (Debug.unityLogger.logHandler is FilteringLogHandler) return;

        Debug.unityLogger.logHandler = new FilteringLogHandler(Debug.unityLogger.logHandler);
    }

    private class FilteringLogHandler : ILogHandler
    {
        private readonly ILogHandler _inner;

        public FilteringLogHandler(ILogHandler inner)
        {
            _inner = inner;
        }

        public void LogFormat(LogType logType, Object context, string format, params object[] args)
        {
            if (logType != LogType.Log && logType != LogType.Warning)
            {
                // Error/Assert -- 항상 통과.
                _inner.LogFormat(logType, context, format, args);
                return;
            }

            string message = ExtractMessage(format, args);

            if (logType == LogType.Warning)
            {
                if (message != null)
                {
                    for (int i = 0; i < BlockedWarningPrefixes.Length; i++)
                    {
                        if (message.StartsWith(BlockedWarningPrefixes[i])) return; // 확인된 무해한 경고, 버린다
                    }
                }
                _inner.LogFormat(logType, context, format, args); // 블록리스트에 없는 경고는 항상 통과
                return;
            }

            // logType == LogType.Log (정보성 메시지)
            if (message != null)
            {
                for (int i = 0; i < AllowedPrefixes.Length; i++)
                {
                    if (message.StartsWith(AllowedPrefixes[i]))
                    {
                        _inner.LogFormat(logType, context, format, args);
                        return;
                    }
                }
            }
            // 화이트리스트에 없는 정보성 로그는 버린다.
        }

        private static string ExtractMessage(string format, object[] args)
        {
            if (args == null || args.Length == 0)
            {
                return format;
            }
            if (args.Length == 1)
            {
                // Debug.Log(obj)/Debug.LogWarning(obj)의 내부 호출 경로 -- Unity가 "{0}" +
                // 단일 인자로 감싸서 넘겨준다. string.Format을 직접 호출하지 않고 그냥
                // 문자열화해서, 메시지 안에 {}가 들어있어도 FormatException이 나지 않게 한다.
                return args[0]?.ToString() ?? string.Empty;
            }
            // Debug.LogFormat(format, args) 같은 실제 포맷 문자열 호출 -- 우리 목록은 전부
            // 단일 문자열 메시지라 어차피 매칭될 일이 없으므로, 굳이 string.Format을
            // 호출해서 FormatException 위험을 감수하지 않는다.
            return format;
        }

        public void LogException(System.Exception exception, Object context)
        {
            // 예외는 항상 통과.
            _inner.LogException(exception, context);
        }
    }
}

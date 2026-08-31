using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// ConsoleNoiseFilter
// ─────────────────────────────────────────────────────────────────────────────
// 역할: Play 중 콘솔에 찍히는 정보성(Debug.Log) 메시지를, 실제로 필요하다고 정한
//       카테고리(버튼/트리거 눌림, 최적화 단계 진행, 최적화 소요시간)만 남기고 나머지는
//       전부 막는다. Debug.LogWarning/Debug.LogError/예외는 이 필터와 무관하게 항상 그대로
//       통과한다.
// 사용: RuntimeInitializeOnLoadMethod로 Play 시작 시 자동 등록된다 -- 씬에 GameObject를
//       배치하거나 다른 스크립트가 호출할 필요 없음.
// 의존: 없음. UnityEngine.Debug의 기본 ILogHandler를 감싸는 방식이라 다른 로직에 영향 없음.
//
// 왜 필요한가: 서드파티 SDK(Meta Avatar 네이티브 레이어, OVRPlugin, Fusion 등)가 Play를
// 시작할 때마다 수백 건씩 진단성 Info 로그(glTF 메시 로딩, 애니메이션 클립 버전 업그레이드,
// rig 컴파일 시간 등)를 찍어낸다. 그 소스들은 Library/PackageCache 안에 있어 직접 고칠 수
// 없고 고쳐도 패키지 갱신 시 사라지므로, 전역에서 화이트리스트 방식으로 걸러낸다.
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
            if (logType != LogType.Log)
            {
                // Warning/Error/Assert -- 화이트리스트와 무관하게 항상 통과.
                _inner.LogFormat(logType, context, format, args);
                return;
            }

            string message;
            if (args == null || args.Length == 0)
            {
                message = format;
            }
            else if (args.Length == 1)
            {
                // Debug.Log(obj)의 내부 호출 경로 -- Unity가 "{0}" + 단일 인자로 감싸서
                // 넘겨준다. string.Format을 직접 호출하지 않고 그냥 문자열화해서, 메시지
                // 안에 {}가 들어있어도 FormatException이 나지 않게 한다.
                message = args[0]?.ToString() ?? string.Empty;
            }
            else
            {
                // Debug.LogFormat(format, args) 같은 실제 포맷 문자열 호출 -- 우리
                // 화이트리스트는 전부 단일 문자열 메시지라 어차피 매칭될 일이 없으므로,
                // 굳이 string.Format을 호출해서 FormatException 위험을 감수하지 않는다.
                message = format;
            }

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

        public void LogException(System.Exception exception, Object context)
        {
            // 예외는 항상 통과.
            _inner.LogException(exception, context);
        }
    }
}

using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace OmniMixer.Audio;

/// <summary>
/// OmniMixer 오디오 엔진의 진단 로깅 시스템.
/// 성능에 민감한 오디오 스레드에서도 안전하게 사용할 수 있도록 설계됨.
/// </summary>
public static class AudioLogger
{
    private static readonly string LogDirectory = @"C:\Users\Public\Documents\OmniMixer";
    private static readonly string LogFilePath = Path.Combine(LogDirectory, "audio_engine.log");
    private static readonly object LogLock = new();
    private static bool _isInitialized;

    /// <summary>최소 로그 레벨. 이 레벨 이상의 로그만 기록됨.</summary>
    public static LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

    /// <summary>디버그 로그를 파일에도 기록할지 여부. false이면 Debug창에만 출력.</summary>
    public static bool WriteDebugToFile { get; set; } = true;

    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    }

    /// <summary>
    /// 로그 시스템 초기화. 첫 로그 기록 전에 호출되어야 함.
    /// </summary>
    public static void Initialize()
    {
        if (_isInitialized) return;

        try
        {
            Directory.CreateDirectory(LogDirectory);

            // 새로운 세션 시작 표시
            var header = $"\n{'='.ToString().PadRight(80, '=')}\n" +
                        $"OmniMixer Audio Engine Log Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\n" +
                        $"{'='.ToString().PadRight(80, '=')}\n";

            lock (LogLock)
            {
                File.AppendAllText(LogFilePath, header);
            }
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioLogger] Failed to initialize: {ex.Message}");
        }
    }

    /// <summary>
    /// 통합 로그 작성 함수. 모든 로그 출력의 유일한 진입점.
    /// </summary>
    /// <param name="level">로그 레벨 (Debug, Info, Warning, Error)</param>
    /// <param name="message">로그 메시지</param>
    /// <param name="ex">관련 예외 (Error 레벨에서 주로 사용)</param>
    public static void WriteLog(LogLevel level, string message, Exception? ex = null)
    {
        // 레벨 필터링
        if (level < MinimumLevel) return;

        // DEBUG 레벨은 파일 기록 설정에 따라 처리
        if (level == LogLevel.Debug && !WriteDebugToFile)
        {
            Debug.WriteLine($"[AudioLogger] DEBUG: {message}");
            return;
        }

        if (!_isInitialized) Initialize();

        var fullMessage = ex != null ? $"{message} - Exception: {ex.Message}" : message;
        var levelStr = level switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "ERROR",
            _ => "UNKNOWN"
        };

        WriteLogInternal(levelStr, fullMessage);
    }

    /// <summary>
    /// 디버그 로그 (상세 정보) - DEBUG 빌드에서만 컴파일됨.
    /// </summary>
    [Conditional("DEBUG")]
    public static void LogDebug(string message)
    {
        WriteLog(LogLevel.Debug, message);
    }

    /// <summary>
    /// 버퍼 상태 로그 (성능에 민감한 경로에서 사용) - DEBUG 빌드에서만 컴파일됨.
    /// </summary>
    [Conditional("DEBUG")]
    public static void BufferState(int channelIndex, int bufferedBytes, int bufferedMs,
        int bytesRead, int bytesRequested, string source)
    {
        var message = $"[Ch{channelIndex}] {source}: " +
                     $"Buffered={bufferedBytes}B ({bufferedMs}ms), " +
                     $"Read={bytesRead}B, Requested={bytesRequested}B";
        WriteLog(LogLevel.Debug, message);
    }

    /// <summary>
    /// 캡처 데이터 로그 - DEBUG 빌드에서만 컴파일됨.
    /// </summary>
    [Conditional("DEBUG")]
    public static void Capture(int bytesRecorded, int sampleRate, int channels, string encoding)
    {
        var message = $"Capture: {bytesRecorded}B | {sampleRate}Hz | {channels}ch | {encoding}";
        WriteLog(LogLevel.Debug, message);
    }

    /// <summary>
    /// 언더런 감지 로그 (중요!)
    /// </summary>
    public static void Underrun(int channelIndex, int bytesRequested, int bytesAvailable)
    {
        var message = $"[Ch{channelIndex}] UNDERRUN DETECTED! " +
                     $"Requested={bytesRequested}B, Available={bytesAvailable}B";
        WriteLog(LogLevel.Warning, message);
    }

    /// <summary>
    /// 오버런 감지 로그
    /// </summary>
    public static void Overrun(int channelIndex, int bytesToAdd, int bufferFreeSpace)
    {
        var message = $"[Ch{channelIndex}] OVERRUN DETECTED! " +
                     $"ToAdd={bytesToAdd}B, FreeSpace={bufferFreeSpace}B";
        WriteLog(LogLevel.Warning, message);
    }

    /// <summary>
    /// 타이밍 로그 (주기적 통계용) - DEBUG 빌드에서만 컴파일됨.
    /// </summary>
    [Conditional("DEBUG")]
    public static void Timing(string operation, long elapsedMs)
    {
        WriteLog(LogLevel.Debug, $"{operation}: {elapsedMs}ms");
    }

    /// <summary>
    /// 실제 로그 파일 쓰기 (내부 lock 사용)
    /// </summary>
    private static void WriteLogInternal(string level, string message)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var threadId = Environment.CurrentManagedThreadId.ToString("D3");
            var logLine = $"[{timestamp}] [{threadId}] [{level}] {message}\n";

            lock (LogLock)
            {
                File.AppendAllText(LogFilePath, logLine);
            }

            // 디버그 창에도 출력
            Debug.WriteLine($"[AudioLogger] {level}: {message}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioLogger] Failed to write log: {ex.Message}");
        }
    }
}

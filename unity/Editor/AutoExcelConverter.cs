using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

[InitializeOnLoad]
public static class AutoExcelConverter
{
    // 경로 및 파일 상수
    const string EXCEL_FOLDER_RELATIVE_PATH = "../Excels";
    const string JSON_FOLDER_RELATIVE_PATH = "./Data";
    const string TOOLS_FOLDER_RELATIVE_PATH = "../Tools";

    const string WINDOWS_CONVERTER_NAME = "converter-win-x64.exe";
    const string MAC_ARM64_CONVERTER_NAME = "converter-mac-arm64";
    const string MAC_X64_CONVERTER_NAME = "converter-mac-x64";

    const string EXCEL_SEARCH_PATTERN = "*.xlsx";
    const string EXCEL_EXTENSION = ".xlsx";
    const string TEMP_FILE_PREFIX = "~";
    const string HIDDEN_FILE_PREFIX = ".";

    // 타이밍 및 타임아웃 설정
    const double PROCESS_DELAY_SECONDS = 1.0;
    const double DIRECTORY_CHECK_INTERVAL = 2.0;
    const int PROCESS_TIMEOUT_MS = 10000;

    // 캐싱된 절대 경로
    static readonly string _projectRootDirectory;
    static readonly string _excelDirectory;
    static readonly string _jsonDirectory;
    static readonly string _converterPath;

    static FileSystemWatcher _watcher;
    static readonly ConcurrentDictionary<string, byte> _pendingFiles = new();

    static volatile bool _hasNewFileEvent;
    static double _scheduledProcessTime;

    static bool _isManuallyStopped;
    static double _lastDirectoryCheckTime;

    // 마지막으로 실패한 파일 경로 기록 (터미널 직접 실행 디버깅용)
    static string _lastFailedExcelPath;

    static AutoExcelConverter()
    {
        string dataPath = Application.dataPath;
        string converterFileName = GetConverterFileName();

        _projectRootDirectory = Path.GetFullPath(Path.Combine(dataPath, ".."));
        _excelDirectory = Path.GetFullPath(Path.Combine(dataPath, EXCEL_FOLDER_RELATIVE_PATH));
        _jsonDirectory = Path.GetFullPath(Path.Combine(dataPath, JSON_FOLDER_RELATIVE_PATH));
        _converterPath = Path.GetFullPath(Path.Combine(dataPath, TOOLS_FOLDER_RELATIVE_PATH, converterFileName));

        EditorApplication.update += OnEditorUpdate;
        AssemblyReloadEvents.beforeAssemblyReload += DisposeWatcher;
        EditorApplication.quitting += DisposeWatcher;

        StartWatching();
    }

    static string GetConverterFileName()
    {
        if (Application.platform == RuntimePlatform.OSXEditor)
        {
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => MAC_ARM64_CONVERTER_NAME,
                Architecture.X64 => MAC_X64_CONVERTER_NAME,
                _ => throw new PlatformNotSupportedException(
                    $"지원하지 않는 macOS 아키텍처입니다: {RuntimeInformation.OSArchitecture}")
            };
        }

        if (Application.platform == RuntimePlatform.WindowsEditor)
            return WINDOWS_CONVERTER_NAME;

        throw new PlatformNotSupportedException($"지원하지 않는 Unity Editor 플랫폼입니다: {Application.platform}");
    }

    [MenuItem("Tools/Excel Watcher/Start Watching")]
    public static void StartWatching()
    {
        _isManuallyStopped = false;

        if (_watcher != null)
            return;

        if (!Directory.Exists(_excelDirectory))
            return;

        if (!Directory.Exists(_jsonDirectory))
            Directory.CreateDirectory(_jsonDirectory);

        _watcher = new FileSystemWatcher(_excelDirectory, EXCEL_SEARCH_PATTERN)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size,
            IncludeSubdirectories = true
        };

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Renamed += OnFileRenamed;
        _watcher.Error += OnWatcherError;
        _watcher.EnableRaisingEvents = true;

        Debug.Log($"[ExcelWatcher] 👀 엑셀 파일 감시 시작\n- 감시 경로: {_excelDirectory}\n- 출력 경로: {_jsonDirectory}\n- 변환기 경로: {_converterPath}");
    }

    [MenuItem("Tools/Excel Watcher/Stop Watching")]
    public static void StopWatching()
    {
        _isManuallyStopped = true;

        if (_watcher == null)
            return;

        DisposeWatcher();
        Debug.Log("[ExcelWatcher] 🛑 엑셀 파일 감시 중지됨");
    }

    [MenuItem("Tools/Excel Watcher/Convert All Now")]
    public static void ConvertAllNow()
    {
        if (!Directory.Exists(_excelDirectory))
        {
            Debug.LogWarning($"[ExcelWatcher] ⚠️ 엑셀 폴더가 존재하지 않습니다: {_excelDirectory}");

            return;
        }

        string[] files = Directory.GetFiles(_excelDirectory, EXCEL_SEARCH_PATTERN, SearchOption.AllDirectories);
        int successCount = 0;

        foreach (string file in files)
        {
            if (!IsValidExcelFile(file))
                continue;

            if (RunConverter(file))
                successCount++;
        }

        if (successCount > 0)
            AssetDatabase.Refresh();
    }

    [MenuItem("Tools/Excel Watcher/Debug Last Failed in Terminal")]
    public static void DebugLastFailedInTerminal()
    {
        string targetExcel = _lastFailedExcelPath;

        if (string.IsNullOrEmpty(targetExcel) || !File.Exists(targetExcel))
        {
            if (Directory.Exists(_excelDirectory))
            {
                string[] files = Directory.GetFiles(_excelDirectory, EXCEL_SEARCH_PATTERN, SearchOption.AllDirectories);
                foreach (string f in files)
                {
                    if (IsValidExcelFile(f))
                    {
                        targetExcel = f;
                        break;
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(targetExcel))
        {
            Debug.LogWarning("[ExcelWatcher] ⚠️ 테스트할 엑셀 파일을 찾을 수 없습니다.");

            return;
        }

        OpenRealTerminalWithCommand(targetExcel);
    }

    static void DisposeWatcher()
    {
        if (_watcher == null)
            return;

        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnFileChanged;
        _watcher.Created -= OnFileChanged;
        _watcher.Renamed -= OnFileRenamed;
        _watcher.Error -= OnWatcherError;
        _watcher.Dispose();
        _watcher = null;
    }

    static bool IsValidExcelFile(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        string fileName = Path.GetFileName(path);

        if (fileName.StartsWith(TEMP_FILE_PREFIX, StringComparison.Ordinal) ||
            fileName.StartsWith(HIDDEN_FILE_PREFIX, StringComparison.Ordinal))
            return false;

        return fileName.EndsWith(EXCEL_EXTENSION, StringComparison.OrdinalIgnoreCase);
    }

    static void RegisterFileEvent(string path)
    {
        if (!IsValidExcelFile(path))
            return;

        _pendingFiles[path] = 1;
        _hasNewFileEvent = true;
    }

    static void OnFileChanged(object source, FileSystemEventArgs e) => RegisterFileEvent(e.FullPath);
    static void OnFileRenamed(object source, RenamedEventArgs e) => RegisterFileEvent(e.FullPath);
    static void OnWatcherError(object source, ErrorEventArgs e)
    {
        Debug.LogError($"[ExcelWatcher] ❌ 파일 감시자 내부 오류 발생: {e.GetException()?.Message}");
    }

    static void OnEditorUpdate()
    {
        if (_watcher == null && !_isManuallyStopped)
        {
            if (EditorApplication.timeSinceStartup - _lastDirectoryCheckTime > DIRECTORY_CHECK_INTERVAL)
            {
                _lastDirectoryCheckTime = EditorApplication.timeSinceStartup;
                if (Directory.Exists(_excelDirectory))
                    StartWatching();
            }

            return;
        }

        if (_hasNewFileEvent)
        {
            _hasNewFileEvent = false;
            _scheduledProcessTime = EditorApplication.timeSinceStartup + PROCESS_DELAY_SECONDS;
        }

        if (_scheduledProcessTime > 0 && EditorApplication.timeSinceStartup >= _scheduledProcessTime)
        {
            _scheduledProcessTime = 0;
            ProcessPendingFiles();
        }
    }

    static void ProcessPendingFiles()
    {
        if (_pendingFiles.IsEmpty)
            return;

        string[] filesToProcess = new string[_pendingFiles.Count];
        _pendingFiles.Keys.CopyTo(filesToProcess, 0);
        _pendingFiles.Clear();

        bool hasAnySucceeded = false;

        foreach (string file in filesToProcess)
        {
            if (RunConverter(file))
                hasAnySucceeded = true;
        }

        if (hasAnySucceeded)
            AssetDatabase.Refresh();
    }

    static bool RunConverter(string excelFilePath)
    {
        string fileName = Path.GetFileName(excelFilePath);
        string arguments = $"\"{excelFilePath}\" \"{_jsonDirectory}\"";
        string workingDir = Path.GetDirectoryName(_converterPath) ?? _projectRootDirectory;
        string executionDetails = BuildExecutionDetails(workingDir, _converterPath, arguments);

        if (!File.Exists(_converterPath))
        {
            _lastFailedExcelPath = excelFilePath;
            Debug.LogError($"[ExcelWatcher] ❌ 변환 실패 (변환기 파일 없음): {fileName}\n{executionDetails}");

            return false;
        }

        if (!File.Exists(excelFilePath))
        {
            Debug.LogWarning($"[ExcelWatcher] ⚠️ 변환 건너뜀 (대상 엑셀 파일 없음): {fileName}\n{executionDetails}");

            return false;
        }

        FileLockStatus lockStatus = CheckFileLockStatus(excelFilePath);
        if (lockStatus == FileLockStatus.ExclusivelyLocked)
        {
            _lastFailedExcelPath = excelFilePath;
            Debug.LogError($"[ExcelWatcher] ❌ 변환 실패 (파일 독점 잠김): '{fileName}' 파일이 저장 중이거나 다른 프로그램에 의해 완전히 잠겨 있습니다.\n{executionDetails}");

            return false;
        }

        if (!Directory.Exists(_jsonDirectory))
            Directory.CreateDirectory(_jsonDirectory);

        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = _converterPath,
                Arguments = arguments,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            // 파이프 버퍼링으로 인해 에러 로그가 증발하는 현상 및 인코딩 크래시 방지
            startInfo.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
            startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            startInfo.EnvironmentVariables["DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION"] = "false";

            Stopwatch stopwatch = Stopwatch.StartNew();

            using Process process = new Process { StartInfo = startInfo };
            process.Start();

            // 이벤트 유실 버그가 없는 비동기 Task 방식으로 전체 스트림 끝(EOF)까지 캡처
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(PROCESS_TIMEOUT_MS))
            {
                process.Kill();
                _lastFailedExcelPath = excelFilePath;
                Debug.LogError($"[ExcelWatcher] ❌ 변환 실패 (시간 초과 - {PROCESS_TIMEOUT_MS / 1000}초): {fileName}\n{executionDetails}");

                return false;
            }

            Task.WaitAll(new Task[] { outputTask, errorTask }, 2000);
            stopwatch.Stop();

            string output = outputTask.IsCompleted ? outputTask.Result.Trim() : string.Empty;
            string error = errorTask.IsCompleted ? errorTask.Result.Trim() : string.Empty;

            if (process.ExitCode == 0)
            {
                StringBuilder successSb = new();
                successSb.AppendLine($"[ExcelWatcher] ✅ 변환 성공: {fileName} (ExitCode: 0, 소요시간: {stopwatch.ElapsedMilliseconds}ms)");
                successSb.AppendLine(executionDetails);

                if (!string.IsNullOrEmpty(output))
                    successSb.AppendLine($"[StandardOutput]\n{output}");

                if (!string.IsNullOrEmpty(error))
                    successSb.AppendLine($"[StandardError (경고)]\n{error}");

                Debug.Log(successSb.ToString().TrimEnd());

                return true;
            }

            _lastFailedExcelPath = excelFilePath;
            LogConverterFailure(fileName, process.ExitCode, stopwatch.ElapsedMilliseconds, executionDetails, lockStatus, output, error);

            return false;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _lastFailedExcelPath = excelFilePath;
            Debug.LogError(
                $"[ExcelWatcher] ❌ 변환 실패 (OS 실행 거부 - Win32 ErrorCode: {ex.NativeErrorCode}): {fileName}\n" +
                $"사유: {ex.Message}\n" +
                $"💡 팁: macOS라면 터미널에서 chmod +x \"{_converterPath}\" 를 실행하세요.\n" +
                $"{executionDetails}"
            );

            return false;
        }
        catch (Exception ex)
        {
            _lastFailedExcelPath = excelFilePath;
            Debug.LogError($"[ExcelWatcher] ❌ 변환 실패 (예외 발생 - {ex.GetType().Name}): {fileName}\n{ex.Message}\n{executionDetails}\n{ex.StackTrace}");

            return false;
        }
    }

    static string BuildExecutionDetails(string workingDir, string exePath, string arguments)
    {
        string terminalCommand = Application.platform == RuntimePlatform.OSXEditor
            ? $"cd \"{workingDir}\" && \"{exePath}\" {arguments}"
            : $"cd /d \"{workingDir}\" && \"{exePath}\" {arguments}";

        StringBuilder sb = new();
        sb.AppendLine("[실행 정보]");
        sb.AppendLine($"  • 작업 폴더 (WorkingDir) : {workingDir}");
        sb.AppendLine($"  • 실행 파일 (FileName)   : \"{exePath}\"");
        sb.AppendLine($"  • 전달 인자 (Arguments)  : {arguments}");
        sb.Append($"  • 터미널 복붙용 명령어    : {terminalCommand}");

        return sb.ToString();
    }

    static void LogConverterFailure(
        string fileName,
        int exitCode,
        long elapsedMs,
        string executionDetails,
        FileLockStatus lockStatus,
        string output,
        string error)
    {
        string hexExitCode = $"0x{exitCode:X8}";
        StringBuilder sb = new();

        sb.AppendLine($"[ExcelWatcher] ❌ 변환 실패: {fileName} (ExitCode: {exitCode} / {hexExitCode}, 소요시간: {elapsedMs}ms)");
        sb.AppendLine(executionDetails);

        // 1. OS 레벨 크래시 코드 분석
        string crashReason = AnalyzeExitCode(exitCode);
        if (!string.IsNullOrEmpty(crashReason))
            sb.AppendLine($"🚨 [ExitCode 분석]: {crashReason}");

        // 2. 엑셀 파일 열림 상태 분석
        if (lockStatus == FileLockStatus.SharedWriteLocked)
            sb.AppendLine("⚠️ [파일 잠금 감지]: 현재 이 파일이 엑셀(Excel)에 열려 있습니다. 변환기가 열려 있는 파일 읽기를 지원하지 않으면 PermissionError 등으로 즉시 종료됩니다.");

        // 3. 표준 에러 및 표준 출력 표시
        if (!string.IsNullOrEmpty(error))
            sb.AppendLine($"[StandardError]\n{error}");

        if (!string.IsNullOrEmpty(output))
            sb.AppendLine($"[StandardOutput]\n{output}");

        // 4. 여전히 출력이 비어있는 경우의 안내
        if (string.IsNullOrEmpty(error) && string.IsNullOrEmpty(output))
        {
            sb.AppendLine("💡 [출력 스트림 비어있음]: 상단 메뉴 [Tools > Excel Watcher > Debug Last Failed in Terminal]을 클릭하면 실제 터미널 창이 열려 숨겨진 에러를 바로 확인할 수 있습니다.");
        }

        Debug.LogError(sb.ToString().TrimEnd());
    }

    static string AnalyzeExitCode(int exitCode)
    {
        return unchecked((uint)exitCode) switch
        {
            0xC0000135 => "필수 DLL 누락 (0xC0000135: STATUS_DLL_NOT_FOUND). 변환기 실행에 필요한 런타임(.NET, VC++ 재배포 패키지 등)이나 주변 DLL 파일이 없습니다.",
            0xC0000005 => "메모리 접근 위반 (0xC0000005: STATUS_ACCESS_VIOLATION). 변환기 내부에서 Null 포인터 참조나 메모리 크래시가 발생했습니다.",
            0xE0434352 => ".NET 미처리 예외 (0xE0434352: CLR Exception). C# 기반 변환기 내부에서 try-catch로 잡히지 않은 예외가 발생했습니다.",
            0xC000007B => "아키텍처 불일치 (0xC000007B: STATUS_INVALID_IMAGE_FORMAT). 32비트/64비트 DLL 충돌이 발생했습니다.",
            126 => "실행 권한 거부 (ExitCode 126). macOS에서 실행 권한(chmod +x)이 없습니다.",
            127 => "명령어/라이브러리 찾을 수 없음 (ExitCode 127).",
            139 => "세그멘테이션 오류 (ExitCode 139: SIGSEGV).",
            _ => null
        };
    }

    static void OpenRealTerminalWithCommand(string excelFilePath)
    {
        string workingDir = Path.GetDirectoryName(_converterPath) ?? _projectRootDirectory;
        string arguments = $"\"{excelFilePath}\" \"{_jsonDirectory}\"";

        if (Application.platform == RuntimePlatform.OSXEditor)
        {
            // macOS: Terminal.app을 열어 명령어를 실행하고 창을 유지
            string script = $"tell application \\\"Terminal\\\" to do script \\\"cd '{workingDir}' && '{_converterPath}' '{excelFilePath}' '{_jsonDirectory}'\\\"";
            Process.Start(new ProcessStartInfo
            {
                FileName = "osascript",
                Arguments = $"-e \"tell application \\\"Terminal\\\" to activate\" -e \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        else
        {
            // Windows: cmd.exe 창을 실제로 띄워서 실행하고 창이 닫히지 않게(/k) 유지
            string cmdArgs = $"/k \"cd /d \"{workingDir}\" && \"{_converterPath}\" {arguments} & echo. & echo [종료 코드: %ERRORLEVEL%]\"";
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = cmdArgs,
                UseShellExecute = true,
                CreateNoWindow = false
            });
        }
    }

    enum FileLockStatus
    {
        Unlocked,
        SharedWriteLocked,
        ExclusivelyLocked
    }

    static FileLockStatus CheckFileLockStatus(string filePath)
    {
        try
        {
            using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            return FileLockStatus.Unlocked;
        }
        catch (IOException)
        {
        }

        try
        {
            using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            return FileLockStatus.SharedWriteLocked;
        }
        catch (IOException)
        {
            return FileLockStatus.ExclusivelyLocked;
        }
    }
}
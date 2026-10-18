using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
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

    const string WINDOWS_CONVERTER_NAME = "converter-win.exe";
    const string MAC_CONVERTER_NAME = "converter-macos";

    const string EXCEL_SEARCH_PATTERN = "*.xlsx";
    const string EXCEL_EXTENSION = ".xlsx";
    const string TEMP_FILE_PREFIX = "~";
    const string HIDDEN_FILE_PREFIX = ".";

    // 타이밍 및 타임아웃 설정
    const double PROCESS_DELAY_SECONDS = 1.0;
    const double DIRECTORY_CHECK_INTERVAL = 2.0;
    const int PROCESS_TIMEOUT_MS = 10000; // 변환기 무한 대기(에디터 멈춤) 방지용 10초 제한

    // 캐싱된 절대 경로 (메인 스레드 초기화 시 1회 연산)
    static readonly string _excelDirectory;
    static readonly string _jsonDirectory;
    static readonly string _converterPath;

    static FileSystemWatcher _watcher;
    static readonly ConcurrentDictionary<string, byte> _pendingFiles = new();

    // 스레드 간 안전한 이벤트 감지를 위한 플래그 및 타이머
    static volatile bool _hasNewFileEvent;
    static double _scheduledProcessTime;

    static bool _isManuallyStopped;
    static double _lastDirectoryCheckTime;

    static AutoExcelConverter()
    {
        string dataPath = Application.dataPath;
        string converterFileName = Application.platform == RuntimePlatform.OSXEditor
            ? MAC_CONVERTER_NAME
            : WINDOWS_CONVERTER_NAME;

        _excelDirectory = Path.GetFullPath(Path.Combine(dataPath, EXCEL_FOLDER_RELATIVE_PATH));
        _jsonDirectory = Path.GetFullPath(Path.Combine(dataPath, JSON_FOLDER_RELATIVE_PATH));
        _converterPath = Path.GetFullPath(Path.Combine(dataPath, TOOLS_FOLDER_RELATIVE_PATH, converterFileName));

        EditorApplication.update += OnEditorUpdate;

        // 스크립트 리컴파일 및 에디터 종료 시 백그라운드 Watcher 리소스 누수 방지
        AssemblyReloadEvents.beforeAssemblyReload += DisposeWatcher;
        EditorApplication.quitting += DisposeWatcher;

        StartWatching();
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

        Debug.Log($"[ExcelWatcher] 👀 엑셀 파일 감시 시작 (경로: {_excelDirectory})");
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
        // 1. 폴더 미생성 상태일 때 주기적으로 생성 여부 확인
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

        // 2. 새로운 파일 이벤트가 들어왔다면 타이머 연장 (디바운싱)
        if (_hasNewFileEvent)
        {
            _hasNewFileEvent = false;
            _scheduledProcessTime = EditorApplication.timeSinceStartup + PROCESS_DELAY_SECONDS;
        }

        // 3. 예약된 시간이 도래했을 때 일괄 변환 처리
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

        // [원인 1] 변환기 실행 파일 자체가 없는 경우
        if (!File.Exists(_converterPath))
        {
            Debug.LogError($"[ExcelWatcher] ❌ 변환 실패 (변환기 없음): '{_converterPath}' 경로에 실행 파일이 없습니다.");

            return false;
        }

        // [원인 2] 이벤트 발생 후 파일이 삭제되었거나 이동된 경우
        if (!File.Exists(excelFilePath))
        {
            Debug.LogWarning($"[ExcelWatcher] ⚠️ 변환 건너뜀 (파일 없음): '{fileName}' 파일이 삭제되었거나 이동되었습니다.\n경로: {excelFilePath}");

            return false;
        }

        // [원인 3] 파일 잠금(Lock) 상태 사전 점검
        FileLockStatus lockStatus = CheckFileLockStatus(excelFilePath);
        if (lockStatus == FileLockStatus.ExclusivelyLocked)
        {
            Debug.LogError($"[ExcelWatcher] ❌ 변환 실패 (파일 독점 잠김): '{fileName}' 파일이 다른 프로세스에서 저장 중이거나 독점 사용 중입니다. 잠시 후 다시 저장해 주세요.");

            return false;
        }

        if (!Directory.Exists(_jsonDirectory))
            Directory.CreateDirectory(_jsonDirectory);

        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = _converterPath,
                Arguments = $"\"{excelFilePath}\" \"{_jsonDirectory}\"",
                WorkingDirectory = Path.GetDirectoryName(_converterPath) ?? string.Empty,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            StringBuilder outputBuilder = new();
            StringBuilder errorBuilder = new();

            using Process process = new Process { StartInfo = startInfo };

            // 비동기 스트림 수신 (버퍼 포화로 인한 에디터 데드락 방지)
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    outputBuilder.AppendLine(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    errorBuilder.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // [원인 4] 변환 프로세스가 응답 없이 멈춘 경우 (타임아웃)
            if (!process.WaitForExit(PROCESS_TIMEOUT_MS))
            {
                process.Kill();
                Debug.LogError($"[ExcelWatcher] ❌ 변환 실패 (시간 초과): '{fileName}' 변환이 {PROCESS_TIMEOUT_MS / 1000}초 내에 끝나지 않아 강제 종료했습니다.");

                return false;
            }

            // 비동기 이벤트 버퍼가 모두 플러시될 때까지 대기
            process.WaitForExit();

            string output = outputBuilder.ToString().Trim();
            string error = errorBuilder.ToString().Trim();

            if (process.ExitCode == 0)
            {
                string logMsg = string.IsNullOrEmpty(output)
                    ? $"[ExcelWatcher] ✅ 변환 완료: {fileName}"
                    : $"[ExcelWatcher] ✅ 변환 완료: {fileName}\n{output}";

                Debug.Log(logMsg);

                return true;
            }

            // [원인 5] 변환기 실행 중 내부 에러 발생 (ExitCode != 0)
            LogConverterFailure(fileName, process.ExitCode, startInfo.Arguments, lockStatus, output, error);

            return false;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // [원인 6] OS 실행 권한 부족 (특히 macOS에서 chmod +x 누락 시) 또는 아키텍처 불일치
            Debug.LogError(
                $"[ExcelWatcher] ❌ 변환기 실행 거부 (OS/권한 오류): {fileName}\n" +
                $"변환기 경로: {_converterPath}\n" +
                $"상세 내용: {ex.Message} (Win32 ErrorCode: {ex.NativeErrorCode})\n" +
                $"💡 팁: macOS 환경이라면 터미널에서 'chmod +x \"{_converterPath}\"' 명령어로 실행 권한을 부여했는지 확인하세요."
            );

            return false;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ExcelWatcher] ❌ 예외 발생 ({ex.GetType().Name}): {fileName}\n{ex.Message}\n{ex.StackTrace}");

            return false;
        }
    }

    static void LogConverterFailure(string fileName, int exitCode, string arguments, FileLockStatus lockStatus, string output, string error)
    {
        StringBuilder sb = new();
        sb.AppendLine($"[ExcelWatcher] ❌ 변환 실패: {fileName} (ExitCode: {exitCode})");

        if (lockStatus == FileLockStatus.SharedWriteLocked)
        {
            sb.AppendLine("⚠️ [유력 원인 - 엑셀 열림 감지]: 현재 이 파일이 엑셀(Excel) 프로그램에 열려 있습니다! 변환기가 열려 있는 파일 읽기를 지원하지 않는 경우 엑셀을 닫고 다시 시도해 보세요.");
        }

        if (!string.IsNullOrEmpty(error))
            sb.AppendLine($"[StandardError]\n{error}");

        if (!string.IsNullOrEmpty(output))
            sb.AppendLine($"[StandardOutput]\n{output}");

        if (string.IsNullOrEmpty(error) && string.IsNullOrEmpty(output))
            sb.AppendLine("💡 변환기가 별도의 에러 메시지를 출력하지 않고 종료되었습니다. 엑셀 시트 규격(컬럼명, 데이터 타입, 빈 행 등)을 확인하세요.");

        sb.AppendLine($"[실행 인자]: {arguments}");

        Debug.LogError(sb.ToString());
    }

    enum FileLockStatus
    {
        Unlocked,           // 아무도 점유하지 않음
        SharedWriteLocked,  // 엑셀 등에서 열려 있으나 공유 읽기는 가능한 상태
        ExclusivelyLocked   // 저장 중이거나 완전히 잠겨 접근 불가능한 상태
    }

    static FileLockStatus CheckFileLockStatus(string filePath)
    {
        // 1. 일반 읽기 전용(FileShare.Read)으로 열어보기
        try
        {
            using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            return FileLockStatus.Unlocked;
        }
        catch (IOException)
        {
            // 실패했다면 다른 프로세스(엑셀 등)가 쓰기 모드로 열어둔 상태
        }

        // 2. 공유 읽기/쓰기(FileShare.ReadWrite)로 열어보기
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
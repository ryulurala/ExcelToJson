using UnityEngine;
using UnityEditor;
using System.IO;
using System.Diagnostics;
using System.Collections.Concurrent;
using System;
using System.Linq;

[InitializeOnLoad]
public static class AutoExcelConverter
{
    const string ExcelFolderRelativePath = "../Excels";
    const string JsonFolderRelativePath = "../Excels";
    const string ToolsFolderRelativePath = "../Tools";

    const string WindowsConverterName = "excel-converter-win.exe";
    const string MacConverterName = "excel-converter-macos";

    const string ExcelSearchPattern = "*.xlsx";
    const string ExcelExtension = ".xlsx";
    const string TempFilePrefix = "~";

    static FileSystemWatcher watcher;

    static ConcurrentDictionary<string, byte> pendingFiles = new ConcurrentDictionary<string, byte>();

    static bool isWaitingToProcess = false;
    static double processTime = 0;
    static readonly double delaySeconds = 1.0;

    // 폴더 생성 감지용 변수
    static bool isManuallyStopped = false;
    static double lastDirectoryCheckTime = 0;
    static readonly double directoryCheckInterval = 2.0;

    // =========================================================
    // ⚙️ 경로 설정 (상수를 기반으로 절대 경로 생성)
    // =========================================================
    static string ExcelDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, ExcelFolderRelativePath));
    static string JsonDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, JsonFolderRelativePath));

    static string ConverterPath
    {
        get
        {
            string fileName = WindowsConverterName;
            if (Application.platform == RuntimePlatform.OSXEditor)      // macOS
                fileName = MacConverterName;

            return Path.GetFullPath(Path.Combine(Application.dataPath, ToolsFolderRelativePath, fileName));
        }
    }

    static AutoExcelConverter()
    {
        EditorApplication.update += OnEditorUpdate;
        StartWatching();
    }

    [MenuItem("Tools/Excel Watcher/Start Watching")]
    public static void StartWatching()
    {
        isManuallyStopped = false;

        if (watcher != null)
            return;

        if (!Directory.Exists(ExcelDirectory))
            return;

        if (!Directory.Exists(JsonDirectory))
            Directory.CreateDirectory(JsonDirectory);

        watcher = new FileSystemWatcher(ExcelDirectory, ExcelSearchPattern);
        watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size;
        watcher.Changed += OnFileChanged;
        watcher.Created += OnFileChanged;
        watcher.Renamed += OnFileRenamed;
        watcher.IncludeSubdirectories = true;
        watcher.EnableRaisingEvents = true;

        UnityEngine.Debug.Log($"[ExcelWatcher] 👀 엑셀 파일 감시 시작 (경로: {ExcelDirectory})");
    }

    [MenuItem("Tools/Excel Watcher/Stop Watching")]
    public static void StopWatching()
    {
        isManuallyStopped = true;

        if (watcher != null)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            watcher = null;
            UnityEngine.Debug.Log("[ExcelWatcher] 🛑 엑셀 파일 감시 중지됨");
        }
    }

    static void RegisterFileEvent(string path)
    {
        string fileName = Path.GetFileName(path);
        // 상수화된 임시 파일 접두사(~)와 확장자(.xlsx) 사용
        if (fileName.StartsWith(TempFilePrefix) || !fileName.EndsWith(ExcelExtension)) return;

        pendingFiles[path] = 1;
        isWaitingToProcess = true;
    }

    static void OnFileChanged(object source, FileSystemEventArgs e) => RegisterFileEvent(e.FullPath);
    static void OnFileRenamed(object source, RenamedEventArgs e) => RegisterFileEvent(e.FullPath);

    static void OnEditorUpdate()
    {
        if (watcher == null && !isManuallyStopped)
        {
            if (EditorApplication.timeSinceStartup - lastDirectoryCheckTime > directoryCheckInterval)
            {
                lastDirectoryCheckTime = EditorApplication.timeSinceStartup;
                if (Directory.Exists(ExcelDirectory))
                {
                    StartWatching();
                }
            }
            return;
        }

        if (isWaitingToProcess)
        {
            if (processTime == 0)
            {
                processTime = EditorApplication.timeSinceStartup + delaySeconds;
            }
            else if (EditorApplication.timeSinceStartup >= processTime)
            {
                isWaitingToProcess = false;
                processTime = 0;

                var filesToProcess = pendingFiles.Keys.ToList();
                pendingFiles.Clear();

                bool hasProcessed = false;
                foreach (var file in filesToProcess)
                {
                    RunConverter(file);
                    hasProcessed = true;
                }

                if (hasProcessed)
                {
                    AssetDatabase.Refresh();
                }
            }
        }
    }

    static void RunConverter(string excelFilePath)
    {
        if (!File.Exists(ConverterPath))
        {
            UnityEngine.Debug.LogError($"[ExcelWatcher] ❌ 변환기를 찾을 수 없습니다: {ConverterPath}");
            return;
        }

        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = ConverterPath,
                Arguments = $"\"{excelFilePath}\" \"{JsonDirectory}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,

                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            using (Process process = Process.Start(startInfo))
            {
                process.WaitForExit();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                if (process.ExitCode == 0)
                {
                    UnityEngine.Debug.Log($"[ExcelWatcher] ✅ 변환 완료: {Path.GetFileName(excelFilePath)}\n{output}");
                }
                else
                {
                    UnityEngine.Debug.LogError($"[ExcelWatcher] ❌ 변환 실패: {Path.GetFileName(excelFilePath)}\n{error}");
                }
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"[ExcelWatcher] ❌ 실행 중 예외 발생: {ex.Message}");
        }
    }
}
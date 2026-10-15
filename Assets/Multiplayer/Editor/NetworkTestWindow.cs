using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Tools > Wizard Duel > Network Test: 테스트 빌드를 만들고 전용 서버와 클라이언트를 띄우거나 강제 종료한다.
public class NetworkTestWindow : EditorWindow
{
    const string NetworkDemoScenePath = "Assets/Scenes/NetworkDemo.unity";
    const string NetworkDemoExePath = "Build/NetworkTest/WizardDuel.exe";
    const string NetworkDemoLogDir = "Logs/NetworkTest";
    const string MainScenePath = "Assets/Scenes/Main.unity";
    const string MainExePath = "Build/LobbyTest/WizardDuel.exe";
    const string MainLogDir = "Logs/LobbyTest";
    const string ServerArgs = "-batchmode -nographics -server";
    const string ClientArgs = "-join 127.0.0.1 -screen-fullscreen 0 -screen-width 1280 -screen-height 720";
    const string RunningKey = "NetworkTestWindow.Running";
    const string ModeKey = "NetworkTestWindow.Mode";

    enum TestMode { Main, NetworkDemo }

    // 플레이 모드 진입(도메인 리로드)으로 비워지면 SessionState에 남긴 "이름:PID"로 다시 찾는다.
    static List<(string name, Process process)> running;
    TestMode mode;
    bool outdated;

    [MenuItem("Tools/Wizard Duel/Network Test")]
    static void Open() => GetWindow<NetworkTestWindow>("Network Test");

    [InitializeOnLoadMethod]
    static void CloseOnQuit() => EditorApplication.quitting += () => Running().ForEach(entry => Kill(entry.process));

    void OnEnable()
    {
        mode = (TestMode)SessionState.GetInt(ModeKey, (int)TestMode.Main);
        OnFocus();
    }

    // 빌드 뒤에 바뀐 에셋이 있으면 다시 빌드하라고 표시한다.
    void OnFocus() => outdated = File.Exists(ExePath) && Directory.EnumerateFiles("Assets", "*", SearchOption.AllDirectories)
        .Any(path => File.GetLastWriteTime(path) > File.GetLastWriteTime(ExePath));

    void OnInspectorUpdate()
    {
        if (Running().RemoveAll(Exited) == 0) return;
        Save();
        Repaint();
    }

    // 창을 닫거나 Kill해서 끝난 프로세스는 핸들을 닫고 목록에서 뺀다.
    static bool Exited((string name, Process process) entry)
    {
        if (!entry.process.HasExited) return false;
        entry.process.Dispose();
        return true;
    }

    void OnGUI()
    {
        GUI.enabled = Running().Count == 0;
        TestMode selectedMode = (TestMode)EditorGUILayout.EnumPopup("Mode", mode);
        if (selectedMode != mode)
        {
            mode = selectedMode;
            SessionState.SetInt(ModeKey, (int)mode);
            OnFocus();
        }
        selectedMode = mode;

        bool built = File.Exists(ExePath);
        bool serverRunning = Running().Exists(entry => entry.name == "server");
        GUILayout.Label(!built ? "No test build yet." : $"Build {File.GetLastWriteTime(ExePath):MM-dd HH:mm}" + (outdated ? " (assets changed since)" : ""));

        // 실행 중에는 exe가 잠겨 있어 다시 빌드할 수 없다.
        GUI.enabled = Running().Count == 0 && !EditorApplication.isPlayingOrWillChangePlaymode;
        if (GUILayout.Button("Build")) EditorApplication.delayCall += () => Build(selectedMode);

        GUILayout.BeginHorizontal();
        GUI.enabled = built && !serverRunning;
        if (GUILayout.Button("Server")) EditorApplication.delayCall += () => Launch(selectedMode, "server", ServerArgs);
        GUI.enabled = built;
        if (GUILayout.Button("Client")) EditorApplication.delayCall += () => LaunchClient(selectedMode);
        GUI.enabled = built && !serverRunning;
        if (GUILayout.Button("1v1")) EditorApplication.delayCall += () => { Launch(selectedMode, "server", ServerArgs); LaunchClient(selectedMode); LaunchClient(selectedMode); };
        GUILayout.EndHorizontal();

        GUI.enabled = true;
        foreach (var (name, process) in Running())
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{name}  (pid {process.Id})");
            if (GUILayout.Button("Kill", GUILayout.Width(60))) Kill(process);
            GUILayout.EndHorizontal();
        }

        GUILayout.BeginHorizontal();
        GUI.enabled = Running().Count > 0;
        if (GUILayout.Button("Close All")) Running().ForEach(entry => Kill(entry.process));
        GUI.enabled = Directory.Exists(LogDir);
        if (GUILayout.Button("Open Logs")) EditorUtility.OpenWithDefaultApp(Path.GetFullPath(LogDir));
        GUILayout.EndHorizontal();
        GUI.enabled = true;
    }

    void Build(TestMode selectedMode)
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string scenePath = selectedMode == TestMode.Main ? MainScenePath : NetworkDemoScenePath;
        string exePath = ExePathFor(selectedMode);
        var report = BuildPipeline.BuildPlayer(new[] { scenePath }, exePath, BuildTarget.StandaloneWindows64, BuildOptions.Development);
        Debug.Log($"[NetworkTest] Build {report.summary.result}: {Path.GetFullPath(exePath)}");
        OnFocus();
    }

    static void LaunchClient(TestMode selectedMode)
    {
        int number = 1;
        while (Running().Exists(entry => entry.name == $"client{number}")) number++;
        Launch(selectedMode, $"client{number}", ClientArgs);
    }

    static void Launch(TestMode selectedMode, string name, string args)
    {
        string logDir = LogDirFor(selectedMode);
        Directory.CreateDirectory(logDir);
        string log = Path.GetFullPath($"{logDir}/{name}.log");
        var startInfo = new ProcessStartInfo(Path.GetFullPath(ExePathFor(selectedMode)), $"{args} -logFile \"{log}\"")
        {
            UseShellExecute = false,
            WindowStyle = name == "server" ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
        };
        Running().Add((name, Process.Start(startInfo)));
        Save();
    }

    string ExePath => ExePathFor(mode);
    string LogDir => LogDirFor(mode);

    static string ExePathFor(TestMode selectedMode) => selectedMode == TestMode.Main ? MainExePath : NetworkDemoExePath;
    static string LogDirFor(TestMode selectedMode) => selectedMode == TestMode.Main ? MainLogDir : NetworkDemoLogDir;

    static void Kill(Process process)
    {
        if (!process.HasExited) process.Kill();
    }

    static List<(string name, Process process)> Running() => running ??= SessionState.GetString(RunningKey, "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(entry => entry.Split(':'))
        .Select(entry => (name: entry[0], process: Find(int.Parse(entry[1]))))
        .Where(entry => entry.process != null).ToList();

    static Process Find(int id)
    {
        try
        {
            Process process = Process.GetProcessById(id);
            return process.ProcessName == "WizardDuel" && !process.HasExited ? process : null;
        }
        catch (SystemException) { return null; } // 이미 끝난 프로세스
    }

    static void Save() => SessionState.SetString(RunningKey, string.Join(",", running.Select(entry => $"{entry.name}:{entry.process.Id}")));
}

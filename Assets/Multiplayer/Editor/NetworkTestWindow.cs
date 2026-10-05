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
    const string ScenePath = "Assets/Multiplayer/NetworkDemo.unity";
    const string ExePath = "Build/NetworkTest/WizardDuel.exe";
    const string LogDir = "Logs/NetworkTest";
    const string ServerArgs = "-batchmode -nographics -server";
    const string ClientArgs = "-join 127.0.0.1 -screen-fullscreen 0 -screen-width 640 -screen-height 480";
    const string RunningKey = "NetworkTestWindow.Running";

    // 플레이 모드 진입(도메인 리로드)으로 비워지면 SessionState에 남긴 "이름:PID"로 다시 찾는다.
    static List<(string name, Process process)> running;
    bool outdated;

    [MenuItem("Tools/Wizard Duel/Network Test")]
    static void Open() => GetWindow<NetworkTestWindow>("Network Test");

    [InitializeOnLoadMethod]
    static void CloseOnQuit() => EditorApplication.quitting += () => Running().ForEach(entry => Kill(entry.process));

    void OnEnable() => OnFocus();

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
        bool built = File.Exists(ExePath);
        bool serverRunning = Running().Exists(entry => entry.name == "server");
        GUILayout.Label(!built ? "No test build yet." : $"Build {File.GetLastWriteTime(ExePath):MM-dd HH:mm}" + (outdated ? " (assets changed since)" : ""));

        // 실행 중에는 exe가 잠겨 있어 다시 빌드할 수 없다.
        GUI.enabled = Running().Count == 0 && !EditorApplication.isPlayingOrWillChangePlaymode;
        if (GUILayout.Button("Build")) EditorApplication.delayCall += Build;

        GUILayout.BeginHorizontal();
        GUI.enabled = built && !serverRunning;
        if (GUILayout.Button("Server")) EditorApplication.delayCall += () => Launch("server", ServerArgs);
        GUI.enabled = built;
        if (GUILayout.Button("Client")) EditorApplication.delayCall += LaunchClient;
        GUI.enabled = built && !serverRunning;
        if (GUILayout.Button("1v1")) EditorApplication.delayCall += () => { Launch("server", ServerArgs); LaunchClient(); LaunchClient(); };
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

    void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var report = BuildPipeline.BuildPlayer(new[] { ScenePath }, ExePath, BuildTarget.StandaloneWindows64, BuildOptions.Development);
        Debug.Log($"[NetworkTest] Build {report.summary.result}: {Path.GetFullPath(ExePath)}");
        OnFocus();
    }

    static void LaunchClient()
    {
        int number = 1;
        while (Running().Exists(entry => entry.name == $"client{number}")) number++;
        Launch($"client{number}", ClientArgs);
    }

    static void Launch(string name, string args)
    {
        Directory.CreateDirectory(LogDir);
        string log = Path.GetFullPath($"{LogDir}/{name}.log");
        Running().Add((name, Process.Start(new ProcessStartInfo(Path.GetFullPath(ExePath), $"{args} -logFile \"{log}\"") { UseShellExecute = false })));
        Save();
    }

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

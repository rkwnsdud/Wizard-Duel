using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Main 씬을 리눅스 전용 서버(Dedicated Server)로 빌드한다.
// Deploy/Server/Dockerfile이 배치 모드로 Build()를 부른다.
public static class LinuxServerBuild
{
    const string ScenePath = "Assets/Scenes/Main.unity";
    const string DefaultOutputDir = "Build/LinuxServer";
    const string ExecutableName = "WizardDuel.x86_64";

    // 배치 모드 진입점: unity-editor -batchmode -executeMethod LinuxServerBuild.Build -buildOutput /out
    // -quit 대신 직접 종료해 빌드 실패를 종료 코드 1로 알린다.
    public static void Build()
    {
        string outputDir = ArgValue("-buildOutput") ?? DefaultOutputDir;
        EditorApplication.Exit(Run(outputDir) ? 0 : 1);
    }

    static bool Run(string outputDir)
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = Path.Combine(outputDir, ExecutableName),
            target = BuildTarget.StandaloneLinux64,
            targetGroup = BuildTargetGroup.Standalone,
            subtarget = (int)StandaloneBuildSubtarget.Server,
            options = BuildOptions.None,
        };
        BuildSummary summary = BuildPipeline.BuildPlayer(options).summary;
        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[LinuxServerBuild] Succeeded path={summary.outputPath} " +
                $"size={summary.totalSize / (1024 * 1024)}MB warnings={summary.totalWarnings}");
            return true;
        }
        Debug.LogError($"[LinuxServerBuild] {summary.result} errors={summary.totalErrors} path={options.locationPathName}");
        return false;
    }

    static string ArgValue(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}

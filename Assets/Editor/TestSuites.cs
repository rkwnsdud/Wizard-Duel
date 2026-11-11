#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

// 테스트를 두 묶음으로 나눠 돌린다.
//  - 가벼운 테스트: 플레이 모드에 들어가지 않는 테스트. 한 번의 호출 안에서 끝나고 에디터 포커스가 필요 없다.
//  - 전체 테스트: 플레이 모드 테스트(PlayMode 카테고리)까지 포함. 도메인 리로드가 여러 번 일어나 느리다.
// 결과는 Logs/TestResults/ 아래 파일로도 남는다(Logs는 git에 올라가지 않는다).
[InitializeOnLoad]
public static class TestSuites
{
    public const string PlayModeCategory = "PlayMode";
    public const string LightResultPath = "Logs/TestResults/light.txt";
    public const string FullResultPath = "Logs/TestResults/full.txt";
    const string RunningKey = "TestSuites.FullRunning";

    static TestSuites()
    {
        // 전체 테스트는 플레이 모드를 드나들며 도메인을 다시 불러온다. 그때마다 콜백을 다시 건다.
        if (SessionState.GetBool(RunningKey, false))
            ScriptableObject.CreateInstance<TestRunnerApi>().RegisterCallbacks(new FullCallbacks());
    }

    [MenuItem("Tools/Tests/가벼운 테스트 실행")]
    static void RunLightMenu() => Debug.Log(RunLight());

    [MenuItem("Tools/Tests/전체 테스트 실행 (플레이 모드 포함)")]
    static void RunFullMenu() => StartFull();

    // 플레이 모드 테스트를 뺀 나머지를 동기 실행하고 요약을 돌려준다.
    public static string RunLight()
    {
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        var collector = new Collector();
        api.RegisterCallbacks(collector);
        try
        {
            var filter = new Filter { testMode = TestMode.EditMode, categoryNames = new[] { "!" + PlayModeCategory } };
            api.Execute(new ExecutionSettings(filter) { runSynchronously = true });
        }
        finally
        {
            api.UnregisterCallbacks(collector);
            Object.DestroyImmediate(api);
        }
        return Write(LightResultPath, collector.Report());
    }

    // 모든 테스트를 비동기로 시작한다. 끝나면 FullResultPath에 요약을 쓴다(진행 중에는 .partial 파일에 쌓인다).
    public static string StartFull()
    {
        SessionState.SetBool(RunningKey, true);
        Write(FullResultPath, "");
        Write(FullResultPath + ".partial", "");
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.RegisterCallbacks(new FullCallbacks());
        api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }));
        return "started: " + FullResultPath;
    }

    static string Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
        return text;
    }

    static bool IsFixture(ITestAdaptor test) => test.IsSuite && test.TypeInfo != null && test.Method == null;

    static string Line(ITestResultAdaptor result)
    {
        if (IsFixture(result.Test)) return $"TIME {result.Duration:F2}s {result.Test.Name}\n";
        if (result.HasChildren || result.TestStatus == TestStatus.Passed) return "";
        if (result.TestStatus != TestStatus.Failed) return "SKIP " + result.FullName + "\n";
        string message = (result.Message ?? "").Trim();
        if (message.Length > 600) message = message.Substring(0, 600) + "...";
        return "FAIL " + result.FullName + "\n  " + message.Replace("\n", "\n  ") + "\n";
    }

    // 실패·건너뜀을 앞에, 오래 걸린 테스트 클래스를 뒤에 둔다.
    static string Summarize(int passed, int failed, int skipped, double seconds, IEnumerable<string> lines)
    {
        var report = new StringBuilder($"passed={passed} failed={failed} skipped={skipped} time={seconds:F1}s\n");
        var all = lines.Where(line => line.Length > 0).ToList();
        foreach (string line in all.Where(line => !line.StartsWith("TIME "))) report.Append(line);
        foreach (string line in all.Where(line => line.StartsWith("TIME "))
            .OrderByDescending(line => double.Parse(line.Substring(5, line.IndexOf('s') - 5))).Take(8))
            report.Append(line);
        return report.ToString();
    }

    sealed class Collector : ICallbacks
    {
        readonly List<string> lines = new List<string>();
        int passed, failed, skipped;
        double seconds;

        public void RunStarted(ITestAdaptor testsToRun) { }
        public void TestStarted(ITestAdaptor test) { }
        public void RunFinished(ITestResultAdaptor result) => seconds = result.Duration;

        public void TestFinished(ITestResultAdaptor result)
        {
            lines.Add(Line(result));
            if (result.HasChildren) return;
            if (result.TestStatus == TestStatus.Passed) passed++;
            else if (result.TestStatus == TestStatus.Failed) failed++;
            else skipped++;
        }

        public string Report() => Summarize(passed, failed, skipped, seconds, lines);
    }

    sealed class FullCallbacks : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) { }
        public void TestStarted(ITestAdaptor test) { }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (SessionState.GetBool(RunningKey, false)) File.AppendAllText(FullResultPath + ".partial", Line(result));
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            if (!SessionState.GetBool(RunningKey, false)) return;
            SessionState.EraseBool(RunningKey);
            string partial = FullResultPath + ".partial";
            string[] blocks = File.Exists(partial) ? SplitEntries(File.ReadAllText(partial)) : new string[0];
            string report = Summarize(result.PassCount, result.FailCount, result.SkipCount, result.Duration, blocks);
            Write(FullResultPath, report);
            File.Delete(partial);
            Debug.Log("전체 테스트 완료\n" + report);
        }

        // 실패 항목은 여러 줄(들여쓴 메시지)이라 항목 단위로 다시 묶는다.
        static string[] SplitEntries(string text)
        {
            var entries = new List<string>();
            foreach (string line in text.Split('\n'))
            {
                if (line.Length == 0) continue;
                if (line.StartsWith("  ") && entries.Count > 0) entries[entries.Count - 1] += line + "\n";
                else entries.Add(line + "\n");
            }
            return entries.ToArray();
        }
    }
}
#endif

#if UNITY_INCLUDE_TESTS
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// 플레이 모드가 필요한 테스트 클래스의 바탕. 테스트마다가 아니라 클래스당 한 번만 플레이 모드에 들어간다.
// 이 클래스를 상속하면 PlayMode 카테고리가 붙어 가벼운 테스트에서는 빠진다(TestSuites 참고).
// 테스트 준비는 [SetUp] 대신 SetUpInPlayMode를 재정의한다([SetUp]은 플레이 모드 진입보다 먼저 불린다).
[Category(TestSuites.PlayModeCategory)]
public abstract class PlayModeTestFixture
{
    const string SceneName = "PlayModeTestScene";

    // 빈 씬에서 돌리고 테스트가 끝날 때마다 비운다. 직접 씬을 불러오는 테스트는 false로 바꾼다.
    protected virtual bool UseEmptyScene => true;

    protected virtual IEnumerator SetUpInPlayMode() { yield break; }

    [UnitySetUp]
    public IEnumerator EnterPlayModeAndSetUp()
    {
        // 클래스의 첫 테스트만 플레이 모드에 들어가고, 나머지는 그대로 이어서 쓴다.
        if (!EditorApplication.isPlaying) yield return new EnterPlayMode();
        // 에디터 창이 포커스를 잃어도 프레임이 흐르게 한다. 플레이 중에만 적용되고 프로젝트 설정은 바뀌지 않는다.
        Application.runInBackground = true;
        if (UseEmptyScene && !SceneManager.GetSceneByName(SceneName).IsValid())
        {
            // 편집 중이던 씬의 오브젝트가 판정에 끼어들지 않게 빈 씬만 남긴다. 플레이 모드를 나가면 원래 씬이 돌아온다.
            Scene scene = SceneManager.CreateScene(SceneName);
            SceneManager.SetActiveScene(scene);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
                if (SceneManager.GetSceneAt(i) != scene) SceneManager.UnloadSceneAsync(SceneManager.GetSceneAt(i));
            float deadline = Time.realtimeSinceStartup + 5f;
            while (SceneManager.sceneCount > 1 && Time.realtimeSinceStartup < deadline) yield return null;
        }
        yield return SetUpInPlayMode();
    }

    [TearDown]
    public void ClearTestScene()
    {
        Scene scene = SceneManager.GetSceneByName(SceneName);
        if (!scene.IsValid()) return;
        foreach (GameObject root in scene.GetRootGameObjects()) Object.DestroyImmediate(root);
    }

    // 테스트 프레임워크 1.6은 OneTime 단계에서 yield한 ExitPlayMode를 수행하지 않는다. 직접 나가고 기다린다.
    [UnityOneTimeTearDown]
    public IEnumerator ExitPlayModeOnce()
    {
        EditorApplication.isPlaying = false;
        while (EditorApplication.isPlaying) yield return null;
    }
}
#endif

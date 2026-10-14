#if UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class LobbyInputTests
{
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    Keyboard keyboard;
    Keyboard previousKeyboard;
    InputSettings originalInputSettings;
    InputSettings testInputSettings;
    StartScreen start;
    LobbyScreen lobby;
    MainFlow flow;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return new EnterPlayMode();
        // 진입 전에 열려 있던 Main의 영구 네트워크 루트가 새 씬에 남지 않게 한다.
        if (NetworkManager.Singleton != null) Object.DestroyImmediate(NetworkManager.Singleton.gameObject);
        // 편집 씬은 건드리지 않는다. ExitPlayMode가 진입 전 씬 상태를 복원한다.
        EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/Main.unity",
            new LoadSceneParameters(LoadSceneMode.Single));
        // LoadSceneInPlayMode는 로딩 중인 씬을 반환한다. 다음 프레임의 씬 통합을 기다린다.
        yield return WaitForFrames(1);
        flow = Object.FindFirstObjectByType<MainFlow>();
        Assert.That(flow, Is.Not.Null, "Main scene must provide its MainFlow.");
        // NetworkManager는 로드 중 루트를 DontDestroyOnLoad로 옮긴다. 전역 검색 대신 씬의 연결을 쓴다.
        LobbyNetwork network = Field<LobbyNetwork>(flow, "network");
        start = Field<StartScreen>(flow, "startScreen");
        lobby = Field<LobbyScreen>(flow, "lobby");
        Assert.That(network, Is.Not.Null, "MainFlow must reference its LobbyNetwork.");
        Assert.That(start, Is.Not.Null);
        Assert.That(lobby, Is.Not.Null);
        flow.enabled = false;
        network.enabled = false;

        // 프로젝트의 InputSettings 에셋을 수정하지 않고 가상 키보드만 포커스와 무관하게 처리한다.
        originalInputSettings = InputSystem.settings;
        testInputSettings = Object.Instantiate(originalInputSettings);
        testInputSettings.hideFlags = HideFlags.HideAndDontSave;
        testInputSettings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
        testInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        testInputSettings.editorInputBehaviorInPlayMode =
            InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = testInputSettings;
        previousKeyboard = Keyboard.current;
        keyboard = InputSystem.AddDevice<Keyboard>();
        keyboard.MakeCurrent();
        yield return ReleaseEscape();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
        if (originalInputSettings != null) InputSystem.settings = originalInputSettings;
        if (testInputSettings != null) Object.DestroyImmediate(testInputSettings);
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator HostLobby_EscapeRequestsLeaveOnce() => CheckLobbyEscape(true);

    [UnityTest]
    public IEnumerator GuestLobby_EscapeRequestsLeaveOnce() => CheckLobbyEscape(false);

    IEnumerator CheckLobbyEscape(bool isHost)
    {
        OpenLobby(isHost);
        int leaves = 0;
        lobby.LeaveRequested += () => leaves++;
        yield return WaitForFrames(1);

        yield return PressEscape();
        Assert.That(leaves, Is.EqualTo(1));
        yield return WaitForFrames(1);
        Assert.That(leaves, Is.EqualTo(1), "Holding Escape must not repeat LeaveRequested.");
        yield return ReleaseEscape();
    }

    [UnityTest]
    public IEnumerator HostSeedEditing_EscapeDoesNotLeave_ThenEscapeAfterEditingLeaves()
    {
        OpenLobby(true);
        MapSettingsPanel panel = Field<MapSettingsPanel>(lobby, "mapSettings");
        TMP_InputField seed = Field<TMP_InputField>(panel, "seedField");
        int leaves = 0;
        lobby.LeaveRequested += () => leaves++;
        EventSystem.current.SetSelectedGameObject(seed.gameObject);
        seed.ActivateInputField();
        // TMP는 LateUpdate에 포커스를 켠다. 다음 Update에서 로비의 typing 상태도 갱신한다.
        yield return WaitForFrames(2);
        Assert.That(seed.isFocused, Is.True);
        seed.text = "9999";

        yield return PressEscape();
        Assert.That(leaves, Is.Zero, "Escape while editing must not also leave the lobby.");
        yield return ReleaseEscape();

        // TMP의 실제 취소는 IMGUI 입력이다. 가상 키 상태로 그 입력까지 만들었다고 가정하지 않는다.
        seed.DeactivateInputField();
        EventSystem.current.SetSelectedGameObject(null);
        yield return WaitForFrames(1);
        Assert.That(panel.IsTyping, Is.False);
        yield return PressEscape();
        Assert.That(leaves, Is.EqualTo(1));
        yield return ReleaseEscape();
    }

    [UnityTest]
    public IEnumerator CreatingRoom_EscapeCancelsOnce_AndAllowsRetry()
    {
        int canceled = 0, createRequests = 0;
        start.JoinCanceled += () => canceled++;
        start.CreateRequested += () => createRequests++;
        start.BeginCreate();
        Assert.That(Field<GameObject>(start, "notice").activeSelf, Is.True);

        yield return PressEscape();
        Assert.That(canceled, Is.EqualTo(1));
        Assert.That(Field<GameObject>(start, "notice").activeSelf, Is.False);
        Assert.That(Field<TMP_InputField>(start, "codeField").readOnly, Is.False);
        yield return WaitForFrames(1);
        Assert.That(canceled, Is.EqualTo(1));
        yield return ReleaseEscape();
        start.CreateMap();
        Assert.That(createRequests, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator JoiningRoom_EscapeCancelsOnce_AndKeepsCodeForRetry()
    {
        int canceled = 0, joins = 0;
        string joinedCode = null;
        start.JoinCanceled += () => canceled++;
        start.JoinRequested += code => { joins++; joinedCode = code; };
        start.OpenJoin();
        TMP_InputField codeField = Field<TMP_InputField>(start, "codeField");
        codeField.SetTextWithoutNotify("123456");
        start.Join();
        Assert.That(joins, Is.EqualTo(1));
        Assert.That(codeField.readOnly, Is.True);

        yield return PressEscape();
        Assert.That(canceled, Is.EqualTo(1));
        Assert.That(Field<GameObject>(start, "joinBox").activeSelf, Is.True);
        Assert.That(Field<TMP_Text>(start, "joinButtonLabel").text, Is.EqualTo("참가"));
        Assert.That(codeField.readOnly, Is.False);
        Assert.That(codeField.text, Is.EqualTo("123456"));
        yield return WaitForFrames(1);
        Assert.That(canceled, Is.EqualTo(1));
        yield return ReleaseEscape();
        start.Join();
        Assert.That(joins, Is.EqualTo(2));
        Assert.That(joinedCode, Is.EqualTo("123456"));
    }

    [UnityTest]
    public IEnumerator RoomClosedDuringEntryTransition_ReturnsToStartWithoutOpeningLobby()
    {
        flow.enabled = true;
        var snapshot = new NetworkLobbySnapshot
        {
            Code = "123456",
            HostClientId = 0,
            Settings = new NetworkLobbySettings(new DuelSettings
            {
                SeedFixed = true,
                Map = new MapGenerationSettings { Seed = 7301 }
            })
        };
        // 서버 이벤트를 흉내 내되, 실제 MainFlow의 화면 전환 코루틴을 끝까지 실행한다.
        InvokeFlow("OnSnapshot", snapshot);
        Assert.That(Field<Coroutine>(flow, "transition"), Is.Not.Null);
        Assert.That(start.gameObject.activeSelf, Is.True, "The entry hide has not finished yet.");
        InvokeFlow("OnClosed", "A player left, so the room was closed.");
        yield return new WaitForSecondsRealtime(1f);

        Assert.That(start.gameObject.activeSelf, Is.True);
        Assert.That(lobby.gameObject.activeSelf, Is.False);
        Assert.That(Field<Coroutine>(flow, "transition"), Is.Null);
        Assert.That(Field<bool>(flow, "wantsLobby"), Is.False);
        Assert.That(Field<bool>(flow, "lobbyVisible"), Is.False);
    }

    void OpenLobby(bool isHost)
    {
        start.gameObject.SetActive(false);
        lobby.gameObject.SetActive(true);
        lobby.Open(isHost, "123456", new DuelSettings
        {
            SeedFixed = true,
            Map = new MapGenerationSettings { Seed = 7301 }
        }, false);
    }

    IEnumerator PressEscape() => QueueEscape(true);

    IEnumerator ReleaseEscape() => QueueEscape(false);

    IEnumerator QueueEscape(bool pressed)
    {
        bool injected = false, dynamicUpdated = false, stateObserved = false, pressObserved = false;
        Action beforeUpdate = () =>
        {
            if (injected || InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            injected = true;
            keyboard.MakeCurrent();
            InputSystem.QueueStateEvent(keyboard, pressed ? new KeyboardState(Key.Escape) : new KeyboardState());
        };
        Action afterUpdate = () =>
        {
            InputUpdateType update = InputState.currentUpdateType;
            if (update == InputUpdateType.Dynamic && injected)
            {
                dynamicUpdated = true;
                stateObserved |= keyboard.escapeKey.isPressed == pressed;
                pressObserved |= keyboard.escapeKey.wasPressedThisFrame;
            }
        };
        InputSystem.onBeforeUpdate += beforeUpdate;
        InputSystem.onAfterUpdate += afterUpdate;
        try
        {
            int targetFrame = Time.frameCount + 2;
            float deadline = Time.realtimeSinceStartup + 2f;
            // EditMode의 yield null만으로는 게임 프레임을 보장하지 않는다. 실제 Update 두 번을 기다린다.
            while ((!dynamicUpdated || Time.frameCount < targetFrame) && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(dynamicUpdated, Is.True, "A runtime DynamicUpdate must process queued input.");
            Assert.That(Time.frameCount, Is.GreaterThanOrEqualTo(targetFrame));
            // 테스트 코루틴은 Editor 버퍼를 볼 수 있으므로 Dynamic 콜백에서 관찰한 상태를 검사한다.
            Assert.That(stateObserved, Is.True, "The Dynamic input buffer must observe the requested key state.");
            if (pressed) Assert.That(pressObserved, Is.True);
        }
        finally
        {
            InputSystem.onBeforeUpdate -= beforeUpdate;
            InputSystem.onAfterUpdate -= afterUpdate;
        }
    }

    static IEnumerator WaitForFrames(int count)
    {
        int targetFrame = Time.frameCount + count;
        float deadline = Time.realtimeSinceStartup + 2f;
        while (Time.frameCount < targetFrame && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.That(Time.frameCount, Is.GreaterThanOrEqualTo(targetFrame), "The runtime must advance before assertions.");
    }

    static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, PrivateInstance).GetValue(owner);

    void InvokeFlow(string method, object value) =>
        typeof(MainFlow).GetMethod(method, PrivateInstance).Invoke(flow, new[] { value });
}
#endif

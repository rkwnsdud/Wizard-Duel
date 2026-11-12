#if UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public class LobbyInputTests : PlayModeTestFixture
{
    protected override bool UseEmptyScene => false; // 테스트마다 Main 씬을 새로 불러온다.
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    Keyboard keyboard;
    Keyboard previousKeyboard;
    InputSettings originalInputSettings;
    HideFlags originalInputSettingsFlags;
    InputSettings testInputSettings;
    StartScreen start;
    LobbyScreen lobby;
    MainFlow flow;
    LobbyNetwork network;
    NetworkManager manager;

    protected override IEnumerator SetUpInPlayMode()
    {
        // 편집 씬이나 앞선 테스트가 남긴 영구 네트워크 루트가 새 씬에 남지 않게 한다.
        if (NetworkManager.Singleton != null) Object.DestroyImmediate(NetworkManager.Singleton.gameObject);
        // 편집 씬은 건드리지 않는다. 클래스가 끝나 플레이 모드를 나가면 진입 전 씬 상태로 돌아온다.
        EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/Main.unity",
            new LoadSceneParameters(LoadSceneMode.Single));
        // LoadSceneInPlayMode는 로딩 중인 씬을 반환한다. 다음 프레임의 씬 통합을 기다린다.
        yield return WaitForFrames(1);
        flow = Object.FindFirstObjectByType<MainFlow>();
        Assert.That(flow, Is.Not.Null, "Main scene must provide its MainFlow.");
        // NetworkManager는 로드 중 루트를 DontDestroyOnLoad로 옮긴다. 전역 검색 대신 씬의 연결을 쓴다.
        network = Field<LobbyNetwork>(flow, "network");
        manager = Field<NetworkManager>(network, "manager");
        start = Field<StartScreen>(flow, "startScreen");
        lobby = Field<LobbyScreen>(flow, "lobby");
        Assert.That(network, Is.Not.Null, "MainFlow must reference its LobbyNetwork.");
        Assert.That(start, Is.Not.Null);
        Assert.That(lobby, Is.Not.Null);
        flow.enabled = false;
        network.enabled = false;

        // 프로젝트의 InputSettings 에셋을 수정하지 않고 가상 키보드만 포커스와 무관하게 처리한다.
        originalInputSettings = InputSystem.settings;
        originalInputSettingsFlags = originalInputSettings.hideFlags;
        // InputManager는 교체하는 기존 HideAndDontSave 설정을 파괴한다. 기본 임시 설정도 복원할 수 있게 보존한다.
        if (originalInputSettingsFlags == HideFlags.HideAndDontSave)
            originalInputSettings.hideFlags = HideFlags.DontSave;
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
        if (manager != null && manager.IsListening) manager.Shutdown();
        float deadline = Time.realtimeSinceStartup + 3f;
        while (manager != null && manager.ShutdownInProgress && Time.realtimeSinceStartup < deadline)
            yield return null;
        bool shutdownComplete = manager == null || !manager.ShutdownInProgress;
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
        if (originalInputSettings != null)
        {
            InputSystem.settings = originalInputSettings;
            originalInputSettings.hideFlags = originalInputSettingsFlags;
        }
        if (testInputSettings != null) Object.DestroyImmediate(testInputSettings);
        Assert.That(shutdownComplete, Is.True, "The test client must finish shutting down.");
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
    public IEnumerator SeedDraftAndRapidMapEdits_KeepCommittedSeedUntilEndEdit_AndIgnoreEarlierAck()
    {
        OpenLobby(true);
        MapSettingsPanel panel = Field<MapSettingsPanel>(lobby, "mapSettings");
        TMP_InputField seed = Field<TMP_InputField>(panel, "seedField");
        Slider density = Field<Slider>(panel, "density");
        var edits = new List<DuelSettings>();
        panel.Changed += changed => edits.Add(changed.Clone());
        yield return Focus(seed);
        seed.text = "222";
        density.value = 19;
        density.value = 20;
        Assert.That(edits.Count, Is.EqualTo(2));
        Assert.That(edits.TrueForAll(edit => edit.Map.Seed == 7301), Is.True);
        Assert.That(seed.text, Is.EqualTo("222"));
        seed.DeactivateInputField();
        EventSystem.current.SetSelectedGameObject(null);
        Assert.That(edits.Count, Is.EqualTo(3));
        Assert.That(edits[2].Map.Seed, Is.EqualTo(222));

        // r2 입력이 남아 있는 동안 r1 승인은 패널의 최신 초안을 덮지 않는다.
        SetField(network, "inLobby", true);
        SetField(network, "requestedSettingsRevision", 2u);
        SetField(network, "settledSettingsRevision", 1u);
        SetField(network, "currentSnapshot", Snapshot(1, 111));
        SetField(flow, "lobbyVisible", true);
        yield return Focus(seed);
        seed.text = "333";
        InvokeFlow("OnSnapshot", Snapshot(1, 111));
        density.value = 21;
        Assert.That(edits[3].Map.Seed, Is.EqualTo(222));
        Assert.That(seed.text, Is.EqualTo("333"));

        SetField(network, "settledSettingsRevision", 2u);
        SetField(network, "currentSnapshot", Snapshot(2, 222));
        InvokeFlow("OnSnapshot", Snapshot(2, 222));
        Assert.That(seed.text, Is.EqualTo("333"));
        seed.DeactivateInputField();
        Assert.That(edits.Count, Is.EqualTo(5));
        Assert.That(edits[4].Map.Seed, Is.EqualTo(333));
        Assert.That(edits[4].Map.ObstacleDensity, Is.EqualTo(0.2f));
    }

    [UnityTest]
    public IEnumerator HostBushDensitySlider_PublishesTheBushDensityInHundredthSteps()
    {
        OpenLobby(true);
        MapSettingsPanel panel = Field<MapSettingsPanel>(lobby, "mapSettings");
        Slider bushDensity = Field<Slider>(panel, "bushDensity");
        TMP_Text value = Field<TMP_Text>(panel, "bushDensityValue");
        var edits = new List<DuelSettings>();
        panel.Changed += changed => edits.Add(changed.Clone());
        yield return WaitForFrames(1);
        Assert.That(bushDensity.wholeNumbers, Is.True);
        Assert.That(bushDensity.minValue, Is.Zero);
        Assert.That(bushDensity.maxValue, Is.EqualTo(DuelSettings.MaxBushDensity * 100f).Within(1e-4f));
        Assert.That(bushDensity.value, Is.EqualTo(5), "The default density 0.05 is five steps of 0.01.");
        Assert.That(value.text, Is.EqualTo("0.05"));

        bushDensity.value = 12;
        Assert.That(edits.Count, Is.EqualTo(1));
        Assert.That(edits[0].Map.BushDensity, Is.EqualTo(0.12f).Within(1e-6f));
        Assert.That(edits[0].Map.ObstacleDensity, Is.EqualTo(0.15f).Within(1e-6f), "The bush slider must not move the obstacle density.");
        Assert.That(value.text, Is.EqualTo("0.12"));
    }

    [UnityTest]
    public IEnumerator InvalidCanceledAndUnchangedSeedEndEdit_DoNotPublish_AndRollDiscardsOldFocusedText()
    {
        OpenLobby(true);
        MapSettingsPanel panel = Field<MapSettingsPanel>(lobby, "mapSettings");
        TMP_InputField seed = Field<TMP_InputField>(panel, "seedField");
        var edits = new List<DuelSettings>();
        panel.Changed += changed => edits.Add(changed.Clone());
        yield return Focus(seed);
        seed.text = "2147483648";
        seed.DeactivateInputField();
        seed.onEndEdit.Invoke(seed.text);
        Assert.That(edits, Is.Empty, "Invalid input and repeated end-edit must not publish.");

        yield return Focus(seed);
        seed.text = "123";
        seed.OnCancel(new BaseEventData(EventSystem.current));
        Assert.That(seed.wasCanceled, Is.True);
        Assert.That(edits, Is.Empty);

        yield return Focus(seed);
        seed.text = "73010";
        seed.text = "7301";
        seed.DeactivateInputField();
        Assert.That(edits, Is.Empty, "Returning to the current seed must not publish.");

        yield return Focus(seed);
        seed.text = "-99";
        panel.RollSeed();
        Assert.That(edits.Count, Is.EqualTo(1));
        int rolledSeed = edits[0].Map.Seed;
        Assert.That(seed.text, Is.EqualTo(rolledSeed.ToString()));
        Assert.That(seed.isFocused, Is.True);
        seed.DeactivateInputField();
        seed.onEndEdit.Invoke(seed.text);
        Assert.That(edits.Count, Is.EqualTo(1), "Blur must not restore the old draft after rolling.");
        Field<Slider>(panel, "density").value = 19;
        Assert.That(edits[1].Map.Seed, Is.EqualTo(rolledSeed));
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
            Settings = new NetworkMapSettings(new MapGenerationSettings { Seed = 7301 })
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

    [UnityTest]
    public IEnumerator StartButton_WorksOnlyForHostWithOpponent_AndLocksWhileStarting()
    {
        Button button = Field<Button>(lobby, "startButton");
        MapSettingsPanel panel = Field<MapSettingsPanel>(lobby, "mapSettings");
        int requests = 0, edits = 0;
        lobby.StartRequested += () => requests++;
        panel.Changed += _ => edits++;
        OpenLobby(true);
        Assert.That(button.interactable, Is.False, "The host cannot start alone.");

        lobby.SetOpponentPresent(true);
        Assert.That(button.interactable, Is.True);
        lobby.RequestStart();
        lobby.SetStarting(true);
        lobby.RequestStart();
        Assert.That(requests, Is.EqualTo(1), "Pressing again while starting must not send another request.");
        Assert.That(button.interactable, Is.False);
        Field<Slider>(panel, "density").value = 19;
        Assert.That(edits, Is.Zero, "Settings are locked once the duel starts.");

        // 시작이 거절되면 다시 누를 수 있고 설정도 바꿀 수 있다.
        lobby.SetStarting(false);
        Assert.That(button.interactable, Is.True);
        Field<Slider>(panel, "density").value = 20;
        Assert.That(edits, Is.EqualTo(1));

        OpenLobby(false);
        lobby.SetOpponentPresent(true);
        Assert.That(button.gameObject.activeSelf, Is.False);
        lobby.RequestStart();
        Assert.That(requests, Is.EqualTo(1), "A guest cannot request the start.");
        yield return WaitForFrames(1);
    }

    [UnityTest]
    public IEnumerator PreparingSnapshotHidesPreview_AndRoomClosedBeforeSpawnReturnsToStart()
    {
        flow.enabled = true;
        TitleBackdrop backdrop = Field<TitleBackdrop>(flow, "backdrop");
        ProceduralMap titleMap = Field<ProceduralMap>(backdrop, "map");
        OpenLobby(true);
        // 실제 흐름처럼 대기실과 함께 모형 마법사 무대도 켜 둔다.
        LobbyStage stage = Field<LobbyStage>(flow, "stage");
        stage.gameObject.SetActive(true);
        SetField(flow, "wantsLobby", true);
        SetField(flow, "lobbyVisible", true);
        NetworkLobbySnapshot preparing = Snapshot(0, 7301);
        preparing.OpponentPresent = true;
        preparing.GuestClientId = manager.LocalClientId + 1;
        preparing.Phase = LobbyPhase.Preparing;
        SetField(network, "inLobby", true);
        SetField(network, "currentSnapshot", preparing);
        Camera view = Field<Camera>(backdrop, "viewCamera");
        CameraFollow follow = view.GetComponent<CameraFollow>();
        List<UnityEngine.Rendering.Universal.ScriptableRendererFeature> occlusion = ActiveOcclusion();
        yield return null;
        // 시작 화면·대기실 카메라는 맵 중심 둘레를 돌기만 해서 중심까지 거리가 그대로다. 내려가면 이 거리가 확 줄어든다.
        float lobbyDistance = Vector3.Distance(view.transform.position, titleMap.transform.position);

        InvokeFlow("OnSnapshot", preparing);
        yield return WaitForFrames(2);
        Assert.That(titleMap.gameObject.activeSelf, Is.False, "The battle map takes the preview's place.");
        Assert.That(Field<Button>(lobby, "startButton").interactable, Is.False);
        yield return new WaitForSecondsRealtime(0.5f);
        Assert.That(lobby.gameObject.activeSelf, Is.False, "The lobby UI leaves while the camera dives to my spawn.");
        Assert.That(stage.gameObject.activeSelf, Is.True, "The mock wizards stay until the local player spawns.");
        Assert.That(Field<bool>(flow, "battleVisible"), Is.False);
        Assert.That(Field<Coroutine>(flow, "transition"), Is.Not.Null);
        Assert.That(Field<object>(backdrop, "blend").ToString(), Is.EqualTo("Enter"));
        Assert.That(follow.Held, Is.True, "The dive keeps the follow camera off the camera until it lands.");
        Assert.That(Vector3.Distance(view.transform.position, titleMap.transform.position), Is.LessThan(lobbyDistance - 5f),
            "The camera is diving toward my spawn.");
        Assert.That(view.orthographic, Is.False);
        foreach (var feature in occlusion) Assert.That(feature.isActive, Is.False, "SSAO pauses while the projections blend.");

        SetField(network, "inLobby", false);
        InvokeFlow("OnClosed", "A player left, so the room was closed.");
        yield return new WaitForSecondsRealtime(1.5f);
        Assert.That(start.gameObject.activeSelf, Is.True);
        Assert.That(lobby.gameObject.activeSelf, Is.False);
        Assert.That(stage.gameObject.activeSelf, Is.False);
        Assert.That(titleMap.gameObject.activeSelf, Is.True);
        Assert.That(Field<Coroutine>(flow, "transition"), Is.Null);
        Assert.That(Field<bool>(flow, "lobbyVisible"), Is.False);
        Assert.That(Field<bool>(flow, "battleVisible"), Is.False);
        // 내려가다 접속이 끊기면 그 자리에서 시작 화면 구도로 빠져나오고, 붙잡았던 것을 모두 되돌린다.
        Assert.That(Field<object>(backdrop, "blend").ToString(), Is.EqualTo("None"));
        Assert.That(Field<bool>(backdrop, "released"), Is.False);
        Assert.That(follow.Held, Is.False);
        Assert.That(follow.enabled, Is.False);
        Assert.That(view.orthographic, Is.False);
        foreach (var feature in occlusion) Assert.That(feature.isActive, Is.True, "Leaving mid-dive turns SSAO back on.");
    }

    // 지금 켜져 있는 SSAO 렌더러 기능. 전투로 내려가는 동안에만 꺼졌다가 다시 켜져야 한다.
    static List<UnityEngine.Rendering.Universal.ScriptableRendererFeature> ActiveOcclusion()
    {
        var features = new List<UnityEngine.Rendering.Universal.ScriptableRendererFeature>();
        if (!(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset pipeline))
            return features;
        foreach (UnityEngine.Rendering.Universal.ScriptableRendererData data in pipeline.rendererDataList)
        {
            if (data == null) continue;
            foreach (UnityEngine.Rendering.Universal.ScriptableRendererFeature feature in data.rendererFeatures)
                if (feature is UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion && feature.isActive) features.Add(feature);
        }
        return features;
    }

    [UnityTest]
    public IEnumerator CreateRoom_CopiesEntireTitleMap_HoldsItUntilApproval_AndReusesItsBlocks()
    {
        TitleBackdrop backdrop = PrepareFastTitleCycle();
        MapGenerationSettings current = backdrop.Current;
        DuelSettings expected = new DuelSettings { Map = current }.Clone();
        ProceduralMap map = Field<ProceduralMap>(backdrop, "map");
        var blocks = new List<IBlock>(map.Blocks);
        MapLayout layout = map.GeneratedLayout;
        InvokeFlow("Create");
        Assert.That(manager.IsListening, Is.True);
        using (var reader = new FastBufferReader(manager.NetworkConfig.ConnectionData, Allocator.Temp))
        {
            reader.ReadNetworkSerializable(out LobbyJoinRequest request);
            Assert.That(request.CreateRoom, Is.True);
            AssertMapEqual(expected.Map, request.Settings.ToSettings());
        }
        Assert.That(Field<DuelSettings>(flow, "settings").Map, Is.Not.SameAs(current));
        current.Seed++;
        Assert.That(Field<DuelSettings>(flow, "settings").Map.Seed, Is.EqualTo(expected.Map.Seed));
        current.Seed--;

        yield return WaitForRuntime(0.2f);
        Assert.That(backdrop.Current, Is.SameAs(current));
        Assert.That(map.GeneratedLayout, Is.SameAs(layout));
        CollectionAssert.AreEqual(blocks, map.Blocks);
        Assert.That(Field<bool>(backdrop, "preview"), Is.False);
        Assert.That(Field<float>(backdrop, "targetDistance"),
            Is.EqualTo(Field<float>(backdrop, "radius") * Field<float>(backdrop, "distanceScale")));
        // 맵을 붙잡으면 블록뿐 아니라 배경 바닥과 장식도 제자리에 선다.
        IReadOnlyList<Transform> pieces = map.Pieces;
        Assert.That(pieces.Count, Is.GreaterThan(blocks.Count), "The wave must carry the background with the map.");
        Vector3[] homes = Field<Vector3[]>(backdrop, "homes");
        Assert.That(homes.Length, Is.EqualTo(pieces.Count));
        for (int p = 0; p < pieces.Count; p++)
            Assert.That(Vector3.Distance(pieces[p].localPosition, homes[p]), Is.LessThan(0.001f));

        NetworkLobbySnapshot approved = Snapshot(0, expected.Map.Seed);
        approved.Settings = new NetworkMapSettings(expected.Map);
        InvokeFlow("OnSnapshot", approved);
        Assert.That(Field<bool>(backdrop, "preview"), Is.True);
        Assert.That(Field<MapGenerationSettings>(Field<LobbyStage>(flow, "stage"), "revealMap"), Is.Null);
        Assert.That(backdrop.Current, Is.SameAs(current));
        Assert.That(map.GeneratedLayout, Is.SameAs(layout));
        CollectionAssert.AreEqual(blocks, map.Blocks);

        float transitionTime = Field<float>(Field<ScreenTransition>(flow, "startTransition"), "hideDuration") +
            Field<float>(Field<ScreenTransition>(flow, "lobbyTransition"), "showDuration");
        float runtimeDeadline = Time.unscaledTime + transitionTime + 0.5f;
        float realtimeDeadline = Time.realtimeSinceStartup + 3f;
        while (Field<Coroutine>(flow, "transition") != null && Time.unscaledTime < runtimeDeadline &&
            Time.realtimeSinceStartup < realtimeDeadline) yield return WaitForFrames(1);
        Assert.That(Field<bool>(flow, "lobbyVisible"), Is.True);
        Assert.That(Field<Coroutine>(flow, "transition"), Is.Null);
        Assert.That(backdrop.Current, Is.SameAs(current));
        Assert.That(map.GeneratedLayout, Is.SameAs(layout));
        CollectionAssert.AreEqual(blocks, map.Blocks);
    }

    [UnityTest]
    public IEnumerator TitleWave_CarriesBackgroundWithMap()
    {
        TitleBackdrop backdrop = Field<TitleBackdrop>(flow, "backdrop");
        ProceduralMap map = Field<ProceduralMap>(backdrop, "map");
        ArenaBackgroundBuilderNew background = Field<ArenaBackgroundBuilderNew>(backdrop, "background");
        Assert.That(background, Is.Not.Null);
        // 씬을 연 뒤 배경의 Start가 같은 맵의 배경을 다시 만들면 물결이 잡은 첫 맵의 조각이 파괴된다.
        foreach (Transform piece in map.Pieces) Assert.That(piece != null, Is.True, "Start must not rebuild the background.");

        // 느린 프레임이 몇 번 지나도 가장 먼 조각이 아직 출발하지 않도록 물결을 늘린다.
        SetField(backdrop, "waveDuration", 2f);
        backdrop.ShowTitle(new DuelSettings
        {
            Map = new MapGenerationSettings
            {
                Size = new Vector2Int(35, 29), Shape = MapShape.Rectangle,
                ObstacleDensity = 0.2f, Symmetry = MapSymmetry.None
            }
        }, false);
        yield return WaitForFrames(1);

        IReadOnlyList<Transform> pieces = map.Pieces;
        int blockCount = map.Blocks.Count;
        Transform root = Field<Transform>(background, "backgroundRoot");
        Transform farGround = null;
        foreach (Transform child in root)
            if (child.name.StartsWith("FarGround")) farGround = child;
        Assert.That(farGround, Is.Not.Null);
        // 배경 루트 아래는 FarGround를 빼고 모두 물결에 들어간다. FarGround는 조각이 가라앉는 바닥이라 그대로 있다.
        Assert.That(pieces.Count - blockCount, Is.GreaterThan(0));
        Assert.That(pieces.Count - blockCount, Is.EqualTo(root.childCount - 1));
        // Transform은 자식을 도는 IEnumerable이라 CollectionAssert는 자식 없는 Transform끼리 같다고 본다. 참조로 비교한다.
        bool farGroundListed = false;
        foreach (Transform piece in pieces) farGroundListed |= piece == farGround;
        Assert.That(farGroundListed, Is.False, "FarGround must stay out of the wave.");
        Assert.That(farGround.position.y, Is.EqualTo(Field<float>(background, "farGroundY")).Within(0.001f));

        // 물결은 맵 가장자리에서 끝나지 않고 배경 바깥까지 이어진다. 가장 먼 조각은 아직 출발하지 않았다.
        float[] delays = Field<float[]>(backdrop, "delays");
        Vector3[] homes = Field<Vector3[]>(backdrop, "homes");
        int farthest = 0;
        float blockDelay = 0f;
        for (int p = 0; p < pieces.Count; p++)
        {
            if (delays[p] > delays[farthest]) farthest = p;
            if (p < blockCount) blockDelay = Mathf.Max(blockDelay, delays[p]);
        }
        Assert.That(farthest, Is.GreaterThanOrEqualTo(blockCount));
        Assert.That(delays[farthest], Is.GreaterThan(blockDelay));
        Vector3 sunk = homes[farthest] + Vector3.down * Field<float>(backdrop, "sinkDepth");
        Assert.That(Vector3.Distance(pieces[farthest].localPosition, sunk), Is.LessThan(0.001f));

        // 맵을 붙잡으면 배경까지 모두 제자리로 돌아온다.
        SetField(backdrop, "blockDuration", 0.05f);
        backdrop.HoldTitle();
        yield return WaitForRuntime(delays[farthest] + 0.2f);
        for (int p = 0; p < pieces.Count; p++)
            Assert.That(Vector3.Distance(pieces[p].localPosition, homes[p]), Is.LessThan(0.001f));
    }

    // 로비 화면에서 부시 밀도만 바꾸면 미리보기 맵 전체를 다시 솟게 하지 않는다.
    // 벽·바닥·배경은 제자리에 서 있고, 전에 없던 자리의 부시만 땅속에서 올라온다.
    [UnityTest]
    public IEnumerator BushDensityPreview_KeepsWallsStanding_AndRaisesOnlyNewBushes()
    {
        TitleBackdrop backdrop = Field<TitleBackdrop>(flow, "backdrop");
        ProceduralMap map = Field<ProceduralMap>(backdrop, "map");
        var settings = new MapGenerationSettings
        {
            Seed = 4242, Size = new Vector2Int(35, 29), Shape = MapShape.Rectangle,
            ObstacleDensity = 0.2f, BushDensity = 0.03f
        };
        SetField(backdrop, "waveDuration", 0.01f);
        SetField(backdrop, "blockDuration", 0.01f);
        Assert.That(backdrop.ShowPreview(settings), Is.True);
        yield return WaitForRuntime(0.2f);

        string[] before = map.GeneratedLayout.ToLayout();
        var oldBushes = new HashSet<Vector3>();
        foreach (Transform piece in map.Pieces)
            if (piece.GetComponent<BushBlock>() != null) oldBushes.Add(piece.localPosition);
        Assert.That(oldBushes.Count, Is.GreaterThan(0));

        // 다시 솟는 조각이 한 프레임 만에 제자리까지 오지 않도록 천천히 올린다.
        SetField(backdrop, "waveDuration", 1f);
        SetField(backdrop, "blockDuration", 5f);
        settings = new MapGenerationSettings
        {
            Seed = settings.Seed, Size = settings.Size, Shape = settings.Shape,
            ObstacleDensity = settings.ObstacleDensity, BushDensity = 0.15f
        };
        Assert.That(backdrop.ShowPreview(settings), Is.True);
        yield return WaitForFrames(1);

        string[] after = map.GeneratedLayout.ToLayout();
        Assert.That(after.Length, Is.EqualTo(before.Length));
        for (int row = 0; row < before.Length; row++)
            Assert.That(after[row].Replace('*', '.'), Is.EqualTo(before[row].Replace('*', '.')), $"Row {row} changed walls.");

        IReadOnlyList<Transform> pieces = map.Pieces;
        Vector3[] homes = Field<Vector3[]>(backdrop, "homes");
        int risingBushes = 0;
        for (int p = 0; p < pieces.Count; p++)
        {
            bool bush = pieces[p].GetComponent<BushBlock>() != null;
            float drop = homes[p].y - pieces[p].localPosition.y;
            if (bush && !oldBushes.Contains(homes[p]))
            {
                Assert.That(drop, Is.GreaterThan(1f), $"New bush {pieces[p].name} must rise from below.");
                risingBushes++;
            }
            else Assert.That(drop, Is.EqualTo(0f).Within(0.001f), $"{pieces[p].name} must stay standing.");
        }
        Assert.That(risingBushes, Is.GreaterThan(0), "More bushes should rise in.");
    }

    [UnityTest]
    public IEnumerator CreateCanceledBeforeApproval_ResumesTitleCycle() => CheckTitleCycleResumes(false);

    [UnityTest]
    public IEnumerator CreateFailedBeforeApproval_ResumesTitleCycle() => CheckTitleCycleResumes(true);

    IEnumerator CheckTitleCycleResumes(bool failed)
    {
        TitleBackdrop backdrop = PrepareFastTitleCycle();
        MapGenerationSettings held = backdrop.Current;
        InvokeFlow("Create");
        yield return WaitForRuntime(0.2f);
        Assert.That(backdrop.Current, Is.SameAs(held));
        if (failed)
        {
            network.Cancel();
            InvokeFlow("OnConnectionFailed", "Disconnected from the server.");
        }
        else InvokeFlow("Cancel");
        yield return WaitForRuntime(0.3f);
        Assert.That(backdrop.Current, Is.Not.SameAs(held));
        Assert.That(Field<bool>(backdrop, "preview"), Is.False);
    }

    TitleBackdrop PrepareFastTitleCycle()
    {
        TitleBackdrop backdrop = Field<TitleBackdrop>(flow, "backdrop");
        SetField(backdrop, "regenerateInterval", 0.05f);
        SetField(backdrop, "waveDuration", 0.02f);
        SetField(backdrop, "blockDuration", 0.03f);
        backdrop.ShowTitle(new DuelSettings
        {
            Map = new MapGenerationSettings
            {
                Size = new Vector2Int(35, 29), Shape = MapShape.Rectangle,
                ObstacleDensity = 0.22f, Symmetry = MapSymmetry.None,
                NoiseStrength = 0.65f, NoiseScale = 1.25f
            }
        }, false);
        return backdrop;
    }

    NetworkLobbySnapshot Snapshot(uint revision, int seed) => new NetworkLobbySnapshot
    {
        Code = "123456", HostClientId = manager.LocalClientId, SettingsRevision = revision,
        Settings = new NetworkMapSettings(new MapGenerationSettings { Seed = seed, ObstacleDensity = 0.2f })
    };

    static void AssertMapEqual(MapGenerationSettings expected, MapGenerationSettings actual)
    {
        Assert.That(actual.Seed, Is.EqualTo(expected.Seed));
        Assert.That(actual.Size, Is.EqualTo(expected.Size));
        Assert.That(actual.Shape, Is.EqualTo(expected.Shape));
        Assert.That(actual.ObstacleDensity, Is.EqualTo(expected.ObstacleDensity));
        Assert.That(actual.BushDensity, Is.EqualTo(expected.BushDensity));
        Assert.That(actual.Symmetry, Is.EqualTo(expected.Symmetry));
        Assert.That(actual.NoiseStrength, Is.EqualTo(expected.NoiseStrength));
        Assert.That(actual.NoiseScale, Is.EqualTo(expected.NoiseScale));
    }

    void OpenLobby(bool isHost)
    {
        start.gameObject.SetActive(false);
        lobby.gameObject.SetActive(true);
        lobby.Open(isHost, "123456", new DuelSettings
        {
            Map = new MapGenerationSettings { Seed = 7301 }
        }, false);
    }

    IEnumerator PressEscape() => QueueEscape(true);

    static IEnumerator Focus(TMP_InputField field)
    {
        EventSystem.current.SetSelectedGameObject(field.gameObject);
        field.ActivateInputField();
        yield return WaitForFrames(2);
        Assert.That(field.isFocused, Is.True);
    }

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

    static IEnumerator WaitForRuntime(float seconds)
    {
        // EditMode runner는 EnterPlayMode 뒤에도 WaitForSeconds를 게임 코루틴처럼 기다리지 않는다.
        float targetTime = Time.time + seconds;
        int firstFrame = Time.frameCount;
        float deadline = Time.realtimeSinceStartup + 3f;
        while (Time.time < targetTime && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(Time.time, Is.GreaterThanOrEqualTo(targetTime), "Runtime animation time must elapse.");
        Assert.That(Time.frameCount, Is.GreaterThan(firstFrame));
    }

    static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, PrivateInstance).GetValue(owner);

    static void SetField(object owner, string name, object value) =>
        owner.GetType().GetField(name, PrivateInstance).SetValue(owner, value);

    void InvokeFlow(string method) => typeof(MainFlow).GetMethod(method, PrivateInstance).Invoke(flow, null);

    void InvokeFlow(string method, object value) =>
        typeof(MainFlow).GetMethod(method, PrivateInstance).Invoke(flow, new[] { value });
}
#endif

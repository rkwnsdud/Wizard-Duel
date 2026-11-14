using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 시작 화면 "설정" 모달의 그래픽 항목. 값을 바꾸면 바로 적용한다.
// 화면 모드와 해상도는 Screen으로 바꾸고(Unity 플레이어가 다음 실행에도 기억한다), 나머지는 GraphicsOptions가 저장하고 적용한다.
// 에디터에서는 Game 뷰 크기가 바뀌지 않으므로 화면 모드와 해상도는 빌드에서만 효과가 보인다.
public class GraphicsOptionsPanel : MonoBehaviour
{
    const int FullScreen = 0;
    const int Windowed = 1;
    const float DisabledArrowAlpha = 0.3f;

    [SerializeField] SegmentedControl screenMode;
    [SerializeField] Button previousResolution;
    [SerializeField] Button nextResolution;
    [SerializeField] TMP_Text resolutionLabel;
    [SerializeField] SegmentedControl quality;
    [SerializeField] SegmentedControl antiAliasing;
    [SerializeField] SegmentedControl vSync;
    [SerializeField] SegmentedControl frameLimit;
    [Tooltip("최대 프레임 줄의 버튼 묶음. 수직 동기화를 켜면 쓰이지 않으므로 흐리게 잠근다.")]
    [SerializeField] CanvasGroup frameLimitGroup;

    readonly List<Vector2Int> sizes = new List<Vector2Int>();
    int sizeIndex;
    bool ready;

    // 모달을 열 때 지금 화면 상태와 저장된 그래픽 값을 받아 보여 준다. 모달을 켠 뒤에 부른다.
    public void Open()
    {
        Setup();
        screenMode.SetValueWithoutNotify(Screen.fullScreenMode == FullScreenMode.Windowed ? Windowed : FullScreen);
        var screenSize = new Vector2Int(Screen.width, Screen.height);
        sizes.Clear();
        sizes.AddRange(GraphicsOptions.ScreenSizes(Screen.resolutions, screenSize));
        sizeIndex = sizes.IndexOf(screenSize);
        ShowResolution();

        GraphicsOptions options = GraphicsOptions.Current;
        quality.SetValueWithoutNotify((int)options.Quality);
        antiAliasing.SetValueWithoutNotify(Array.IndexOf(GraphicsOptions.MsaaSamples, options.Msaa));
        vSync.SetValueWithoutNotify(options.VSync ? 1 : 0);
        frameLimit.SetValueWithoutNotify(Array.IndexOf(GraphicsOptions.FrameLimits, options.FrameLimit));
        LockFrameLimit(options.VSync);
    }

    // 처음 열 때 한 번 컨트롤을 구독한다. 이 모달은 꺼진 채로 시작해서 Awake에 두지 않는다.
    void Setup()
    {
        if (ready) return;
        ready = true;
        screenMode.Changed += _ => ApplyScreen();
        previousResolution.onClick.AddListener(() => StepResolution(-1));
        nextResolution.onClick.AddListener(() => StepResolution(1));
        quality.Changed += _ => Edit();
        antiAliasing.Changed += _ => Edit();
        vSync.Changed += _ => Edit();
        frameLimit.Changed += _ => Edit();
    }

    void StepResolution(int step)
    {
        int next = Mathf.Clamp(sizeIndex + step, 0, sizes.Count - 1);
        if (next == sizeIndex) return;
        sizeIndex = next;
        ShowResolution();
        ApplyScreen();
    }

    void ApplyScreen()
    {
        Vector2Int size = sizes[sizeIndex];
        FullScreenMode mode = screenMode.Value == Windowed ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
        Screen.SetResolution(size.x, size.y, mode);
    }

    void Edit()
    {
        var options = new GraphicsOptions
        {
            Quality = (GraphicsQuality)quality.Value,
            Msaa = GraphicsOptions.MsaaSamples[antiAliasing.Value],
            VSync = vSync.Value == 1,
            FrameLimit = GraphicsOptions.FrameLimits[frameLimit.Value]
        };
        GraphicsOptions.Change(options);
        LockFrameLimit(options.VSync);
    }

    void ShowResolution()
    {
        Vector2Int size = sizes[sizeIndex];
        resolutionLabel.text = $"{size.x} × {size.y}";
        SetArrow(previousResolution, sizeIndex > 0);
        SetArrow(nextResolution, sizeIndex < sizes.Count - 1);
    }

    static void SetArrow(Button arrow, bool usable)
    {
        arrow.interactable = usable;
        TMP_Text label = arrow.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.alpha = usable ? 1f : DisabledArrowAlpha;
    }

    void LockFrameLimit(bool locked)
    {
        frameLimitGroup.interactable = frameLimitGroup.blocksRaycasts = !locked;
        frameLimitGroup.alpha = locked ? 0.5f : 1f;
    }
}

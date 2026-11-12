using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 버튼 여러 개 중 하나만 켜지는 선택 묶음. 켜진 버튼은 바탕과 글자 색이 바뀐다.
// 글자 대신 그림을 쓰는 버튼(무기 아이콘)은 icons에 넣으면 켜진 버튼의 그림만 또렷하게 보인다.
public class SegmentedControl : MonoBehaviour
{
    public event Action<int> Changed;

    [SerializeField] Button[] buttons;
    [SerializeField] Color onColor = new Color32(0xFF, 0xD8, 0x66, 0xFF);
    [SerializeField] Color offColor = new Color(1f, 1f, 1f, 0.06f);
    [SerializeField] Color onTextColor = new Color32(0x2A, 0x1F, 0x05, 0xFF);
    [SerializeField] Color offTextColor = new Color32(0x9D, 0xA5, 0xB8, 0xFF);
    [Tooltip("버튼 안의 그림. 버튼과 같은 순서로 넣는다. 그림이 없는 묶음은 비워 둔다.")]
    [SerializeField] Graphic[] icons = new Graphic[0];
    [SerializeField] Color onIconColor = Color.white;
    [SerializeField] Color offIconColor = new Color(1f, 1f, 1f, 0.45f);

    public int Value { get; private set; }
    public int Count => buttons.Length;

    void Awake()
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            buttons[i].onClick.AddListener(() => Select(index));
        }
        Refresh();
    }

    public void SetValueWithoutNotify(int value)
    {
        Value = value;
        Refresh();
    }

    void Select(int index)
    {
        if (index == Value) return;
        Value = index;
        Refresh();
        Changed?.Invoke(index);
    }

    void Refresh()
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            bool on = i == Value;
            buttons[i].targetGraphic.color = on ? onColor : offColor;
            TMP_Text label = buttons[i].GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.color = on ? onTextColor : offTextColor;
            if (i < icons.Length && icons[i] != null) icons[i].color = on ? onIconColor : offIconColor;
        }
    }
}

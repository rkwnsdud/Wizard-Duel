using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 버튼 여러 개 중 하나만 켜지는 선택 묶음. 켜진 버튼은 바탕과 글자 색이 바뀐다.
public class SegmentedControl : MonoBehaviour
{
    public event Action<int> Changed;

    [SerializeField] Button[] buttons;
    [SerializeField] Color onColor = new Color32(0xFF, 0xD8, 0x66, 0xFF);
    [SerializeField] Color offColor = new Color(1f, 1f, 1f, 0.06f);
    [SerializeField] Color onTextColor = new Color32(0x2A, 0x1F, 0x05, 0xFF);
    [SerializeField] Color offTextColor = new Color32(0x9D, 0xA5, 0xB8, 0xFF);

    public int Value { get; private set; }

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
            buttons[i].targetGraphic.color = i == Value ? onColor : offColor;
            buttons[i].GetComponentInChildren<TMP_Text>().color = i == Value ? onTextColor : offTextColor;
        }
    }
}

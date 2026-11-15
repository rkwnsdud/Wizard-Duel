using System;
using TMPro;
using UnityEngine;

// AI 대전 로비 화면의 "게임 설정" 탭. 내 무기, 상대 무기, AI 난이도를 고른다.
// 고를 수 있는 무기와 순서는 전투 중 스폰되는 무기와 같은 카탈로그(WeaponSpawnCatalog)를 따르고,
// 이름과 아이콘도 같은 무기 정의에서 읽는다.
public class GameSettingsPanel : MonoBehaviour
{
    public event Action<AiDuelOptions> Changed;

    [SerializeField] WeaponSpawnCatalog weapons;
    [SerializeField] SegmentedControl myWeapon;
    [SerializeField] TMP_Text myWeaponName;
    [SerializeField] SegmentedControl opponentWeapon;
    [SerializeField] TMP_Text opponentWeaponName;
    [SerializeField] SegmentedControl difficulty;
    [Tooltip("전투 준비가 시작되면 잠그는 컨트롤 묶음.")]
    [SerializeField] CanvasGroup[] controls;

    AiDuelOptions options = new AiDuelOptions();
    bool ready;

    public WeaponSpawnCatalog Weapons => weapons;
    // 카탈로그의 정의 수에 맞춰 무기 선택 버튼을 만든다.
    public int WeaponButtonCount => Mathf.Min(myWeapon.Count, opponentWeapon.Count);

    // 화면에 보여 주는 무기 이름.
    public static string NameOf(WeaponBase weapon) => weapon == null ? "" :
        weapon.Definition != null ? weapon.Definition.displayName : weapon.name;

    // 로비 화면을 열 때 지금 고른 값을 받아 보여 준다.
    public void Open(AiDuelOptions current)
    {
        Setup();
        options = current.Clone();
        myWeapon.SetValueWithoutNotify(IndexOf(options.MyWeapon));
        opponentWeapon.SetValueWithoutNotify(IndexOf(options.OpponentWeapon));
        difficulty.SetValueWithoutNotify(Mathf.Clamp((int)options.Difficulty, 0, difficulty.Count - 1));
        ShowNames();
        SetLocked(false);
    }

    public void SetLocked(bool locked)
    {
        foreach (CanvasGroup control in controls)
        {
            control.interactable = control.blocksRaycasts = !locked;
            control.alpha = locked ? 0.5f : 1f;
        }
    }

    // 처음 열 때 한 번, 카탈로그의 아이콘을 버튼에 넣고 값이 바뀌는 것을 듣는다.
    // 이 탭은 꺼진 채로 열릴 수 있어서 Awake에 두지 않는다.
    void Setup()
    {
        if (ready) return;
        ready = true;
        var icons = Array.ConvertAll(weapons.Entries, definition => definition.icon);
        myWeapon.SetIcons(icons);
        opponentWeapon.SetIcons(icons);
        myWeapon.Changed += _ => Edit();
        opponentWeapon.Changed += _ => Edit();
        difficulty.Changed += _ => Edit();
    }

    void Edit()
    {
        options.MyWeapon = IdAt(myWeapon.Value);
        options.OpponentWeapon = IdAt(opponentWeapon.Value);
        options.Difficulty = (BotDifficulty)difficulty.Value;
        ShowNames();
        Changed?.Invoke(options.Clone());
    }

    void ShowNames()
    {
        myWeaponName.text = NameOf(PrefabAt(myWeapon.Value));
        opponentWeaponName.text = NameOf(PrefabAt(opponentWeapon.Value));
    }

    // 카탈로그에 없는 id는 첫 무기로 본다.
    int IndexOf(byte id)
    {
        int index = Array.FindIndex(weapons.Entries, entry => entry.id == id);
        return Mathf.Clamp(index, 0, Mathf.Max(0, WeaponButtonCount - 1));
    }

    byte IdAt(int index) => index < weapons.Entries.Length ? weapons.Entries[index].id : AiDuelOptions.DefaultWeapon;

    WeaponBase PrefabAt(int index) => index < weapons.Entries.Length ? weapons.Entries[index].prefab : null;
}

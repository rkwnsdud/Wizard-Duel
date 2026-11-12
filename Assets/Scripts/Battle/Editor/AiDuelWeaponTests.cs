#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// AI 로비 화면의 "게임 설정" 탭에서 고르는 무기와 난이도가 실제 에셋·코드와 맞는지 본다.
public class AiDuelWeaponTests
{
    static WeaponSpawnCatalog Weapons()
    {
        string[] guids = AssetDatabase.FindAssets("t:WeaponSpawnCatalog");
        Assert.That(guids, Is.Not.Empty, "WeaponSpawnCatalog 에셋이 없다.");
        return AssetDatabase.LoadAssetAtPath<WeaponSpawnCatalog>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }

    static SecondaryWeaponHudCatalog Icons()
    {
        string[] guids = AssetDatabase.FindAssets("t:SecondaryWeaponHudCatalog");
        Assert.That(guids, Is.Not.Empty, "SecondaryWeaponHudCatalog 에셋이 없다.");
        return AssetDatabase.LoadAssetAtPath<SecondaryWeaponHudCatalog>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }

    [Test]
    public void EveryCatalogWeaponHasAnIconAndAName()
    {
        SecondaryWeaponHudCatalog icons = Icons();
        foreach (WeaponSpawnCatalog.Entry entry in Weapons().Entries)
        {
            Assert.That(entry.prefab, Is.Not.Null, $"id={entry.id}");
            SecondaryWeaponHudCatalog.Entry icon = icons.Find(entry.prefab);
            Assert.That(icon, Is.Not.Null, entry.prefab.name);
            Assert.That(icon.icon, Is.Not.Null, entry.prefab.name);
            // 한글 이름을 정해 두지 않은 무기는 프리팹 이름이 그대로 나온다.
            Assert.That(GameSettingsPanel.NameOf(entry.prefab), Is.Not.EqualTo(entry.prefab.name), entry.prefab.name);
        }
    }

    // 봇은 IWeaponReach로 사거리를 읽어 거리를 잡는다. 구현하지 않은 무기는 봇이 제대로 쓰지 못한다.
    [Test]
    public void EveryCatalogWeaponTellsTheBotItsReach()
    {
        foreach (WeaponSpawnCatalog.Entry entry in Weapons().Entries)
        {
            Assert.That(entry.prefab, Is.InstanceOf<IWeaponReach>(), entry.prefab.name);
            var reach = (IWeaponReach)entry.prefab;
            Assert.That(reach.ReachAt(0f), Is.GreaterThan(0f), entry.prefab.name);
            Assert.That(reach.ReachAt(1f), Is.GreaterThanOrEqualTo(reach.ReachAt(0f)), entry.prefab.name);
            Assert.That(reach.ShotSpeed, Is.GreaterThanOrEqualTo(0f), entry.prefab.name);
        }
    }

    [Test]
    public void DefaultWeaponIsInTheCatalog()
    {
        var options = new AiDuelOptions();
        WeaponSpawnCatalog weapons = Weapons();
        Assert.That(weapons.Find(options.MyWeapon)?.prefab, Is.Not.Null);
        Assert.That(weapons.Find(options.OpponentWeapon)?.prefab, Is.Not.Null);
    }

    [Test]
    public void OptionsCloneIsIndependent()
    {
        var options = new AiDuelOptions { MyWeapon = 4, OpponentWeapon = 5, Difficulty = BotDifficulty.Hard };
        AiDuelOptions copy = options.Clone();
        options.MyWeapon = 2;
        options.Difficulty = BotDifficulty.Easy;

        Assert.That(copy.MyWeapon, Is.EqualTo(4));
        Assert.That(copy.OpponentWeapon, Is.EqualTo(5));
        Assert.That(copy.Difficulty, Is.EqualTo(BotDifficulty.Hard));
    }

    // 어려울수록 빨리 반응하고, 덜 빗나가고, 더 자주 피한다.
    [Test]
    public void DifficultyPresetsGetHarderInOrder()
    {
        DuelBotTuning easy = DuelBotTuning.Easy(), normal = DuelBotTuning.Normal(), hard = DuelBotTuning.Hard();

        Assert.That(easy.reactionTime, Is.GreaterThan(normal.reactionTime));
        Assert.That(normal.reactionTime, Is.GreaterThan(hard.reactionTime));
        Assert.That(easy.aimError, Is.GreaterThan(normal.aimError));
        Assert.That(normal.aimError, Is.GreaterThan(hard.aimError));
        Assert.That(easy.dodgeChance, Is.LessThan(normal.dodgeChance));
        Assert.That(normal.dodgeChance, Is.LessThan(hard.dodgeChance));
        Assert.That(easy.projectileLead, Is.LessThan(hard.projectileLead));
    }

    // 로비 화면의 무기 버튼은 씬에 놓여 있다. 카탈로그에 무기를 더하면 버튼도 더해야 고를 수 있다.
    [Test]
    public void LobbyHasAButtonForEveryCatalogWeapon()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/Scenes/Main.unity");
        if (!scene.IsValid() || !scene.isLoaded) Assert.Ignore("Main 씬이 열려 있지 않다.");
        GameSettingsPanel panel = Object.FindFirstObjectByType<GameSettingsPanel>(FindObjectsInactive.Include);
        Assert.That(panel, Is.Not.Null);
        Assert.That(panel.Weapons, Is.Not.Null);
        Assert.That(panel.WeaponIcons, Is.Not.Null);
        Assert.That(panel.WeaponButtonCount, Is.EqualTo(panel.Weapons.Entries.Length));
    }
}
#endif

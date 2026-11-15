#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// 공격 하나의 정보(번호·무기 id)가 공격 시작부터 발사·연출·명중 보고까지 그대로 이어지는지 본다.
// 네트워크 없이 PlayerAttack과 무기만 세워 가벼운 테스트로 돈다.
public class AttackEventTests
{
    const byte ExplosionId = 4;
    const byte CenserId = 5;

    readonly List<Object> created = new List<Object>();

    sealed class RecordingAuthority : IAttackAuthority
    {
        public readonly List<AttackInfo> Starts = new List<AttackInfo>();
        public readonly List<AttackInfo> Fires = new List<AttackInfo>();
        public void StartAttack(AttackInfo attack, float chargeRatio) => Starts.Add(attack);
        public void FireMagic(AttackInfo attack, float chargeRatio) => Fires.Add(attack);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object item in created)
            if (item != null) Object.DestroyImmediate(item);
        created.Clear();
    }

    WeaponDefinition Definition(byte id)
    {
        var definition = ScriptableObject.CreateInstance<WeaponDefinition>();
        definition.id = id;
        created.Add(definition);
        return definition;
    }

    T AddWeapon<T>(Transform socket, byte id) where T : WeaponBase
    {
        var holder = new GameObject(typeof(T).Name);
        holder.transform.SetParent(socket, false);
        T weapon = holder.AddComponent<T>();
        var serialized = new SerializedObject(weapon);
        serialized.FindProperty("definition").objectReferenceValue = Definition(id);
        serialized.FindProperty("attackCooldown").floatValue = 0f;
        serialized.FindProperty("attackAnimation").objectReferenceValue = null;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return weapon;
    }

    // 꺼 둔 채 세우고 Awake를 직접 불러 편집 모드에서도 같은 초기화를 거친다.
    PlayerAttack Player(string name, out Transform socket)
    {
        var root = new GameObject(name);
        root.SetActive(false);
        created.Add(root);
        var wizard = new GameObject("Wizard");
        wizard.transform.SetParent(root.transform, false);
        wizard.AddComponent<Animator>();
        socket = new GameObject("WeaponSocket").transform;
        socket.SetParent(wizard.transform, false);
        return root.AddComponent<PlayerAttack>();
    }

    static void Awake(PlayerAttack attack) => typeof(PlayerAttack)
        .GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(attack, null);

    [Test]
    public void NextIdSkipsZeroAndStatusEffectTickIds()
    {
        Assert.That(AttackInfo.NextId(0), Is.EqualTo(1u));
        Assert.That(AttackInfo.NextId(41), Is.EqualTo(42u));
        Assert.That(AttackInfo.NextId(AttackInfo.MaxId), Is.EqualTo(1u));
        Assert.That(AttackInfo.MaxId & StatusEffectState.DotAttackIdFlag, Is.Zero);
        Assert.That(new AttackInfo(0, ExplosionId).IsValid, Is.False);
        Assert.That(new AttackInfo(1, 0).IsValid, Is.False);
        Assert.That(new AttackInfo(1, ExplosionId).IsValid, Is.True);
    }

    [Test]
    public void OwnerAttackGetsItsNumberAndWeaponAtStart_AndFiresWithTheSameInfo()
    {
        PlayerAttack attack = Player("Attack info owner", out Transform socket);
        var book = AddWeapon<ExplosionMagicWeapon>(socket, ExplosionId);
        var censer = AddWeapon<FireCenserWeapon>(socket, CenserId);
        censer.gameObject.SetActive(false);
        Awake(attack);
        var authority = new RecordingAuthority();
        attack.Authority = authority;
        Assert.That(attack.TryEquipWeapon(book), Is.True);

        Assert.That(attack.TryStartAttack(0f), Is.True);
        Assert.That(attack.CurrentAttack, Is.EqualTo(new AttackInfo(1, ExplosionId)));
        Assert.That(authority.Starts, Is.EqualTo(new[] { new AttackInfo(1, ExplosionId) }));
        attack.FireMagic();
        Assert.That(authority.Fires, Is.EqualTo(new[] { new AttackInfo(1, ExplosionId) }));
        attack.EndAttack();
        Assert.That(attack.CurrentAttack.Id, Is.Zero, "No attack is in progress after it ends.");

        Assert.That(attack.TryEquipWeapon(censer), Is.True);
        Assert.That(attack.TryStartAttack(0f), Is.True);
        Assert.That(attack.CurrentAttack, Is.EqualTo(new AttackInfo(2, CenserId)));
        attack.FireMagic();
        Assert.That(authority.Fires[1], Is.EqualTo(new AttackInfo(2, CenserId)));
        attack.CancelAttack();
        Assert.That(attack.CurrentAttack.Id, Is.Zero);
    }

    [Test]
    public void ReplicatedAttackKeepsTheNumberChosenByTheAttacker()
    {
        PlayerAttack attack = Player("Attack info remote", out Transform socket);
        var book = AddWeapon<ExplosionMagicWeapon>(socket, ExplosionId);
        Awake(attack);
        var authority = new RecordingAuthority();
        attack.Authority = authority;
        Assert.That(attack.TryEquipWeapon(book), Is.True);

        attack.PlayReplicatedAttack(new AttackInfo(7, ExplosionId), 0f);

        Assert.That(attack.IsAttacking, Is.True);
        Assert.That(attack.CurrentAttack, Is.EqualTo(new AttackInfo(7, ExplosionId)));
        Assert.That(authority.Starts, Is.Empty, "Replaying the opponent's attack must not start a new one.");
        attack.EndAttack();
    }
}
#endif

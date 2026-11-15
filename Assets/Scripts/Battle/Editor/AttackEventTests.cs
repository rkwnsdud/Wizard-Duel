#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
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

    // DuelPlayer의 싱크(Broadcast→ShotRpc, Hit→ReportHitRpc)를 네트워크 없이 흉내 낸다.
    // 연출은 공격한 쪽 화면과 상대 화면이 각자 PresentShot으로 재생하고, 명중 보고는 서버로 가는 것처럼 쌓는다.
    sealed class DuelSink : IShotSink
    {
        public PlayerAttack Owner, Remote;
        public readonly List<(AttackInfo attack, int index, PlayerHealth target, float damage)> Reports =
            new List<(AttackInfo, int, PlayerHealth, float)>();
        public void Broadcast(ShotEvent shot)
        {
            Owner.PresentShot(shot);
            Remote?.PresentShot(shot);
        }
        public void Hit(AttackInfo attack, int index, PlayerHealth target, float damage) =>
            Reports.Add((attack, index, target, damage));
    }

    // DuelPlayer처럼 공격 시작을 상대 화면에 전하고, 공격 정보의 무기로 쏜다.
    sealed class DuelAuthority : IAttackAuthority
    {
        public PlayerAttack Owner, Remote;
        public IShotSink Sink;
        public void StartAttack(AttackInfo attack, float chargeRatio) => Remote?.PlayReplicatedAttack(attack, chargeRatio);
        public void FireMagic(AttackInfo attack, float chargeRatio) =>
            ((IWeapon)Owner.FindWeapon(attack.WeaponId)).Fire(attack, Vector3.forward, chargeRatio, Sink);
    }

    // 공격한 쪽과 상대 화면에 같은 두 무기(A: 폭발 id, B: 향로 id)를 든 플레이어를 세운다.
    void Duel(out PlayerAttack owner, out PlayerAttack remote, out AttackTestWeapon[] ownerWeapons,
        out AttackTestWeapon[] remoteWeapons, out DuelSink sink)
    {
        owner = Player("Attack owner", out Transform ownerSocket);
        remote = Player("Attack remote copy", out Transform remoteSocket);
        ownerWeapons = new[] { AddWeapon<AttackTestWeapon>(ownerSocket, ExplosionId), AddWeapon<AttackTestWeapon>(ownerSocket, CenserId) };
        remoteWeapons = new[] { AddWeapon<AttackTestWeapon>(remoteSocket, ExplosionId), AddWeapon<AttackTestWeapon>(remoteSocket, CenserId) };
        Awake(owner);
        Awake(remote);
        Assert.That(owner.TryEquipWeapon(ownerWeapons[0]), Is.True);
        Assert.That(remote.TryEquipWeapon(remoteWeapons[0]), Is.True);
        sink = new DuelSink { Owner = owner, Remote = remote };
        owner.Authority = new DuelAuthority { Owner = owner, Remote = remote, Sink = sink };
    }

    PlayerHealth Target()
    {
        var target = new GameObject("Attack target");
        target.SetActive(false);
        created.Add(target);
        return target.AddComponent<PlayerHealth>();
    }

    [Test]
    public void FlyingShotSwappedBeforeLanding_IsPresentedAndReportedAsTheFiredWeaponOnBothScreens()
    {
        Duel(out PlayerAttack owner, out PlayerAttack remote, out AttackTestWeapon[] mine, out AttackTestWeapon[] theirs,
            out DuelSink sink);
        PlayerHealth target = Target();

        Assert.That(owner.TryStartAttack(0f), Is.True);
        var shot = new AttackInfo(1, ExplosionId);
        Assert.That(remote.CurrentAttack, Is.EqualTo(shot), "The opponent replays the same attack.");
        owner.FireMagic();
        owner.EndAttack();
        remote.EndAttack();
        // 맞기 전에 두 화면 모두 B로 바꾼다(소유자의 교체와 상대 화면의 교체 스냅샷).
        Assert.That(owner.TryEquipWeapon(mine[1]), Is.True);
        Assert.That(remote.TryEquipWeapon(theirs[1]), Is.True);

        mine[0].Land(target);

        foreach (AttackTestWeapon fired in new[] { mine[0], theirs[0] })
        {
            Assert.That(fired.Presented.ConvertAll(e => e.Kind),
                Is.EqualTo(new[] { AttackTestWeapon.LaunchKind, AttackTestWeapon.LandKind }));
            Assert.That(fired.Presented.TrueForAll(e => e.Attack == shot), Is.True);
            Assert.That(fired.gameObject.activeSelf, Is.False, "The swapped-out weapon presents while hidden.");
        }
        Assert.That(mine[1].Presented, Is.Empty, "The weapon held now must not present the earlier attack.");
        Assert.That(theirs[1].Presented, Is.Empty);
        Assert.That(sink.Reports.Count, Is.EqualTo(1));
        Assert.That(sink.Reports[0].attack, Is.EqualTo(shot), "The hit report names the fired weapon.");
        Assert.That(sink.Reports[0].target, Is.SameAs(target));
    }

    [Test]
    public void ServerHitPassiveFollowsTheReportedWeapon_NotTheWeaponHeldNow()
    {
        Duel(out PlayerAttack owner, out _, out AttackTestWeapon[] mine, out _, out DuelSink sink);
        // 서버의 공격자 사본. 서버는 공격 모션을 재생하지 않고 교체 스냅샷만 받는다.
        PlayerAttack server = Player("Attacker on the server", out Transform socket);
        var serverWeapons = new[] { AddWeapon<AttackTestWeapon>(socket, ExplosionId), AddWeapon<AttackTestWeapon>(socket, CenserId) };
        Awake(server);
        Assert.That(server.TryEquipWeapon(serverWeapons[0]), Is.True);
        PlayerHealth target = Target();

        Assert.That(owner.TryStartAttack(0f), Is.True);
        owner.FireMagic();
        owner.EndAttack();
        Assert.That(owner.TryEquipWeapon(mine[1]), Is.True);
        Assert.That(server.TryEquipWeapon(serverWeapons[1]), Is.True);
        mine[0].Land(target);

        var report = sink.Reports[0];
        Assert.That(PlayerWeaponPassiveController.NotifyServerHit(server, report.attack, null, null), Is.True);
        Assert.That(serverWeapons[0].ServerHits, Is.EqualTo(1), "The fired weapon's hit passive applies.");
        Assert.That(serverWeapons[1].ServerHits, Is.Zero, "The weapon held now must not add its passive.");

        LogAssert.Expect(LogType.Warning, new Regex("Hit passive skipped for attack=9 weapon=200"));
        Assert.That(PlayerWeaponPassiveController.NotifyServerHit(server, new AttackInfo(9, 200), null, null), Is.False);
        Assert.That(serverWeapons[0].ServerHits + serverWeapons[1].ServerHits, Is.EqualTo(1));
    }

    [Test]
    public void AttackThatArrivesBeforeTheSlotSnapshot_EquipsTheAttackWeaponOnTheOpponentScreen()
    {
        Duel(out _, out PlayerAttack remote, out _, out AttackTestWeapon[] theirs, out _);

        remote.PlayReplicatedAttack(new AttackInfo(3, CenserId), 0f);

        Assert.That(remote.IsAttacking, Is.True);
        Assert.That(remote.CurrentWeapon, Is.SameAs(theirs[1]));
        Assert.That(remote.AttackingWeapon, Is.SameAs(theirs[1]));
        Assert.That(theirs[0].gameObject.activeSelf, Is.False);
        remote.EndAttack();
    }

    [Test]
    public void EventsForAWeaponThisScreenCannotFind_AreDroppedWithoutPresentingAnotherWeapon()
    {
        Duel(out _, out PlayerAttack remote, out _, out AttackTestWeapon[] theirs, out _);
        const byte unknown = 200;

        LogAssert.Expect(LogType.Warning, new Regex("dropped shot attack=5 weapon=200"));
        Assert.That(remote.PresentShot(new ShotEvent(new AttackInfo(5, unknown), 1, Vector3.zero, Vector3.zero, 0f)), Is.False);
        LogAssert.Expect(LogType.Warning, new Regex("skipped attack start attack=6 weapon=200"));
        remote.PlayReplicatedAttack(new AttackInfo(6, unknown), 0f);
        LogAssert.Expect(LogType.Warning, new Regex("dropped shot attack=7 weapon=0"));
        Assert.That(remote.PresentShot(new ShotEvent(new AttackInfo(7, 0), 0, Vector3.zero, Vector3.zero, 0f)), Is.False);

        Assert.That(remote.IsAttacking, Is.False);
        Assert.That(remote.CurrentWeapon, Is.SameAs(theirs[0]));
        Assert.That(theirs[0].Presented, Is.Empty);
        Assert.That(theirs[1].Presented, Is.Empty);
    }

    [Test]
    public void WeaponMissingOnThisScreen_IsCreatedFromItsDefinitionAndReusedByTheSlotSnapshot()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<WeaponSpawnCatalog>("Assets/Scripts/Battle/WeaponSpawnCatalog.asset");
        WeaponDefinition book = catalog.Find(ExplosionId);
        PlayerAttack remote = Player("Attack remote without the book", out Transform socket);
        var wand = AddWeapon<AttackTestWeapon>(socket, 1);
        var inventory = remote.gameObject.AddComponent<PlayerWeaponInventory>();
        var spawner = remote.gameObject.AddComponent<ArenaWeaponSpawner>();
        typeof(ArenaWeaponSpawner).GetField("catalog", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(spawner, catalog);
        inventory.NetworkAuthority = spawner;
        Awake(remote);
        Assert.That(remote.TryEquipWeapon(wand), Is.True);
        // 인벤토리는 실행 중에 쓰는 Destroy로 임시 부모를 지운다. 편집 모드에서는 오류만 남기고 루트와 함께 지워진다.
        LogAssert.Expect(LogType.Error, new Regex("Destroy may not be called from edit mode"));

        WeaponBase made = remote.FindWeapon(ExplosionId);

        Assert.That(made, Is.Not.Null);
        Assert.That(made.Id, Is.EqualTo(ExplosionId));
        Assert.That(made.gameObject.activeSelf, Is.False);
        Assert.That(made.transform.parent, Is.SameAs(socket));
        Assert.That(remote.FindWeapon(ExplosionId), Is.SameAs(made), "One instance per weapon id.");
        Assert.That(inventory.ApplyNetworkSlots(book.prefab, wand), Is.True);
        Assert.That(remote.CurrentWeapon, Is.SameAs(made), "The late slot snapshot equips the same instance.");
        Assert.That(remote.FindWeapon(0), Is.Null);
    }
}
#endif

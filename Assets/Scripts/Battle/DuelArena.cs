using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 서버에서 방 하나가 치르는 한 판. 서버는 물리 판정을 하지 않으므로 맵에서 지문과 스폰 자리만 받아 두고,
// 소환한 플레이어를 방마다 따로 가진다. 여러 방의 플레이어가 서버 씬의 같은 자리에 있어도 서로 보이지 않는다.
public sealed class DuelMatch
{
    public DuelMatch(string fingerprint, Pose[] spawns)
    {
        Fingerprint = fingerprint;
        Spawns = spawns;
    }

    public string Fingerprint { get; }
    // 0은 방장, 1은 참가자의 스폰 자리.
    public Pose[] Spawns { get; }
    public int PlayerCount => Players.Count;

    internal readonly List<NetworkObject> Players = new List<NetworkObject>();
    internal readonly List<DuelPlayer> Duelists = new List<DuelPlayer>();
    internal Coroutine Combat;
    internal Action<int> PlayerDefeated;
}

// Main의 전투 맵과 플레이어. 서버와 두 클라이언트가 방에 저장된 설정으로 같은 자리에 맵을 만들고 지문을 비교한다.
// 화면 없는 서버에서도 맵을 만들어야 하므로 LobbyNetwork의 clientPresentation에 넣지 않는다.
// 클라이언트는 자기 방의 맵 하나를 Build로 세워 두고, 서버는 방마다 Prepare로 DuelMatch를 만들어 쓴다.
public sealed class DuelArena : MonoBehaviour
{
    [SerializeField] ProceduralMap map;
    [Tooltip("전투 맵이 갱신되면 같이 갱신할 배경. Main 씬 Duel Arena 하위에 두고 map을 전투맵으로 지정한다.")]
    [SerializeField] ArenaBackgroundBuilderNew background;
    [Tooltip("0은 방장(파랑), 1은 참가자(빨강).")]
    [SerializeField] NetworkObject[] playerPrefabs;
    [Tooltip("스폰 이펙트가 끝난 뒤, 소환 메시지가 클라이언트에 닿도록 전투 입력을 켜기 전에 더 기다리는 시간.")]
    [SerializeField, Min(0f)] float combatDelayPadding = 0.15f;
    [SerializeField] WeaponSpawnCatalog weaponCatalog;
    public ArenaWeaponSpawner Weapons { get; private set; }

    public void RegisterWeaponMessages(NetworkManager manager)
    {
        if (Weapons == null) Weapons = gameObject.AddComponent<ArenaWeaponSpawner>();
        Weapons.Initialize(manager, weaponCatalog);
    }

    public void UnregisterWeaponMessages() => Weapons?.Shutdown();

    public bool HasMap => map.GeneratedLayout != null;
    public string Fingerprint { get; private set; }

    // 이전 맵을 지우고 설정대로 새로 만든다. 만들 수 없는 설정이면 빈 상태로 두고 false를 돌려준다.
    public bool Build(MapGenerationSettings settings)
    {
        if (map == null) return false;
        if (background == null) background = GetComponentInChildren<ArenaBackgroundBuilderNew>(true);
        // 전투 배경을 먼저 켠다. 꺼진 채로 map.Generate가 Rebuild하면 자식이 비활성으로 만들어져서 다시 갱신이 필요하다.
        if (background != null && !background.gameObject.activeInHierarchy)
            background.gameObject.SetActive(true);
        try
        {
            map.Generate(settings);
            Fingerprint = MapFingerprint.Compute(map.GeneratedLayout, map.SpawnPoints);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Arena] Could not build seed={settings.Seed}: {exception.Message}");
            Clear();
            return false;
        }

        // 가시성 판정은 지금 싸우는 맵의 부시만 본다. map이 지워지거나 다시 만들어지면 스스로 등록을 푼다.
        BushField.Activate(map.Bushes);
        MapLayout layout = map.GeneratedLayout;
        Debug.Log($"[Arena] Ready settings={JsonUtility.ToJson(new NetworkMapSettings(settings))} shape={layout.Shape} " +
            $"size={layout.Size} spawnA={layout.SpawnA} spawnB={layout.SpawnB} fingerprint={Fingerprint}");
        for (int slot = 0; slot < map.SpawnPoints.Length; slot++)
            Debug.Log($"[Arena] Spawn {slot}: position={map.SpawnPoints[slot].position} rotation={map.SpawnPoints[slot].rotation}");
        return true;
    }

    // 서버에서 방 하나의 판을 준비한다. 씬의 맵을 작업대로 써서 지문과 스폰 자리만 받아 두고 맵은 곧바로 지운다.
    // 다른 방이 같은 프레임에 준비해도 서로의 맵을 건드리지 않는다. 만들 수 없는 설정이면 null이다.
    public DuelMatch Prepare(MapGenerationSettings settings)
    {
        DuelMatch match = PrepareLocal(settings);
        if (match != null) Clear();
        return match;
    }

    // AI 대전의 판을 준비한다. 로컬 호스트가 서버와 클라이언트를 겸하므로 세운 맵을 지우지 않고 그 위에서 싸운다.
    public DuelMatch PrepareLocal(MapGenerationSettings settings)
    {
        if (!Build(settings)) return null;
        var spawns = new Pose[map.SpawnPoints.Length];
        for (int slot = 0; slot < spawns.Length; slot++)
            spawns[slot] = new Pose(map.SpawnPoints[slot].position, map.SpawnPoints[slot].rotation);
        return new DuelMatch(Fingerprint, spawns);
    }

    // 서버에서 방장과 참가자를 각자의 스폰 위치에 소환하고, 스폰 이펙트가 끝나면 전투 입력을 켠다.
    // 두 플레이어는 이 방의 두 클라이언트에게만 보인다. playerDefeated는 먼저 쓰러진 플레이어의 슬롯으로 한 번만 불린다.
    public void SpawnPlayers(DuelMatch match, ulong host, ulong guest, Action combatEnabled, Action<int> playerDefeated)
    {
        match.PlayerDefeated = playerDefeated;
        ulong[] owners = { host, guest };
        var spawned = new DuelPlayer[owners.Length];
        for (int slot = 0; slot < owners.Length; slot++)
        {
            NetworkObject player = Create(match, slot, out spawned[slot]);
            // Netcode는 플레이어 오브젝트끼리는 가시성 판정 없이 서로의 관찰자로 넣는다. 자동 관찰자를 끄고
            // 소환한 뒤 이 방의 두 명에게만 보여 준다. 나중에 접속한 클라이언트의 동기화와 공격 RPC는 이 판정을 따른다.
            player.SpawnWithObservers = false;
            player.CheckObjectVisibility = clientId => clientId == host || clientId == guest;
            player.SpawnAsPlayerObject(owners[slot]);
            player.NetworkShow(host);
            player.NetworkShow(guest);
            Debug.Log($"[Arena] Player-{slot + 1} owner={owners[slot]} position={player.transform.position} rotation={player.transform.rotation}");
        }
        match.Combat = StartCoroutine(EnableCombat(match, spawned, combatEnabled));
    }

    // AI 대전: 로컬 호스트가 방장 자리(슬롯 0)의 내 플레이어와 참가자 자리(슬롯 1)의 봇을 모두 소유한다.
    // 봇은 플레이어 오브젝트가 아니어서 카메라와 부시 가시성 판정은 내 플레이어가 맡고, 입력 대신 DuelBot이 움직인다.
    // PrepareLocal로 세운 맵이 그대로 있어야 한다. 봇은 그 맵의 격자로 길을 찾는다.
    public void SpawnAgainstBot(DuelMatch match, ulong owner, DuelBotTuning tuning, Action combatEnabled, Action<int> playerDefeated)
    {
        match.PlayerDefeated = playerDefeated;
        var spawned = new DuelPlayer[2];
        NetworkObject me = Create(match, 0, out spawned[0]);
        NetworkObject opponent = Create(match, 1, out spawned[1]);
        DuelBot bot = opponent.gameObject.AddComponent<DuelBot>();
        bot.Initialize(new BotGrid(map.GeneratedLayout, map.transform), spawned[0], tuning);
        spawned[1].AttachBot(bot);
        me.SpawnAsPlayerObject(owner);
        opponent.SpawnWithOwnership(owner);
        Debug.Log($"[Arena] Player-1 owner={owner} position={me.transform.position} rotation={me.transform.rotation}");
        Debug.Log($"[Arena] Player-2 bot owner={owner} position={opponent.transform.position} rotation={opponent.transform.rotation}");
        match.Combat = StartCoroutine(EnableCombat(match, spawned, combatEnabled));
    }

    // 슬롯의 프리팹을 그 판의 스폰 자리에 만들고 판에 넣는다. 소환은 부른 쪽에서 한다.
    NetworkObject Create(DuelMatch match, int slot, out DuelPlayer duelist)
    {
        Pose spawn = match.Spawns[slot];
        NetworkObject player = Instantiate(playerPrefabs[slot], spawn.position, spawn.rotation);
        duelist = player.GetComponent<DuelPlayer>();
        if (duelist != null)
        {
            duelist.Match = match;
            duelist.Defeated += defeated => OnPlayerDefeated(match, defeated);
            match.Duelists.Add(duelist);
        }
        match.Players.Add(player);
        return player;
    }

    void OnPlayerDefeated(DuelMatch match, DuelPlayer player)
    {
        Debug.Log($"[Arena] Player-{player.Slot + 1} defeated owner={player.OwnerClientId} position={player.transform.position}");
        Action<int> playerDefeated = match.PlayerDefeated;
        match.PlayerDefeated = null;
        playerDefeated?.Invoke(player.Slot);
    }

    IEnumerator EnableCombat(DuelMatch match, DuelPlayer[] spawned, Action combatEnabled)
    {
        float duration = 0f;
        foreach (DuelPlayer player in spawned)
            if (player != null) duration = Mathf.Max(duration, player.SpawnEffectDuration);
        yield return new WaitForSeconds(duration + combatDelayPadding);
        match.Combat = null;
        foreach (DuelPlayer player in spawned)
            if (player != null) player.ServerEnableCombat();
        // 무기 스폰은 맵이 서 있는 쪽에서만 연다. 전용 서버는 맵을 지문만 뽑고 지우며 방이 여럿이라 판을 열지 않는다.
        if (HasMap) Weapons?.BeginRound(map, match.Duelists);
        combatEnabled?.Invoke();
    }

    // 서버에서 승부가 나면 부른다. 그 방 두 플레이어의 입력을 끄고 플레이어는 결과를 보여 주는 동안 그대로 둔다.
    public void EndCombat(DuelMatch match)
    {
        if (match == null) return;
        if (HasMap) Weapons?.EndRound();
        if (match.Combat != null) StopCoroutine(match.Combat);
        match.Combat = null;
        match.PlayerDefeated = null;
        foreach (DuelPlayer player in match.Duelists)
            if (player != null) player.ServerDisableCombat();
    }

    // 서버에서 그 방이 소환한 플레이어를 정리한다. 방이 닫힐 때와 결과를 보여 준 뒤 다음 판으로 넘어갈 때 부른다.
    public void Clear(DuelMatch match)
    {
        if (match == null) return;
        if (HasMap) Weapons?.Clear();
        if (match.Combat != null) StopCoroutine(match.Combat);
        match.Combat = null;
        match.PlayerDefeated = null;
        match.Duelists.Clear();
        foreach (NetworkObject player in match.Players)
        {
            if (player == null) continue;
            Debug.Log($"[Arena] Removing {player.name} owner={player.OwnerClientId} position={player.transform.position}");
            if (player.IsSpawned) player.Despawn();
            else Destroy(player.gameObject);
        }
        match.Players.Clear();
    }

    // 맵을 비운다. 클라이언트는 시작 화면이나 로비 화면으로 돌아갈 때 부른다. 소환된 플레이어는 서버가 디스폰한다.
    public void Clear()
    {
        if (map != null) map.Clear();
        Fingerprint = null;
        // 다음 판이 빈 배경에서 시작하도록 전투 배경도 비운다. map이 비었으므로 FarGround만 남는다.
        // 클라는 시작 화면(미리보기 맵)으로 돌아가므로 전투 배경 오브젝트도 끈다.
        if (background == null) background = GetComponentInChildren<ArenaBackgroundBuilderNew>(true);
        if (background != null)
        {
            background.Rebuild();
            background.gameObject.SetActive(false);
        }
    }
}

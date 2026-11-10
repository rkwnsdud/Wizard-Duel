using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Main의 전투 맵과 플레이어. 서버와 두 클라이언트가 방에 저장된 설정으로 같은 자리에 맵을 만들고 지문을 비교한다.
// 화면 없는 서버에서도 맵을 만들어야 하므로 LobbyNetwork의 clientPresentation에 넣지 않는다.
public sealed class DuelArena : MonoBehaviour
{
    [SerializeField] ProceduralMap map;
    [Tooltip("전투 맵이 갱신되면 같이 갱신할 배경. Main 씬 Duel Arena 하위에 두고 map을 전투맵으로 지정한다.")]
    [SerializeField] ArenaBackgroundBuilderNew background;
    [Tooltip("0은 방장(파랑), 1은 참가자(빨강).")]
    [SerializeField] NetworkObject[] playerPrefabs;
    [Tooltip("스폰 이펙트가 끝난 뒤, 소환 메시지가 클라이언트에 닿도록 전투 입력을 켜기 전에 더 기다리는 시간.")]
    [SerializeField, Min(0f)] float combatDelayPadding = 0.15f;

    readonly List<NetworkObject> players = new List<NetworkObject>();
    readonly List<DuelPlayer> duelists = new List<DuelPlayer>();
    Coroutine combat;
    Action<int> playerDefeated;

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

    // 서버에서 방장과 참가자를 각자의 스폰 위치에 소환하고, 스폰 이펙트가 끝나면 전투 입력을 켠다.
    // playerDefeated는 먼저 쓰러진 플레이어의 슬롯으로 한 번만 불린다.
    public void SpawnPlayers(ulong host, ulong guest, Action combatEnabled, Action<int> playerDefeated)
    {
        this.playerDefeated = playerDefeated;
        ulong[] owners = { host, guest };
        var spawned = new DuelPlayer[owners.Length];
        for (int slot = 0; slot < owners.Length; slot++)
        {
            Transform spawn = map.SpawnPoints[slot];
            NetworkObject player = Instantiate(playerPrefabs[slot], spawn.position, spawn.rotation);
            player.SpawnAsPlayerObject(owners[slot]);
            players.Add(player);
            spawned[slot] = player.GetComponent<DuelPlayer>();
            if (spawned[slot] != null)
            {
                spawned[slot].Defeated += OnPlayerDefeated;
                duelists.Add(spawned[slot]);
            }
            Debug.Log($"[Arena] Player-{slot + 1} owner={owners[slot]} position={spawn.position} rotation={spawn.rotation}");
        }
        combat = StartCoroutine(EnableCombat(spawned, combatEnabled));
    }

    void OnPlayerDefeated(DuelPlayer player)
    {
        Debug.Log($"[Arena] Player-{player.Slot + 1} defeated owner={player.OwnerClientId} position={player.transform.position}");
        playerDefeated?.Invoke(player.Slot);
        playerDefeated = null;
    }

    IEnumerator EnableCombat(DuelPlayer[] spawned, Action combatEnabled)
    {
        float duration = 0f;
        foreach (DuelPlayer player in spawned)
            if (player != null) duration = Mathf.Max(duration, player.SpawnEffectDuration);
        yield return new WaitForSeconds(duration + combatDelayPadding);
        combat = null;
        foreach (DuelPlayer player in spawned)
            if (player != null) player.ServerEnableCombat();
        combatEnabled?.Invoke();
    }

    // 서버에서 승부가 나면 부른다. 두 플레이어의 입력을 끄고 플레이어와 맵은 결과를 보여 주는 동안 그대로 둔다.
    public void EndCombat()
    {
        if (combat != null) StopCoroutine(combat);
        combat = null;
        playerDefeated = null;
        foreach (DuelPlayer player in duelists)
            if (player != null) player.ServerDisableCombat();
    }

    // 소환한 플레이어와 맵을 정리한다. 서버는 방이 닫혀 연결을 끊기 전과 결과를 보여 준 뒤 다음 판으로 넘어갈 때,
    // 클라이언트는 시작 화면이나 로비 화면으로 돌아갈 때 부른다.
    public void Clear()
    {
        if (combat != null) StopCoroutine(combat);
        combat = null;
        playerDefeated = null;
        duelists.Clear();
        foreach (NetworkObject player in players)
        {
            if (player == null) continue;
            Debug.Log($"[Arena] Removing {player.name} owner={player.OwnerClientId} position={player.transform.position}");
            if (player.IsSpawned) player.Despawn();
            else Destroy(player.gameObject);
        }
        players.Clear();
        if (map != null) map.Clear();
        Fingerprint = null;
        // 다음 방이 빈 배경에서 시작하도록 전투 배경도 비운다. map이 비었으므로 FarGround만 남는다.
        // 클라는 시작 화면(미리보기 맵)으로 돌아가므로 전투 배경 오브젝트도 끈다.
        if (background == null) background = GetComponentInChildren<ArenaBackgroundBuilderNew>(true);
        if (background != null)
        {
            background.Rebuild();
            background.gameObject.SetActive(false);
        }
    }
}

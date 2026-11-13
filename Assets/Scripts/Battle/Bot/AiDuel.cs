using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

// AI 대전 한 판. 전용 서버에 접속하지 않고 이 클라이언트가 로컬 호스트(서버+클라이언트)가 되어 내 플레이어와 봇을 소환한다.
// 공격·피해·승패는 온라인과 같은 DuelArena·DuelPlayer 코드로 돈다. 맵과 무기·난이도는 AI 로비 화면에서 정해 Begin에 넘긴다.
// 승부가 나면 결과를 보여 줄 시간을 둔 뒤 스스로 정리하고 Ended를 알린다. LobbyNetwork와 같은 NetworkManager를 번갈아 쓴다.
public sealed class AiDuel : MonoBehaviour
{
    [SerializeField] NetworkManager manager;
    [SerializeField] UnityTransport transport;
    [SerializeField] DuelArena arena;
    [Tooltip("승부가 난 뒤 승자를 보여 주는 시간. 지나면 플레이어와 맵을 정리한다.")]
    [SerializeField, Min(0f)] float resultDuration = 3f;
    [Tooltip("난이도별 봇 실력. 순서는 쉬움, 보통, 어려움(BotDifficulty)이다.")]
    [SerializeField] DuelBotTuning[] difficulties = { DuelBotTuning.Easy(), DuelBotTuning.Normal(), DuelBotTuning.Hard() };

    // 전투 맵과 플레이어를 세우는 곳. 카메라가 내 스폰 지점으로 내려갈 때 쓴다.
    public DuelArena Arena => arena;

    // 승부가 났다. 이긴 쪽의 슬롯(0은 나, 1은 봇)을 넘긴다.
    public event Action<int> Finished;
    // 플레이어와 맵을 정리하고 연결을 닫았다. 승부가 나서 끝났든 Stop으로 끝냈든 한 번 불린다.
    public event Action Ended;

    // Begin이 성공한 뒤부터 Ended를 알릴 때까지.
    public bool IsRunning { get; private set; }

    DuelMatch match;
    bool stopping;
    // 0보다 작으면 예약이 없다.
    float stopAt = -1f;

    // 설정대로 전투 맵을 세우고 내 플레이어(슬롯 0)와 봇(슬롯 1)을 고른 무기로 소환한다. 맵을 만들 수 없거나 호스트를 열 수 없으면 false다.
    public bool Begin(MapGenerationSettings settings, AiDuelOptions options)
    {
        if (IsRunning || manager == null || transport == null || arena == null ||
            manager.IsListening || manager.ShutdownInProgress) return false;
        options ??= new AiDuelOptions();
        match = arena.PrepareLocal(settings);
        if (match == null) return false;
        // 루프백의 빈 포트에서만 듣는다. 같은 PC의 전용 서버나 다른 게임 창과 포트가 겹치지 않고, 밖에서는 접속할 수 없다.
        transport.SetConnectionData("127.0.0.1", 0, "127.0.0.1");
        if (!manager.StartHost())
        {
            Debug.LogError("[AiDuel] Could not start a local host.");
            match = null;
            arena.Clear();
            return false;
        }
        IsRunning = true;
        stopping = false;
        stopAt = -1f;
        // 전투 중 무기 스폰과 줍기·교체는 이 메시지로 오간다. 온라인에서는 LobbyNetwork가 걸고, 여기서는 호스트를 연 쪽이 건다.
        // 플레이어가 소환될 때 인벤토리가 이 스포너를 물어 가므로 소환보다 먼저 건다.
        arena.RegisterWeaponMessages(manager);
        arena.SpawnAgainstBot(match, manager.LocalClientId, options.MyWeapon, options.OpponentWeapon,
            Tuning(options.Difficulty), null, OnPlayerDefeated);
        Debug.Log($"[AiDuel] Started seed={settings.Seed} fingerprint={match.Fingerprint} weapon={options.MyWeapon} " +
            $"botweapon={options.OpponentWeapon} difficulty={options.Difficulty}");
        return true;
    }

    // 판을 바로 끝낸다. 정리가 끝나면 Ended가 불린다.
    public void Stop()
    {
        if (!IsRunning || stopping) return;
        stopping = true;
        stopAt = -1f;
        arena.Clear(match);
        match = null;
        arena.UnregisterWeaponMessages();
        if (manager.IsListening && !manager.ShutdownInProgress) manager.Shutdown();
    }

    // 목록이 비어 있거나 짧으면 가까운 난이도나 기본값으로 대신한다.
    DuelBotTuning Tuning(BotDifficulty difficulty)
    {
        if (difficulties == null || difficulties.Length == 0) return DuelBotTuning.Normal();
        return difficulties[Mathf.Clamp((int)difficulty, 0, difficulties.Length - 1)] ?? DuelBotTuning.Normal();
    }

    void OnPlayerDefeated(int slot)
    {
        if (!IsRunning || stopping) return;
        Debug.Log($"[AiDuel] Finished loser={slot} winner={1 - slot}");
        arena.EndCombat(match);
        stopAt = Time.realtimeSinceStartup + resultDuration;
        Finished?.Invoke(1 - slot);
    }

    void Update()
    {
        if (!IsRunning) return;
        if (!stopping)
        {
            // 결과를 다 보여 줬거나, 호스트가 스스로 닫혔으면 정리한다.
            if ((stopAt >= 0f && Time.realtimeSinceStartup >= stopAt) || !manager.IsListening) Stop();
            return;
        }
        if (manager.IsListening || manager.ShutdownInProgress) return;
        stopping = false;
        IsRunning = false;
        // 소환된 플레이어는 위에서 디스폰했다. 연결이 닫힌 프레임에 전투 맵도 지운다.
        arena.Clear();
        Ended?.Invoke();
    }
}

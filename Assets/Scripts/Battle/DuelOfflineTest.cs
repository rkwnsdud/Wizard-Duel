using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// DuelOfflineTest 씬의 시작점. Main의 온라인 전투 스크립트(DuelPlayer, IWeapon 무기, 서버 피해 판정)를
// 로비와 상대 클라이언트 없이 혼자 시험한다. 이 에디터가 로컬 호스트(서버+클라이언트)가 되어
// Main과 같은 DuelWizard 프리팹으로 플레이어와 더미를 스폰하므로, 공격·명중 보고·체력 반영이 온라인과 같은 코드로 돈다.
// 더미도 호스트 소유라서 전투를 켜지 않는다(켜면 DuelPlayer가 내 입력으로 더미까지 움직인다).
public sealed class DuelOfflineTest : MonoBehaviour
{
    [SerializeField] NetworkManager manager;
    [SerializeField] GridMap map;
    [Tooltip("조작하는 플레이어. Main의 방장 프리팹(DuelWizard-1)을 쓴다.")]
    [SerializeField] NetworkObject playerPrefab;
    [Tooltip("맞기만 하는 더미. 슬롯이 플레이어와 달라야 피해가 들어가므로 참가자 프리팹(DuelWizard-2)을 쓴다.")]
    [SerializeField] NetworkObject dummyPrefab;
    [Tooltip("더미가 쓰러진 뒤 다시 세우기까지 기다리는 시간.")]
    [SerializeField, Min(0f)] float dummyRespawnDelay = 1.5f;

    DuelPlayer player;
    DuelPlayer dummy;
    PlayerHealth dummyHealth;
    PlayerBushVisibility dummyVisibility;
    // 더미가 서 있는 부시 덩어리 번호. -1이면 스폰 자리다.
    int dummyBush = -1;
    // 0보다 작으면 예약이 없다.
    float combatAt = -1f;
    float dummyRespawnAt = -1f;
    GUIStyle helpStyle;

    void Start()
    {
        if (!manager.StartHost())
        {
            Debug.LogError("[OfflineTest] Could not start a local host.", this);
            enabled = false;
            return;
        }
        Respawn();
    }

    void Update()
    {
        if (!manager.IsServer) return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.rKey.wasPressedThisFrame) Respawn();
        if (keyboard != null && keyboard.bKey.wasPressedThisFrame) MoveDummyToNextBush();
        // 상대 화면이 더미의 공격 시작(PlayAttackRpc)을 받은 것처럼 부시 노출과 공격 모션을 재생한다. 더미는 전투가 꺼져 있어 쏘지는 않는다.
        if (keyboard != null && keyboard.tKey.wasPressedThisFrame && dummy != null) dummy.SimulateReceivedAttack(0f);
        // Main의 DuelArena처럼 스폰 이펙트가 끝난 뒤 입력을 켠다.
        if (combatAt >= 0f && Time.time >= combatAt)
        {
            combatAt = -1f;
            if (player != null) player.ServerEnableCombat();
        }
        if (dummyRespawnAt >= 0f && Time.time >= dummyRespawnAt) SpawnDummy();
    }

    // 플레이어와 더미를 각자의 스폰 위치에 새로 세운다.
    void Respawn()
    {
        SpawnDummy();
        Despawn(player);
        player = Spawn(playerPrefab, 0);
        player.NetworkObject.SpawnAsPlayerObject(manager.LocalClientId);
        combatAt = Time.time + player.SpawnEffectDuration;
        FollowPlayer();
    }

    void SpawnDummy()
    {
        dummyRespawnAt = -1f;
        if (dummy != null) dummy.Defeated -= OnDummyDefeated;
        Despawn(dummy);
        dummy = Spawn(dummyPrefab, 1);
        dummy.NetworkObject.Spawn();
        dummy.name = "Dummy (Player-2)";
        dummy.Defeated += OnDummyDefeated;
        dummyHealth = dummy.GetComponent<PlayerHealth>();
        dummyVisibility = dummy.GetComponent<PlayerBushVisibility>();
        dummyBush = -1;
        // 상대 화면의 원격 플레이어처럼 맞을 수 있게 콜라이더만 켠다. 이동·조준·공격은 DuelPlayer가 꺼 둔 그대로다.
        dummy.GetComponent<CharacterController>().enabled = true;
        // 더미도 소유자라서 스폰할 때 카메라를 가져가므로 플레이어에게 돌려준다.
        FollowPlayer();
    }

    // 두 스폰 위치가 서로 마주 보도록 세운다. 스폰은 호출한 쪽에서 한다.
    DuelPlayer Spawn(NetworkObject prefab, int spawnIndex)
    {
        Transform spawn = map.SpawnPoints[spawnIndex];
        Vector3 toOther = map.SpawnPoints[1 - spawnIndex].position - spawn.position;
        toOther.y = 0f;
        Quaternion rotation = toOther.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toOther) : spawn.rotation;
        return Instantiate(prefab, spawn.position, rotation).GetComponent<DuelPlayer>();
    }

    static void Despawn(DuelPlayer target)
    {
        if (target != null && target.IsSpawned) target.NetworkObject.Despawn();
    }

    void OnDummyDefeated(DuelPlayer defeated) => dummyRespawnAt = Time.time + dummyRespawnDelay;

    // 더미를 맵의 부시 덩어리 한가운데로 차례로 옮긴다. 마지막 덩어리 다음에는 스폰 자리로 돌아온다.
    void MoveDummyToNextBush()
    {
        BushField bushes = map.Bushes;
        if (dummy == null || bushes == null || bushes.Count == 0) return;
        dummyBush = dummyBush + 1 < bushes.Count ? dummyBush + 1 : -1;
        Vector3 target = dummyBush >= 0 ? bushes.CenterOf(dummyBush) : map.SpawnPoints[1].position;
        // CharacterController가 켜져 있으면 옮긴 위치를 되돌릴 수 있어 잠깐 끈다.
        var controller = dummy.GetComponent<CharacterController>();
        controller.enabled = false;
        dummy.transform.position = new Vector3(target.x, dummy.transform.position.y, target.z);
        controller.enabled = true;
    }

    // Main에서 TitleBackdrop.ReleaseCamera가 하듯 전투 투영·안개를 같은 에셋으로 맞추고 플레이어를 따라가게 한다.
    void FollowPlayer()
    {
        if (player == null || Camera.main == null || !Camera.main.TryGetComponent(out CameraFollow follow)) return;
        if (follow.Settings != null) follow.Settings.Apply(Camera.main);
        follow.player = player.transform;
        follow.enabled = true;
    }

    void OnGUI()
    {
        helpStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.UpperLeft, padding = new RectOffset(12, 12, 8, 8) };
        string health = dummyHealth != null ? $"{dummyHealth.CurrentHealth:0.#} / {dummyHealth.MaxHealth:0.#}" : "-";
        string look = dummyVisibility != null ? dummyVisibility.Look.ToString() : "-";
        string place = dummyBush >= 0 ? $"부시 {dummyBush}" : "스폰";
        GUI.Box(new Rect(16f, 16f, 600f, 92f),
            $"온라인 전투 스크립트 오프라인 테스트\n더미 체력 {health}    더미 위치 {place} ({look})\n" +
            "R: 다시 세우기    B: 더미를 다음 부시로    T: 더미 공격(부시 노출)", helpStyle);
    }
}

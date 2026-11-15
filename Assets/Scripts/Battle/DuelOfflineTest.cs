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
    [Tooltip("무기 변경 패널에 더할 무기 프리팹. 플레이어를 세울 때마다 WeaponSocket에 꺼진 채로 넣는다(같은 이름이 이미 있으면 건너뛴다). " +
             "IWeapon이 아닌 무기는 그 무기를 든 동안만 무기 자체의 오프라인 판정으로 쏜다.")]
    [SerializeField] WeaponBase[] extraWeapons = System.Array.Empty<WeaponBase>();

    DuelPlayer player;
    DuelPlayer dummy;
    PlayerHealth dummyHealth;
    PlayerBushVisibility dummyVisibility;
    // 더미가 서 있는 부시 덩어리 번호. -1이면 스폰 자리다.
    int dummyBush = -1;
    // 프리팹에 저장된 더미 레이어. 오프라인 판정 무기를 든 동안만 Player 레이어로 바꾼다.
    int dummyLayer;
    // 0보다 작으면 예약이 없다.
    float combatAt = -1f;
    float dummyRespawnAt = -1f;
    GUIStyle helpStyle;
    // 씬에 놓인 태양 방향. 다시 세울 때마다 이 방향에서 두 스폰을 잇는 선에 직교하게 돌린다.
    Quaternion? sunHome;

    // 도움말 상자 아래 무기 변경 패널. 플레이어 WeaponSocket에 들어 있는 무기(꺼진 예비 무기 포함)를 고른다.
    const float WeaponRowHeight = 36f;
    PlayerAttack playerAttack;
    WeaponBase[] weapons = System.Array.Empty<WeaponBase>();
    // 고른 무기의 오브젝트 이름. R로 다시 세워도 같은 무기를 들고, 공격 중에 고르면 공격이 끝난 뒤 바꾼다.
    // null이면 프리팹의 기본 무기를 그대로 든다.
    string selectedWeapon;
    GUIStyle weaponButtonStyle;
    // 도움말 상자(y 16, 높이 92) 바로 아래에 둔다.
    Rect WeaponPanelRect => new Rect(16f, 116f, 440f, 46f + weapons.Length * (WeaponRowHeight + 4f));

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
        UpdateWeaponSwitch();
    }

    // 숫자 키나 패널 버튼으로 고른 무기를 TryEquipWeapon으로 장착한다. 호스트 혼자이므로 로컬 장착이 곧 전체 상태다.
    void UpdateWeaponSwitch()
    {
        if (playerAttack == null) return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
            for (int i = 0; i < weapons.Length && i < 9; i++)
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame) selectedWeapon = weapons[i].name;

        // 패널 버튼을 누르는 왼쪽 클릭이 공격으로 나가면 공격 중이라 장착이 거절되므로, 패널 위에서는 공격 입력을 막는다.
        // 입력 이벤트는 다음 프레임 Update 전에 처리되므로 이번 프레임의 마우스 위치로 미리 정해 둔다.
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            Vector2 position = mouse.position.ReadValue();
            playerAttack.AttackInputBlocked = WeaponPanelRect.Contains(new Vector2(position.x, Screen.height - position.y));
        }

        EquipSelectedWeapon();
        SyncFireMode();
    }

    void EquipSelectedWeapon()
    {
        if (selectedWeapon == null || (playerAttack.CurrentWeapon != null && playerAttack.CurrentWeapon.name == selectedWeapon)) return;
        WeaponBase target = System.Array.Find(weapons, weapon => weapon != null && weapon.name == selectedWeapon);
        if (target == null)
        {
            selectedWeapon = null;
            return;
        }
        // 공격·차징 중이거나 쓰러져 있으면 거절되므로 다음 프레임에 다시 시도한다.
        if (playerAttack.TryEquipWeapon(target))
            Debug.Log($"[OfflineTest] Equipped {target.name}{(target is IWeapon ? "" : " (offline fire)")}", this);
    }

    // DuelPlayer는 IWeapon이 아닌 무기의 발사를 건너뛴다. 그런 무기를 든 동안은 Authority를 비워
    // PlayerAttack이 무기의 Fire()를 직접 부르게 한다(피해는 서버를 거치지 않고 이 화면에서만 들어간다).
    // 그런 무기의 투사체는 Player 레이어만 맞히는데 대전 캐릭터는 Default 레이어라서, 그동안만 더미를 Player 레이어로 옮긴다.
    // 온라인 무기는 Main과 같은 Authority·레이어로 시험한다. 다시 세우면 DuelPlayer가 Authority를 넣으므로 매 프레임 맞춘다.
    void SyncFireMode()
    {
        bool online = playerAttack.CurrentWeapon is IWeapon;
        playerAttack.Authority = online ? player : null;
        if (dummy != null) dummy.gameObject.layer = online ? dummyLayer : LayerMask.NameToLayer("Player");
    }

    // 꺼진 부모 아래에서 만들어, 소켓에 붙기 전에 무기의 Awake·OnEnable이 돌지 않게 한다.
    void AddExtraWeapons()
    {
        Transform socket = playerAttack.CurrentWeapon != null ? playerAttack.CurrentWeapon.transform.parent : null;
        if (socket == null) return;
        var holder = new GameObject("Offline test weapon holder");
        holder.SetActive(false);
        foreach (WeaponBase prefab in extraWeapons)
        {
            if (prefab == null || socket.Find(prefab.name) != null) continue;
            WeaponBase weapon = Instantiate(prefab, holder.transform, false);
            weapon.name = prefab.name;
            weapon.gameObject.SetActive(false);
            weapon.transform.SetParent(socket, false);
            // 씬에 놓였던 자리를 루트에 그대로 저장한 프리팹(화염 향로)은 손에서 수십 m 떨어지므로 소켓 원점에 둔다.
            if (weapon.transform.localPosition.sqrMagnitude > 1f)
                weapon.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        }
        Destroy(holder);
    }

    // 플레이어와 더미를 각자의 스폰 위치에 새로 세운다.
    void Respawn()
    {
        SpawnDummy();
        Despawn(player);
        player = Spawn(playerPrefab, 0);
        player.NetworkObject.SpawnAsPlayerObject(manager.LocalClientId);
        playerAttack = player.GetComponent<PlayerAttack>();
        AddExtraWeapons();
        weapons = playerAttack.GetComponentsInChildren<WeaponBase>(true);

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
        dummyLayer = dummy.gameObject.layer;
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

    void OnDummyDefeated(DuelPlayer defeated)
    {
        // 사망 모션(mixamo.com, 약 4.38초)을 끝까지 보여 주고 리스폰한다.
        // 씬에 저장된 dummyRespawnDelay가 모션보다 짧으면 모션 길이에 맞춘다.
        float delay = dummyRespawnDelay;
        if (defeated != null && defeated.TryGetComponent(out PlayerHealth health))
            delay = Mathf.Max(delay, health.DeathDuration + 0.5f);
        dummyRespawnAt = Time.time + delay;
    }

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
        if (follow.Settings != null)
        {
            follow.Settings.Apply(Camera.main);
            // Main의 방장(파랑)처럼 더미가 있는 상대 스폰 쪽을 화면 위로 둔다.
            if (follow.Settings.TryFacingYaw(map.SpawnPoints, player.Slot, out float yaw)) follow.Yaw = yaw;
        }
        // Main 전투처럼 광원을 두 스폰을 잇는 선에 직교하게 돌린다.
        Light sun = RenderSettings.sun;
        if (sun != null && DuelAxis.TryYaw(map.SpawnPoints, out float axis))
        {
            sunHome ??= sun.transform.rotation;
            sun.transform.rotation = DuelAxis.SideLight(axis, sunHome.Value);
        }
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
        DrawWeaponPanel();
    }

    void DrawWeaponPanel()
    {
        if (playerAttack == null || weapons.Length == 0) return;
        weaponButtonStyle ??= new GUIStyle(GUI.skin.button)
        {
            fontSize = 17, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(12, 12, 4, 4)
        };
        Rect panel = WeaponPanelRect;
        GUI.Box(panel, $"무기 변경    숫자 키 1~{Mathf.Min(weapons.Length, 9)}", helpStyle);
        for (int i = 0; i < weapons.Length; i++)
        {
            WeaponBase weapon = weapons[i];
            if (weapon == null) continue;
            bool equipped = playerAttack.CurrentWeapon == weapon;
            // 공격 중에 고르면 끝난 뒤 바뀌므로 그동안 대기로 보여 준다.
            string state = equipped ? "    장착 중" : weapon.name == selectedWeapon ? "    대기" : "";
            Rect row = new Rect(panel.x + 8f, panel.y + 40f + i * (WeaponRowHeight + 4f), panel.width - 16f, WeaponRowHeight);
            Color previous = GUI.backgroundColor;
            if (equipped) GUI.backgroundColor = new Color(1f, 0.7f, 0.3f);
            if (GUI.Button(row, $"{i + 1}. {WeaponLabel(weapon)}{state}", weaponButtonStyle)) selectedWeapon = weapon.name;
            GUI.backgroundColor = previous;
        }
    }

    static string WeaponLabel(WeaponBase weapon)
    {
        string label = weapon switch
        {
            DuelLightningWeapon => "번개 지팡이",
            ExplosionMagicWeapon => "폭발 마법 책",
            FireCenserWeapon => "화염 향로",
            LightOrbWeapon => "빛 반사 구슬",
            BasicWandWeapon => "기본 완드",
            _ => weapon.name,
        };
        return weapon is IWeapon ? label : label + " · 오프라인 판정";
    }
}

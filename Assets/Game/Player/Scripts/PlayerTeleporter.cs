using System;
using UnityEngine;

// 플레이어가 텔레포트 타일 가운데에 닿으면 서버에 이동을 요청하고, 승인되면 짝 타일 가운데로 옮긴다.
// 이동 동기화가 소유자 권한이라 요청과 이동은 소유자 화면(내 캐릭터, 호스트의 봇)에서만 한다.
// 도착한 뒤에는 모든 타일 밖으로 한 번 나가야 다시 쓸 수 있다. 그래서 도착한 타일에서 바로 되돌아가지 않는다.
// 이동(PlayerMovement, 순서 10)이 끝난 뒤의 자리로 판정한다.
[DefaultExecutionOrder(20)]
[DisallowMultipleComponent]
public sealed class PlayerTeleporter : MonoBehaviour
{
    // 타일 가운데에서 이 거리(m) 안에 들어오면 이동을 요청한다.
    public const float TriggerRadius = 0.35f;

    // 이 화면에서 이 플레이어가 순간 이동한 직후. 이동 전 자리를 넘긴다. 소유자 화면에서만 불린다.
    public event Action<PlayerTeleporter, Vector3> Teleported;

    DuelPlayer player;
    CharacterController controller;
    PlayerMotor motor;

    // false면 타일 위에 도착한 뒤 아직 타일 밖으로 나가지 않았다.
    public bool Armed { get; private set; } = true;

    void Awake()
    {
        player = GetComponent<DuelPlayer>();
        controller = GetComponent<CharacterController>();
        motor = GetComponent<PlayerMotor>();
    }

    void OnEnable() => Armed = true;

    void Update()
    {
        TeleporterField field = TeleporterField.Active;
        ArenaTeleporters network = ArenaTeleporters.Instance;
        if (field == null || field.Count == 0 || network == null || player == null || !player.IsSpawned || !player.IsOwner) return;
        Vector3 position = transform.position;
        bool onTile = field.TryFind(position, out int pair, out int side, out float distance);
        if (!Armed)
        {
            if (!onTile) Armed = true;
            return;
        }
        if (!onTile || distance > TriggerRadius || !player.CanUseInventory || controller == null || !controller.enabled) return;
        network.TryRequest(this, pair, side);
    }

    // 서버가 이동을 승인했다. destination(바닥 높이의 타일 가운데)으로 옮기고, 이 캐릭터를 따라가는 카메라가 뒤쫓게 한다.
    public void Arrive(Vector3 destination)
    {
        Vector3 from = transform.position;
        Vector3 to = new Vector3(destination.x, from.y, destination.z);
        Armed = false;
        bool wasEnabled = controller != null && controller.enabled;
        if (controller != null) controller.enabled = false;
        transform.position = to;
        if (controller != null) controller.enabled = wasEnabled;
        Physics.SyncTransforms();
        if (motor != null) motor.ResetFall();

        Camera main = Camera.main;
        if (main != null && main.TryGetComponent(out CameraFollow follow) && follow.player == transform)
            follow.ChaseFrom(from);
        Debug.Log($"[Teleport] {name} arrived from={from} to={to}");
        Teleported?.Invoke(this, from);
    }
}

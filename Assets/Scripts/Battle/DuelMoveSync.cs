using Unity.Netcode;
using UnityEngine;

// 소유자가 보내는 이동 상태 하나. 시각은 보낸 클라이언트의 프레임 시각이다.
public struct DuelMoveState : INetworkSerializable
{
    public double Time;
    public Vector3 Position;
    // 수평 속도(x, z). 받는 쪽이 다음 상태가 올 때까지 이어 그리는 데 쓴다.
    public Vector2 Velocity;
    public float Yaw;

    public bool IsFinite => IsFiniteValue(Time) && IsFiniteValue(Position.x) && IsFiniteValue(Position.y) &&
        IsFiniteValue(Position.z);

    static bool IsFiniteValue(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Time);
        serializer.SerializeValue(ref Position);
        // 속도는 mm/s, 방향은 360/65536도 단위로 줄여 보낸다.
        short velocityX = 0, velocityZ = 0;
        ushort yaw = 0;
        if (serializer.IsWriter)
        {
            velocityX = (short)Mathf.Clamp(Mathf.RoundToInt(Velocity.x * 1000f), short.MinValue, short.MaxValue);
            velocityZ = (short)Mathf.Clamp(Mathf.RoundToInt(Velocity.y * 1000f), short.MinValue, short.MaxValue);
            yaw = (ushort)(Mathf.RoundToInt(Mathf.Repeat(Yaw, 360f) * (65536f / 360f)) & 0xFFFF);
        }
        serializer.SerializeValue(ref velocityX);
        serializer.SerializeValue(ref velocityZ);
        serializer.SerializeValue(ref yaw);
        if (serializer.IsReader)
        {
            Velocity = new Vector2(velocityX / 1000f, velocityZ / 1000f);
            Yaw = yaw * (360f / 65536f);
        }
    }
}

// Main 전투 플레이어의 이동 동기화. NetworkTransform 대신 쓴다.
// 소유자는 움직인 그 프레임에 위치·속도·방향을 서버로 보내고, 서버는 받은 프레임에 같은 방의 상대에게 그대로 넘긴다.
// 상대 클라이언트는 MovePlayback으로 그릴 자리를 정한다. 틱을 기다렸다 보내고 몇 틱 뒤의 자리를 보간해 그리던 지연이 없다.
// 순서를 보장하지 않는 전달을 쓴다. 하나를 잃어도 다음 프레임 것이 바로 오므로, 다시 보내느라 뒤따르는 상태가 밀리지 않는다.
[DefaultExecutionOrder(-100)]
public sealed class DuelMoveSync : NetworkBehaviour
{
    // 가만히 서 있을 때 보내는 간격. 마지막 상태를 잃었을 때 바로잡고, 받는 쪽이 지연을 계속 재게 한다.
    const double IdleSendInterval = 0.1;
    // 멈춘 뒤 같은 상태를 더 보내는 횟수. 멈췄다는 패킷 하나를 잃어도 다음 것으로 멈춘다.
    const int StopResends = 2;

    [Tooltip("움직이는 동안 1초에 보내는 횟수의 상한. 화면이 초당 60프레임이면 60에서 매 프레임 보낸다. " +
        "낮추면 서버 트래픽이 줄고, 방향 전환·정지가 상대 화면에 그만큼 늦게 보인다.")]
    [SerializeField, Range(10f, 120f)] float sendRate = 60f;
    [Tooltip("다음 상태가 늦을 때 마지막 속도로 이어 그리는 최대 시간(초). 넘으면 그 자리에 세운다.")]
    [SerializeField, Min(0f)] float maxExtrapolation = 0.12f;
    [Tooltip("이어 그린 자리와 실제 자리의 차이를 푸는 시간 상수(초). 작을수록 빨리 맞추고 클수록 부드럽다.")]
    [SerializeField, Min(0.001f)] float correctionTime = 0.06f;
    [Tooltip("늦게 도착한 방향 변화를 푸는 시간 상수(초).")]
    [SerializeField, Min(0.001f)] float yawCorrectionTime = 0.03f;
    [Tooltip("자리 차이가 이보다 크면 풀지 않고 바로 옮긴다(m).")]
    [SerializeField, Min(0f)] float snapDistance = 2f;
    [Tooltip("가장 빨리 도착한 상태보다 더 늦춰 그리는 여유(초). 0이면 가장 빠르고, 키우면 지연이 늘지만 정지할 때 덜 넘어간다.")]
    [SerializeField, Min(0f)] float delayMargin = 0f;

    MovePlayback playback;
    // 소유자: 마지막으로 보낸 상태와 직전 프레임의 자리.
    DuelMoveState sent;
    bool hasSent;
    int resendsLeft;
    Vector3 previousPosition;
    // 서버: 마지막으로 넘긴 상태의 시각. 순서가 뒤바뀐 패킷을 버린다.
    double relayedTime = double.NegativeInfinity;

    public override void OnNetworkSpawn()
    {
        playback = new MovePlayback(maxExtrapolation, correctionTime, yawCorrectionTime, snapDistance, delayMargin);
        playback.Reset(transform.position, transform.eulerAngles.y);
        previousPosition = transform.position;
        hasSent = false;
        resendsLeft = 0;
        relayedTime = double.NegativeInfinity;
        // 전용 서버는 그릴 것이 없다. 받은 상태는 RPC에서 바로 넘기고 Transform에 넣는다.
        enabled = IsClient;
    }

    /// <summary>상대 클라이언트에서 지금 그리는 이동의 속도. 내가 소유했거나 아직 받은 상태가 없으면 false다.</summary>
    public bool TryGetRemoteVelocity(out Vector3 velocity)
    {
        velocity = Vector3.zero;
        if (!IsSpawned || IsOwner || playback == null || !playback.HasSamples) return false;
        velocity = playback.Velocity;
        return true;
    }

    // 이동(PlayerMovement, DuelBot)과 조준이 모두 Update에서 끝난 뒤, 다른 LateUpdate(걷기 애니메이션, 부시 가시성)보다 먼저 돈다.
    void LateUpdate()
    {
        if (!IsSpawned) return;
        if (IsOwner) SendOwnState();
        else if (playback.Advance(Time.unscaledTimeAsDouble, Time.unscaledDeltaTime, out Vector3 position, out float yaw))
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
    }

    void SendOwnState()
    {
        double now = Time.unscaledTimeAsDouble;
        float deltaTime = Time.unscaledDeltaTime;
        Vector3 position = transform.position;
        Vector3 moved = position - previousPosition;
        previousPosition = position;
        // 호스트 혼자 하는 판(AI 대전, 오프라인 테스트)에는 받을 클라이언트가 없다.
        if (!HasAudience()) return;

        // 한 프레임에 1m 넘게 옮겨졌으면 걸은 것이 아니라 자리를 옮긴 것이다. 그 속도로 이어 그리지 않게 0으로 보낸다.
        bool walked = deltaTime > 0f && moved.sqrMagnitude < 1f;
        var state = new DuelMoveState
        {
            Time = now,
            Position = position,
            Velocity = walked ? new Vector2(moved.x, moved.z) / deltaTime : Vector2.zero,
            Yaw = transform.eulerAngles.y
        };
        // 1mm, 1cm/s, 0.05도보다 작은 차이는 서 있는 것으로 본다.
        bool changed = !hasSent || (state.Position - sent.Position).sqrMagnitude > 1e-6f ||
            (state.Velocity - sent.Velocity).sqrMagnitude > 1e-4f || Mathf.Abs(Mathf.DeltaAngle(state.Yaw, sent.Yaw)) > 0.05f;
        double since = hasSent ? now - sent.Time : double.PositiveInfinity;
        // 프레임 간격은 조금씩 흔들린다. 상한과 같은 속도로 도는 화면이 프레임을 건너뛰지 않게 간격에 여유를 둔다.
        double minInterval = 1.0 / (sendRate * 1.08);
        if (changed)
        {
            if (since < minInterval) return;
            resendsLeft = StopResends;
        }
        else if (resendsLeft > 0)
        {
            if (since < minInterval) return;
            resendsLeft--;
        }
        else if (since < IdleSendInterval) return;

        sent = state;
        hasSent = true;
        if (IsServer) Relay(state);
        else SubmitRpc(state);
    }

    bool HasAudience()
    {
        if (!IsServer) return true;
        var observers = NetworkObject.GetObservers();
        while (observers.MoveNext())
            if (observers.Current != NetworkManager.LocalClientId) return true;
        return false;
    }

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
    void SubmitRpc(DuelMoveState state)
    {
        if (!state.IsFinite || state.Time <= relayedTime) return;
        relayedTime = state.Time;
        // 서버에는 여러 방의 플레이어가 함께 있다. 이 플레이어를 보는 클라이언트(같은 방의 상대)에게만 넘긴다.
        Relay(state);
        // 서버도 최신 자리를 알고 있게 한다. 호스트가 화면에 그리는 경우에는 다른 클라이언트처럼 재생한다.
        if (IsClient) Receive(state);
        else transform.SetPositionAndRotation(state.Position, Quaternion.Euler(0f, state.Yaw, 0f));
    }

    void Relay(DuelMoveState state)
    {
        var observers = NetworkObject.GetObservers();
        while (observers.MoveNext())
        {
            ulong clientId = observers.Current;
            if (clientId == OwnerClientId || clientId == NetworkManager.ServerClientId) continue;
            ReceiveRpc(state, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }
    }

    [Rpc(SendTo.SpecifiedInParams, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Server)]
    void ReceiveRpc(DuelMoveState state, RpcParams rpcParams)
    {
        if (!IsOwner && state.IsFinite) Receive(state);
    }

    void Receive(DuelMoveState state)
    {
        playback?.Push(state.Time, Time.unscaledTimeAsDouble, state.Position,
            new Vector3(state.Velocity.x, 0f, state.Velocity.y), state.Yaw);
    }
}

using System;
using UnityEngine;

// 상대 클라이언트가 받은 이동 표본으로 이 화면에 그 플레이어를 어디에 그릴지 정한다. 네트워크와 Transform은 모른다.
// 표본을 쌓아 두고 늦게 재생하지 않는다. 가장 빨리 도착한 표본을 기준으로 "지금 보여 줄 시각"을 잡고,
// 그 시각의 표본이 아직 오지 않았으면 마지막 속도로 잠깐 이어 그린다(방향도 돌던 빠르기로 잠깐 이어 돌린다).
// 그래서 걷거나 도는 동안에는 도착 간격이 흔들려도 고르게 움직인다.
// 예측이 빗나가면(정지·방향 전환·늦은 패킷) 어긋난 만큼을 짧은 시간에 풀어 순간이동처럼 보이지 않게 한다.
public sealed class MovePlayback
{
    const int Capacity = 16;
    // 도착 지연의 바닥값을 찾는 구간 길이. 두 구간(최근 1~2초)의 최솟값을 쓴다.
    const double WindowSeconds = 1.0;
    // 표시 시각을 바닥값에 맞춰 옮기는 속도. 0.1이면 재생이 최대 10% 빨라지거나 느려진다.
    const double MaxWarp = 0.1;
    // 도착 지연이 한 번에 이만큼 넘게 달라지면 보낸 쪽 시계가 건너뛴 것으로 보고 새로 시작한다.
    const double ClockJump = 0.5;
    // 방향은 속도를 따로 받지 않고 이어진 두 표본의 차이로 도는 빠르기를 구한다.
    // 두 표본이 이보다 떨어져 있으면(서 있다가 막 돌기 시작한 경우) 빠르기를 알 수 없으니 이어 돌리지 않는다.
    const double YawRateGap = 0.05;
    // 마우스를 휙 돌린 직후에 표본이 늦으면 그 빠르기로 더 돌아가 버린다. 이어 돌리는 빠르기(도/초)와 시간(초)을 묶어 둔다.
    const float MaxYawRate = 720f;
    const double MaxYawExtrapolation = 0.05;

    struct Sample
    {
        public double Time;
        public Vector3 Position;
        public Vector3 Velocity;
        public float Yaw;
        public float YawRate;
    }

    readonly Sample[] samples = new Sample[Capacity];
    readonly float maxExtrapolation;
    readonly float correctionTime;
    readonly float yawCorrectionTime;
    readonly float snapDistance;
    readonly double margin;

    int count;
    Vector3 restPosition;
    float restYaw;
    // 표시 시각 = 받는 쪽 시계 - delay. 보낸 쪽 시계와의 차이와 전송 지연을 함께 담는다.
    double delay;
    bool hasDelay;
    double bucketStart;
    double bucketMin;
    double previousMin;
    double displayTime;
    bool displayed;
    // 마지막 표본을 받은 시각(받는 쪽 시계).
    double lastReceived;
    Vector3 correction;
    float yawCorrection;
    // 마지막으로 그린 자리와 방향.
    Vector3 lastPosition;
    float lastYaw;

    /// <param name="maxExtrapolation">마지막 표본 뒤로 속도를 따라 이어 그리는 최대 시간(초). 넘으면 그 자리에 세운다.</param>
    /// <param name="correctionTime">예측과 실제의 위치 차이를 푸는 시간 상수(초).</param>
    /// <param name="yawCorrectionTime">늦게 온 방향 변화를 푸는 시간 상수(초).</param>
    /// <param name="snapDistance">차이가 이보다 크면(순간이동) 풀지 않고 바로 옮긴다.</param>
    /// <param name="margin">표시 시각을 바닥값보다 더 늦추는 여유(초). 0이면 가장 빠르다.</param>
    public MovePlayback(float maxExtrapolation, float correctionTime, float yawCorrectionTime, float snapDistance, float margin)
    {
        this.maxExtrapolation = Mathf.Max(0f, maxExtrapolation);
        this.correctionTime = Mathf.Max(0.001f, correctionTime);
        this.yawCorrectionTime = Mathf.Max(0.001f, yawCorrectionTime);
        this.snapDistance = Mathf.Max(0f, snapDistance);
        this.margin = Math.Max(0f, margin);
    }

    // 지금 그리고 있는 구간의 속도. 걷기 애니메이션에 쓴다. 어긋남을 푸는 움직임은 들어 있지 않다.
    public Vector3 Velocity { get; private set; }
    public bool HasSamples => count > 0;
    // 표시 시각이 마지막 표본보다 얼마나 앞서 있는지(초). 음수면 표본 사이를 재생하는 중이다.
    public double Ahead => count > 0 && displayed ? displayTime - samples[count - 1].Time : 0.0;

    // 표본이 오기 전에 서 있을 자리. 소환 직후와 다시 시작할 때 부른다.
    public void Reset(Vector3 position, float yaw)
    {
        count = 0;
        restPosition = lastPosition = position;
        restYaw = lastYaw = yaw;
        hasDelay = false;
        displayed = false;
        correction = Vector3.zero;
        yawCorrection = 0f;
        Velocity = Vector3.zero;
    }

    // sentTime은 보낸 쪽 시계, receivedTime은 받는 쪽 시계다. 이미 받은 것보다 오래된 표본은 버리고 false를 돌려준다.
    public bool Push(double sentTime, double receivedTime, Vector3 position, Vector3 velocity, float yaw)
    {
        if (count > 0 && sentTime <= samples[count - 1].Time) return false;
        double offset = receivedTime - sentTime;
        if (hasDelay && Math.Abs(offset - delay) > ClockJump)
        {
            // 보낸 쪽 시계가 크게 건너뛰었다(창이 멈췄다 돌아온 경우 등). 지금 그린 자리에서 새로 시작한다.
            count = 0;
            hasDelay = false;
            displayed = false;
        }
        TrackDelay(offset, receivedTime);
        lastReceived = receivedTime;

        // 새 표본이 들어와도 방금 그린 자리에서 이어지도록, 직전 표시 시각에서 달라진 만큼을 보정값으로 옮긴다.
        Vector3 before = default;
        float yawBefore = 0f;
        if (displayed) Evaluate(displayTime, out before, out yawBefore, out _, out _);
        if (count == Capacity)
        {
            Array.Copy(samples, 1, samples, 0, Capacity - 1);
            count--;
        }
        float yawRate = 0f;
        if (count > 0 && sentTime - samples[count - 1].Time <= YawRateGap)
        {
            yawRate = Mathf.DeltaAngle(samples[count - 1].Yaw, yaw) / (float)(sentTime - samples[count - 1].Time);
            yawRate = Mathf.Clamp(yawRate, -MaxYawRate, MaxYawRate);
        }
        samples[count++] = new Sample { Time = sentTime, Position = position, Velocity = velocity, Yaw = yaw, YawRate = yawRate };
        if (displayed)
        {
            Evaluate(displayTime, out Vector3 after, out float yawAfter, out _, out _);
            correction += before - after;
            yawCorrection += Mathf.DeltaAngle(yawAfter, yawBefore);
        }
        else
        {
            // 아직 그린 적이 없으면(첫 표본, 시계가 건너뛴 뒤) 마지막으로 서 있던 자리에서 이어 간다.
            correction = lastPosition - position;
            yawCorrection = Mathf.DeltaAngle(yaw, lastYaw);
        }
        if (correction.sqrMagnitude > snapDistance * snapDistance)
        {
            correction = Vector3.zero;
            yawCorrection = 0f;
        }
        return true;
    }

    // 이번 프레임에 그릴 자리와 방향. 표본을 한 번도 받지 못했으면 false를 돌려주고 소환된 자리를 그대로 준다.
    public bool Advance(double now, float deltaTime, out Vector3 position, out float yaw)
    {
        if (!hasDelay)
        {
            position = restPosition;
            yaw = restYaw;
            Velocity = Vector3.zero;
            return false;
        }
        // 지연의 바닥값이 달라지면 표시 시각을 한 번에 옮기지 않고 재생 속도를 조금 바꿔 따라간다.
        double target = Math.Min(bucketMin, previousMin) + margin;
        double step = MaxWarp * Math.Max(0f, deltaTime);
        delay += Math.Max(-step, Math.Min(step, target - delay));

        // 표시 시각은 뒤로 가지 않는다.
        double time = now - delay;
        if (displayed && time < displayTime) time = displayTime;
        displayTime = time;
        displayed = true;

        Evaluate(time, out Vector3 targetPosition, out float targetYaw, out Vector3 velocity, out bool held);
        // 이어 그리는 한도를 넘겼어도 표본이 계속 오면(지연이 갑자기 커진 경우) 자리는 표본을 따라 움직인다.
        // 표본까지 끊겨 자리가 멈췄을 때만 걷지 않는 것으로 본다.
        if (held && now - lastReceived > maxExtrapolation) velocity = Vector3.zero;
        correction *= Mathf.Exp(-deltaTime / correctionTime);
        yawCorrection *= Mathf.Exp(-deltaTime / yawCorrectionTime);
        position = lastPosition = targetPosition + correction;
        yaw = lastYaw = targetYaw + yawCorrection;
        Velocity = velocity;
        return true;
    }

    void TrackDelay(double offset, double receivedTime)
    {
        if (!hasDelay)
        {
            hasDelay = true;
            delay = offset + margin;
            bucketStart = receivedTime;
            bucketMin = previousMin = offset;
            return;
        }
        if (receivedTime - bucketStart >= WindowSeconds)
        {
            previousMin = bucketMin;
            bucketMin = offset;
            bucketStart = receivedTime;
        }
        else if (offset < bucketMin) bucketMin = offset;
    }

    // held는 마지막 표본에서 이어 그리는 한도를 넘겨 자리를 세워 둔 상태다.
    void Evaluate(double time, out Vector3 position, out float yaw, out Vector3 velocity, out bool held)
    {
        held = false;
        if (count == 0)
        {
            position = restPosition;
            yaw = restYaw;
            velocity = Vector3.zero;
            return;
        }
        Sample last = samples[count - 1];
        if (time >= last.Time)
        {
            double ahead = time - last.Time;
            held = ahead > maxExtrapolation;
            position = last.Position + last.Velocity * (float)(held ? maxExtrapolation : ahead);
            yaw = last.Yaw + last.YawRate * (float)Math.Min(ahead, Math.Min(MaxYawExtrapolation, maxExtrapolation));
            velocity = last.Velocity;
            return;
        }
        for (int i = count - 2; i >= 0; i--)
        {
            Sample from = samples[i];
            if (time < from.Time) continue;
            Sample to = samples[i + 1];
            double span = to.Time - from.Time;
            float t = span > 0.0 ? (float)((time - from.Time) / span) : 1f;
            position = Vector3.LerpUnclamped(from.Position, to.Position, t);
            yaw = from.Yaw + Mathf.DeltaAngle(from.Yaw, to.Yaw) * t;
            velocity = span > 0.0 ? (to.Position - from.Position) / (float)span : Vector3.zero;
            return;
        }
        position = samples[0].Position;
        yaw = samples[0].Yaw;
        velocity = Vector3.zero;
    }
}

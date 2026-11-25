using System;

// 한 판의 텔레포트 쌍 쿨타임. 서버가 판정에 쓰고, 클라이언트는 서버가 보낸 값을 그대로 받아 둔다.
// 쿨타임은 쌍 전체에 걸린다. 한쪽 타일로 이동하면 같은 색 두 타일이 함께 쉰다. 시각은 서버 시각(초)이다.
public sealed class TeleporterCooldowns
{
    // 이동한 뒤 그 쌍을 다시 쓸 수 있을 때까지의 시간(초).
    public const float Cooldown = 5f;

    readonly double[] readyAt;

    public TeleporterCooldowns(int pairs)
    {
        if (pairs < 0) throw new ArgumentOutOfRangeException(nameof(pairs));
        readyAt = new double[pairs];
        for (int i = 0; i < pairs; i++) readyAt[i] = double.NegativeInfinity;
    }

    public int Count => readyAt.Length;

    // 그 쌍을 다시 쓸 수 있는 서버 시각. 한 번도 쓰지 않았으면 음의 무한대다.
    public double ReadyAt(int pair) => readyAt[pair];

    public bool IsReady(int pair, double now) => pair >= 0 && pair < readyAt.Length && now >= readyAt[pair];

    // 쓸 수 있으면 쿨타임을 걸고 true를 돌려준다.
    public bool TryUse(int pair, double now)
    {
        if (!IsReady(pair, now)) return false;
        readyAt[pair] = now + Cooldown;
        return true;
    }

    // 서버가 보낸 값을 넣는다.
    public void Set(int pair, double value)
    {
        if (pair >= 0 && pair < readyAt.Length) readyAt[pair] = value;
    }
}

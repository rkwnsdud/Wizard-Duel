// 부시 안팎에서 플레이어를 어떻게 그릴지 정하는 규칙(이슈 #93). 화면 하나를 기준으로 계산한다.
// 유니티 오브젝트를 쓰지 않으므로 EditMode 테스트로 규칙만 따로 검사한다.
public enum BushLook
{
    // 원래대로 그린다.
    Normal,
    // 부시 안에서 보인다. 캐릭터와 무기를 한 덩어리로 CharacterAlpha만큼 비쳐 보이게 그리고(파츠끼리는 비치지 않는다),
    // 들어간 부시를 BushAlpha로 비쳐 보이게 해서 갈대가 캐릭터 앞에 겹쳐 보인다. 체력바는 보인다.
    InBush,
    // 캐릭터, 무기, 체력바를 그리지 않는다. 충돌과 위치 동기화는 그대로다.
    Hidden
}

// 한 상대에 대해 계산한 결과. 부시 번호가 -1이면 해당 부시가 없다.
public readonly struct BushView
{
    public readonly BushLook Me;
    public readonly BushLook Opponent;
    // 내 화면에서 BushAlpha로 그릴 부시. 내가 들어간 부시와, 보이는 상대가 들어간 부시다. 둘이 같을 수 있다.
    public readonly int MyFadedBush;
    public readonly int OpponentFadedBush;

    public BushView(BushLook me, BushLook opponent, int myFadedBush, int opponentFadedBush)
    {
        Me = me;
        Opponent = opponent;
        MyFadedBush = myFadedBush;
        OpponentFadedBush = opponentFadedBush;
    }
}

public static class BushVisibilityRules
{
    // 부시 안에서 보이는 캐릭터(나, 같은 부시의 상대, 공격으로 드러난 상대)의 알파. 캐릭터 반투명 셰이더가 한 픽셀에 한 번만 섞는다.
    public const float CharacterAlpha = 0.8f;
    // 내 화면에서 속이 비쳐 보이는 부시의 알파. 겹친 잎이 쌓여 진해지지 않게 갈대 반투명 셰이더가 한 픽셀에 한 번만 섞는다.
    // 이슈는 캐릭터 0.6·부시 0.4였다. 테스트하며 부시 0.6으로 캐릭터 앞에 겹치게 하고, 캐릭터는 한 덩어리로 0.8만큼 비치게 바꿨다.
    public const float BushAlpha = 0.6f;
    // 부시 안에서 공격하면 공격 시작부터 이 시간 동안 드러난다.
    public const float RevealDuration = 0.5f;

    // myBush·opponentBush는 각자 서 있는 부시 덩어리 번호이고, 부시 밖이면 -1이다.
    // opponentRevealed는 상대가 공격해서 드러나 있는지다(BushReveal).
    public static BushView Evaluate(int myBush, int opponentBush, bool opponentRevealed)
    {
        BushLook me = myBush >= 0 ? BushLook.InBush : BushLook.Normal;
        if (opponentBush < 0)
            return new BushView(me, BushLook.Normal, myBush, -1);
        // 같은 부시면 서로 보이고, 다른 부시나 부시 밖에서는 공격으로 드러날 때만 보인다.
        // 보이는 상대도 부시 안에 있으므로 그 부시를 비쳐 보이게 해서 갈대 사이에 서 있는 모습으로 보인다.
        bool visible = opponentBush == myBush || opponentRevealed;
        return visible
            ? new BushView(me, BushLook.InBush, myBush, opponentBush)
            : new BushView(me, BushLook.Hidden, myBush, -1);
    }
}

// 공격 노출 타이머. 시간은 인자로 받는다. 다시 공격하면 노출 시간을 처음부터 다시 센다.
public sealed class BushReveal
{
    float revealedUntil = float.NegativeInfinity;

    public void Reveal(float now) => revealedUntil = now + BushVisibilityRules.RevealDuration;

    public bool IsRevealed(float now) => now < revealedUntil;

    public void Clear() => revealedUntil = float.NegativeInfinity;
}

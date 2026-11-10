#if UNITY_INCLUDE_TESTS
using NUnit.Framework;

// 이슈 #93의 "보이는 규칙"을 한 화면 기준으로 검사한다. 부시 번호 -1은 부시 밖이다.
public class BushVisibilityRulesTests
{
    [Test]
    public void OutsideBushesEveryoneLooksNormal()
    {
        BushView view = BushVisibilityRules.Evaluate(-1, -1, false);

        Assert.That(view.Me, Is.EqualTo(BushLook.Normal));
        Assert.That(view.Opponent, Is.EqualTo(BushLook.Normal));
        Assert.That(view.MyFadedBush, Is.EqualTo(-1));
        Assert.That(view.OpponentFadedBush, Is.EqualTo(-1));
    }

    [Test]
    public void InsideABushISeeThroughMyBush()
    {
        BushView view = BushVisibilityRules.Evaluate(2, -1, false);

        Assert.That(view.Me, Is.EqualTo(BushLook.InBush));
        Assert.That(view.MyFadedBush, Is.EqualTo(2));
        Assert.That(view.Opponent, Is.EqualTo(BushLook.Normal));
    }

    [Test]
    public void AnOpponentInABushIsHiddenFromOutside()
    {
        BushView view = BushVisibilityRules.Evaluate(-1, 1, false);

        Assert.That(view.Opponent, Is.EqualTo(BushLook.Hidden));
        Assert.That(view.OpponentFadedBush, Is.EqualTo(-1), "The opponent's bush stays opaque on my screen.");
    }

    [Test]
    public void PlayersInDifferentBushesCannotSeeEachOther()
    {
        BushView view = BushVisibilityRules.Evaluate(0, 1, false);

        Assert.That(view.Me, Is.EqualTo(BushLook.InBush));
        Assert.That(view.Opponent, Is.EqualTo(BushLook.Hidden));
        Assert.That(view.MyFadedBush, Is.EqualTo(0));
        Assert.That(view.OpponentFadedBush, Is.EqualTo(-1));
    }

    [Test]
    public void PlayersInTheSameBushSeeEachOther()
    {
        BushView view = BushVisibilityRules.Evaluate(3, 3, false);

        Assert.That(view.Me, Is.EqualTo(BushLook.InBush));
        Assert.That(view.Opponent, Is.EqualTo(BushLook.InBush));
        Assert.That(view.MyFadedBush, Is.EqualTo(3));
        Assert.That(view.OpponentFadedBush, Is.EqualTo(3));
    }

    [Test]
    public void AnAttackRevealsTheOpponentAndTheirBush()
    {
        BushView fromOutside = BushVisibilityRules.Evaluate(-1, 1, true);
        BushView fromAnotherBush = BushVisibilityRules.Evaluate(0, 1, true);

        Assert.That(fromOutside.Opponent, Is.EqualTo(BushLook.InBush));
        Assert.That(fromOutside.OpponentFadedBush, Is.EqualTo(1));
        Assert.That(fromAnotherBush.Opponent, Is.EqualTo(BushLook.InBush));
        Assert.That(fromAnotherBush.MyFadedBush, Is.EqualTo(0));
        Assert.That(fromAnotherBush.OpponentFadedBush, Is.EqualTo(1));
    }

    [Test]
    public void ARevealOutsideBushesChangesNothing()
    {
        BushView view = BushVisibilityRules.Evaluate(-1, -1, true);

        Assert.That(view.Opponent, Is.EqualTo(BushLook.Normal));
        Assert.That(view.OpponentFadedBush, Is.EqualTo(-1));
    }

    [Test]
    public void RevealLastsHalfASecondFromTheAttack()
    {
        var reveal = new BushReveal();
        Assert.That(reveal.IsRevealed(0f), Is.False, "Nobody starts revealed.");

        reveal.Reveal(10f);

        Assert.That(reveal.IsRevealed(10f), Is.True);
        Assert.That(reveal.IsRevealed(10.49f), Is.True);
        Assert.That(reveal.IsRevealed(10.5f), Is.False);
    }

    [Test]
    public void AttackingAgainRestartsTheReveal()
    {
        var reveal = new BushReveal();
        reveal.Reveal(10f);
        reveal.Reveal(10.4f);

        Assert.That(reveal.IsRevealed(10.6f), Is.True);
        Assert.That(reveal.IsRevealed(10.9f), Is.False);

        reveal.Clear();
        Assert.That(reveal.IsRevealed(10.5f), Is.False);
    }

    [Test]
    public void AlphaValuesMatchTheAgreedLook()
    {
        // 2클라이언트 테스트 뒤 사용자 요청으로 캐릭터는 반투명을 없애고 부시는 0.6이 됐다.
        Assert.That(BushVisibilityRules.BushAlpha, Is.EqualTo(0.6f));
        Assert.That(BushVisibilityRules.RevealDuration, Is.EqualTo(0.5f));
    }
}
#endif

#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

// 두 스폰을 잇는 결투 축(DuelAxis)과, 그 선에 직교하게 돌린 전투 광원.
public class DuelAxisTests
{
    const float Tolerance = 1e-4f;
    static readonly Vector3 Host = new Vector3(-3f, 0.5f, 2f);
    // Main 씬 태양(Directional Light)의 방향.
    static readonly Quaternion SceneSun = Quaternion.Euler(50f, 330f, 0f);

    // 방장 스폰에서 참가자 스폰을 보는 실제 방향이다. 90도 단위로 끊지 않고, 높이 차이는 보지 않는다.
    [TestCase(18f, -8f)]
    [TestCase(2f, 14f)]
    [TestCase(-12f, 5f)]
    [TestCase(-6f, -20f)]
    [TestCase(15f, 15f)]
    public void Yaw_IsTheHostToGuestDirection(float dx, float dz)
    {
        float axis = DuelAxis.Yaw(Host, Host + new Vector3(dx, 3f, dz));
        Assert.That(axis, Is.GreaterThanOrEqualTo(0f).And.LessThan(360f));
        Vector3 along = Quaternion.Euler(0f, axis, 0f) * Vector3.forward;
        // Vector3.Angle은 0도 근처에서 acos 오차가 커서 단위 벡터끼리 비교한다.
        Assert.That(Vector3.Distance(along, new Vector3(dx, 0f, dz).normalized), Is.LessThan(Tolerance));
        Assert.That(DuelAxis.Yaw(Host, Host), Is.EqualTo(0f));
    }

    [Test]
    public void TryYaw_MeasuresFromTheHostSpawn_AndNeedsBothSpawns()
    {
        var host = new GameObject("DuelAxis Test Host");
        var guest = new GameObject("DuelAxis Test Guest");
        try
        {
            host.transform.position = Host;
            guest.transform.position = Host + new Vector3(-12f, 0f, 0f);
            Assert.That(DuelAxis.TryYaw(new[] { host.transform, guest.transform }, out float axis), Is.True);
            Assert.That(axis, Is.EqualTo(270f).Within(Tolerance));
            Assert.That(DuelAxis.TryYaw(new[] { guest.transform, host.transform }, out float reversed), Is.True);
            Assert.That(reversed, Is.EqualTo(90f).Within(Tolerance));

            Assert.That(DuelAxis.TryYaw(null, out _), Is.False);
            Assert.That(DuelAxis.TryYaw(new[] { host.transform }, out _), Is.False);
            Assert.That(DuelAxis.TryYaw(new[] { host.transform, null }, out _), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(guest);
        }
    }

    // 광원은 고도를 그대로 두고 수평 방향만 두 스폰을 잇는 선에 직교하게 돈다. 두 옆 가운데 씬에 놓인 방향에서 덜 도는 쪽을 고른다.
    [TestCase(0f)]
    [TestCase(23.96f)]
    [TestCase(90f)]
    [TestCase(113.96f)]
    [TestCase(180f)]
    [TestCase(251f)]
    [TestCase(330f)]
    public void SideLight_IsPerpendicularToTheAxis_KeepsElevation_AndTurnsTheShorterWay(float axis)
    {
        Vector3 home = SceneSun * Vector3.forward;
        Vector3 side = DuelAxis.SideLight(axis, SceneSun) * Vector3.forward;
        Assert.That(side.y, Is.EqualTo(home.y).Within(Tolerance), "elevation");
        Vector3 along = Quaternion.Euler(0f, axis, 0f) * Vector3.forward;
        Assert.That(Vector3.Dot(Flat(side), along), Is.EqualTo(0f).Within(Tolerance), "perpendicular to the axis");
        Assert.That(Vector3.Angle(Flat(home), Flat(side)), Is.LessThanOrEqualTo(90f + 1e-3f), "shorter turn");
    }

    // Main 씬 태양은 330° 쪽으로 비춘다. 두 스폰이 동서(90°)로 놓이면 0°(북쪽으로), 남북(0°)이면 270°(서쪽으로) 비추는 쪽이 가깝다.
    [TestCase(90f, 0f)]
    [TestCase(270f, 0f)]
    [TestCase(0f, 270f)]
    [TestCase(180f, 270f)]
    public void SideLight_FromTheSceneSun(float axis, float expectedYaw)
    {
        Vector3 side = DuelAxis.SideLight(axis, SceneSun) * Vector3.forward;
        float yaw = Mathf.Atan2(side.x, side.z) * Mathf.Rad2Deg;
        Assert.That(Mathf.DeltaAngle(yaw, expectedYaw), Is.EqualTo(0f).Within(1e-3f));
    }

    [Test]
    public void SideLight_StraightDown_StaysAsIs()
    {
        Quaternion down = Quaternion.Euler(90f, 0f, 0f);
        Assert.That(Quaternion.Angle(DuelAxis.SideLight(90f, down), down), Is.LessThan(0.01f));
    }

    // 로비에서 전투로 넘어가는 동안 해는 고도를 지킨 채 세로축으로만 짧은 쪽으로 고르게 돌고, 끝나면 전투 방향과 같다.
    [TestCase(113.96f)]
    [TestCase(251f)]
    public void TurnLight_KeepsElevation_AndTurnsEvenlyToTheBattleLight(float axis)
    {
        Quaternion battle = DuelAxis.SideLight(axis, SceneSun);
        Vector3 home = SceneSun * Vector3.forward;
        float total = Vector3.Angle(Flat(home), Flat(battle * Vector3.forward));
        Assert.That(Quaternion.Angle(DuelAxis.TurnLight(SceneSun, battle, 0f), SceneSun), Is.LessThan(0.01f));
        Assert.That(Quaternion.Angle(DuelAxis.TurnLight(SceneSun, battle, 1f), battle), Is.LessThan(0.01f));
        foreach (float t in new[] { 0.25f, 0.5f, 0.75f })
        {
            Vector3 forward = DuelAxis.TurnLight(SceneSun, battle, t) * Vector3.forward;
            Assert.That(forward.y, Is.EqualTo(home.y).Within(Tolerance), $"elevation t={t}");
            Assert.That(Vector3.Angle(Flat(home), Flat(forward)), Is.EqualTo(total * t).Within(1e-2f), $"turn t={t}");
        }
        // 로비 화면으로 돌아올 때는 거꾸로 돈다.
        Assert.That(Quaternion.Angle(DuelAxis.TurnLight(battle, SceneSun, 1f), SceneSun), Is.LessThan(0.01f));
    }

    static Vector3 Flat(Vector3 direction) => new Vector3(direction.x, 0f, direction.z).normalized;
}
#endif

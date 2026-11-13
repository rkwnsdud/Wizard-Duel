#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// 로비 카메라가 전투 카메라로 내려가고 빠져나올 때 쓰는 장면 섞기(CameraView). 플레이 모드 없이 투영 행렬과 자리만 확인한다.
public class CameraViewTests
{
    const float Aspect = 16f / 9f;
    const float Near = 0.3f;
    const float Far = 1000f;
    const float Tolerance = 1e-4f;

    static readonly Vector3 MapCenter = Vector3.zero;
    static readonly Vector3 Spawn = new Vector3(-9f, 0.5f, 4f);
    // 방장이 Spawn, 참가자가 맵 중심에 대해 180도 대칭인 (9, 0.5, -4)일 때 방장의 전투 카메라 방향과 비슷한 값.
    const float BattleYaw = 104f;

    DuelCameraSettings settings;
    GameObject cameraObject;
    Camera camera;
    readonly List<GameObject> spawnObjects = new List<GameObject>();

    [SetUp]
    public void SetUp()
    {
        // 코드 기본값이 Assets/Settings/DuelCamera 에셋 값(직교, 바닥 길이 19, 높이 10, 거리 8, 시선 높이 1)과 같다.
        settings = ScriptableObject.CreateInstance<DuelCameraSettings>();
        cameraObject = new GameObject("CameraView Test Camera");
        camera = cameraObject.AddComponent<Camera>();
        camera.aspect = Aspect;
        camera.nearClipPlane = Near;
        camera.farClipPlane = Far;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(cameraObject);
        Object.DestroyImmediate(settings);
        foreach (GameObject spawn in spawnObjects) Object.DestroyImmediate(spawn);
        spawnObjects.Clear();
    }

    // 대기실처럼 맵 중심을 화면 왼쪽에 두고 비스듬히 내려다보는 원근 장면.
    static CameraView Lobby(float yaw = 30f) => CameraView.Perspective(MapCenter, new Vector2(0.34f, 0.5f), yaw, 52f, 60f, 32f,
        new Vector2(54f, 150f), 140f);

    [Test]
    public void LobbyView_MatchesLensShiftedPerspectiveCamera()
    {
        CameraView lobby = Lobby();
        camera.orthographic = false;
        camera.fieldOfView = 32f;
        camera.ResetProjectionMatrix();
        Matrix4x4 expected = camera.projectionMatrix;
        expected[0, 2] = 1f - 2f * 0.34f;
        expected[1, 2] = 1f - 2f * 0.5f;

        AssertMatrix(lobby.Projection(Aspect, Near, Far), expected);
        Quaternion rotation = Quaternion.Euler(52f, 30f, 0f);
        AssertVector(lobby.Position, MapCenter - rotation * Vector3.forward * 60f);
        Assert.That(lobby.OrthographicWeight, Is.EqualTo(0f));
    }

    [Test]
    public void OrthographicBattleView_MatchesOrthographicCamera_AndFollowPose()
    {
        CameraView battle = settings.View(Spawn, BattleYaw, 50f);
        camera.orthographic = true;
        camera.orthographicSize = settings.OrthographicSize;
        camera.ResetProjectionMatrix();

        AssertMatrix(battle.Projection(Aspect, Near, Far), camera.projectionMatrix);
        Pose follow = settings.FollowPose(Spawn, BattleYaw);
        AssertVector(battle.Position, follow.position);
        Assert.That(Quaternion.Angle(battle.Rotation, follow.rotation), Is.LessThan(0.01f));
        Assert.That(battle.OrthographicWeight, Is.EqualTo(1f));
    }

    [Test]
    public void FullyBlendedView_HasFiniteOrthographicMatrix()
    {
        CameraView end = CameraView.Lerp(Lobby(), settings.View(Spawn, BattleYaw, 50f), 1f);
        Matrix4x4 projection = end.Projection(Aspect, Near, Far);
        for (int i = 0; i < 16; i++)
            Assert.That(float.IsNaN(projection[i]) || float.IsInfinity(projection[i]), Is.False, $"element {i}");
        Assert.That(end.OrthographicWeight, Is.EqualTo(1f));
    }

    // 섞은 투영은 원근 기준 거리(1/InverseDepth)에 선 원근 카메라와 같은 그림이어야 한다(돌리 줌).
    // 초점 평면 밖의 점까지 같은 화면 위치에 놓이는지 본다.
    [Test]
    public void BlendedProjection_EqualsDollyZoomPerspective()
    {
        CameraView lobby = Lobby();
        CameraView battle = settings.View(Spawn, BattleYaw, 50f);
        Vector3[] points =
        {
            Spawn, MapCenter, new Vector3(12f, 0f, -10f), new Vector3(-15f, 3f, 14f), new Vector3(4f, -1f, 20f)
        };
        foreach (float t in new[] { 0.1f, 0.35f, 0.6f, 0.85f, 0.97f })
        {
            CameraView view = CameraView.Lerp(lobby, battle, t);
            float depth = 1f / view.InverseDepth;
            Assert.That(depth, Is.GreaterThanOrEqualTo(view.Distance - Tolerance), $"t={t}");
            Vector3 dollyPosition = view.Focus - view.Rotation * Vector3.forward * depth;
            float dollyFov = 2f * Mathf.Atan(view.HalfHeight / depth) * Mathf.Rad2Deg;
            Matrix4x4 dolly = Matrix4x4.Perspective(dollyFov, Aspect, Near, Far + depth);
            dolly[0, 2] = 1f - 2f * view.Anchor.x;
            dolly[1, 2] = 1f - 2f * view.Anchor.y;

            Matrix4x4 blended = view.Projection(Aspect, Near, Far);
            AssertVector(ScreenPoint(blended, view.Position, view.Rotation, view.Focus), view.Anchor, $"focus t={t}");
            foreach (Vector3 point in points)
                AssertVector(ScreenPoint(blended, view.Position, view.Rotation, point),
                    ScreenPoint(dolly, dollyPosition, view.Rotation, point), $"t={t} point={point}");
        }
    }

    // 줌을 로그로 섞는 동안 도착 장면의 초점(내 스폰)이 화면에서 곧게 제자리로 가야 한다. 밀렸다 돌아오면 안 된다.
    [Test]
    public void Blend_MovesTargetStraightToItsAnchor()
    {
        CameraView lobby = Lobby(0f);
        lobby.Pitch = settings.Pitch;
        // 회전이 같을 때 초점 평면 위의 점은 정확히 곧게 가야 한다. 빨강 쪽처럼 화면 오른쪽에 있는 목표를 고른다.
        Vector3 target = MapCenter + lobby.Rotation * new Vector3(14f, -5f, 0f);
        CameraView battle = CameraView.Orthographic(target, new Vector2(0.5f, 0.5f), 0f, settings.Pitch,
            settings.FollowDistance, settings.OrthographicSize, Vector2.zero, 50f);

        Vector2 start = ScreenPoint(lobby, target);
        Vector2 direction = (battle.Anchor - start).normalized;
        float previous = float.MaxValue;
        for (int step = 0; step <= 20; step++)
        {
            Vector2 screen = ScreenPoint(CameraView.Lerp(lobby, battle, step / 20f), target);
            Vector2 offset = screen - start;
            float across = offset.x * direction.y - offset.y * direction.x;
            Assert.That(Mathf.Abs(across), Is.LessThan(1e-3f), $"step {step} left the straight path");
            float remaining = Vector2.Distance(screen, battle.Anchor);
            Assert.That(remaining, Is.LessThanOrEqualTo(previous + 1e-5f), $"step {step} moved away from the anchor");
            previous = remaining;
        }
        Assert.That(previous, Is.LessThan(1e-4f));
    }

    [TestCase(0f)]
    [TestCase(BattleYaw)]
    [TestCase(-170f)]
    public void FollowPose_MatchesLookAt_AndInvertsFromCameraPosition(float yaw)
    {
        Pose pose = settings.FollowPose(Spawn, yaw);
        cameraObject.transform.position = Spawn + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, settings.Height, -settings.Distance);
        cameraObject.transform.LookAt(Spawn + Vector3.up * settings.LookHeight);

        AssertVector(pose.position, cameraObject.transform.position);
        Assert.That(Quaternion.Angle(pose.rotation, cameraObject.transform.rotation), Is.LessThan(0.01f));
        // 로비로 빠져나올 때(TitleBackdrop.RetakeCamera)처럼 카메라가 서 있는 방향으로 내 플레이어 위치를 거꾸로 구한다.
        AssertVector(settings.TargetFromCamera(pose.position, cameraObject.transform.eulerAngles.y), Spawn);
        Assert.That(Vector3.Distance(pose.position, Spawn + Vector3.up * settings.LookHeight),
            Is.EqualTo(settings.FollowDistance).Within(Tolerance));
    }

    // 방장과 참가자의 카메라는 160도 벌어지고, 각자 상대 스폰이 화면 위쪽(방장은 오른쪽, 참가자는 왼쪽으로 10도)에 온다.
    // 마지막 경우는 스폰이 정확히 45도 대각선이다.
    [TestCase(18f, -8f)]
    [TestCase(2f, 14f)]
    [TestCase(-12f, 5f)]
    [TestCase(-6f, -20f)]
    [TestCase(15f, 15f)]
    public void FacingYaw_SpreadsTheTwoCameras_AndPutsTheOpponentAbove(float dx, float dz)
    {
        Assert.That(settings.FacingSpread, Is.EqualTo(160f));
        Vector3 guestSpawn = Spawn + new Vector3(dx, 0f, dz);
        Transform[] spawns = { NewSpawn(Spawn), NewSpawn(guestSpawn) };
        Assert.That(settings.TryFacingYaw(spawns, 0, out float host), Is.True);
        Assert.That(settings.TryFacingYaw(spawns, 1, out float guest), Is.True);
        Assert.That(Mathf.Abs(Mathf.DeltaAngle(host, guest)), Is.EqualTo(160f).Within(Tolerance));

        AssertOpponentAbove(Spawn, guestSpawn, host, 10f);
        AssertOpponentAbove(guestSpawn, Spawn, guest, -10f);
    }

    // 로비 화면은 파랑(방장)이 왼쪽, 빨강이 오른쪽에 오게 본다(TitleBackdrop.Aim). 내려가며 두 사람 모두 80도만 서로 반대로 돈다.
    [Test]
    public void FacingYaw_TurnsHalfTheSpreadFromTheLobbyView()
    {
        Vector3 guestSpawn = new Vector3(9f, 0.5f, -4f);
        Vector3 across = guestSpawn - Spawn;
        float lobbyYaw = Mathf.Atan2(-across.z, across.x) * Mathf.Rad2Deg;
        float axis = DuelAxis.Yaw(Spawn, guestSpawn);
        Assert.That(Mathf.DeltaAngle(lobbyYaw, settings.FacingYaw(axis, 0)), Is.EqualTo(80f).Within(Tolerance));
        Assert.That(Mathf.DeltaAngle(lobbyYaw, settings.FacingYaw(axis, 1)), Is.EqualTo(-80f).Within(Tolerance));
    }

    [Test]
    public void TryFacingYaw_NeedsBothSpawns()
    {
        Assert.That(settings.TryFacingYaw(null, 0, out _), Is.False);
        Assert.That(settings.TryFacingYaw(new Transform[1], 0, out _), Is.False);
        Assert.That(settings.TryFacingYaw(new[] { NewSpawn(Spawn), null }, 0, out _), Is.False);
    }

    Transform NewSpawn(Vector3 position)
    {
        var spawn = new GameObject("CameraView Test Spawn");
        spawn.transform.position = position;
        spawnObjects.Add(spawn);
        return spawn.transform;
    }

    // mine을 따라가는 yaw 방향 전투 화면에서 opponent가 화면 위쪽에 놓이고, 화면 위 방향에서 tilt도만큼
    // (양수면 오른쪽, 음수면 왼쪽) 벗어나 있는지 본다.
    void AssertOpponentAbove(Vector3 mine, Vector3 opponent, float yaw, float tilt)
    {
        Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        Vector3 toward = Vector3.ProjectOnPlane(opponent - mine, Vector3.up);
        Assert.That(Vector3.SignedAngle(forward, toward, Vector3.up), Is.EqualTo(tilt).Within(1e-3f), $"yaw={yaw}");

        CameraView battle = settings.View(mine, yaw, 50f);
        Vector2 screen = ScreenPoint(battle, opponent) - ScreenPoint(battle, mine);
        Assert.That(screen.y, Is.GreaterThan(0f), $"yaw={yaw} screen={screen}");
        Assert.That(Mathf.Sign(screen.x), Is.EqualTo(Mathf.Sign(tilt)), $"yaw={yaw} screen={screen}");
    }

    static Vector2 ScreenPoint(CameraView view, Vector3 point) =>
        ScreenPoint(view.Projection(Aspect, Near, Far), view.Position, view.Rotation, point);

    // 점이 놓이는 화면 위치. (0, 0)은 왼쪽 아래, (1, 1)은 오른쪽 위.
    static Vector2 ScreenPoint(Matrix4x4 projection, Vector3 position, Quaternion rotation, Vector3 point)
    {
        Matrix4x4 worldToCamera = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(position, rotation, Vector3.one).inverse;
        Vector4 clip = projection * worldToCamera * new Vector4(point.x, point.y, point.z, 1f);
        return new Vector2(clip.x / clip.w, clip.y / clip.w) * 0.5f + new Vector2(0.5f, 0.5f);
    }

    static void AssertMatrix(Matrix4x4 actual, Matrix4x4 expected)
    {
        for (int i = 0; i < 16; i++)
            Assert.That(actual[i], Is.EqualTo(expected[i]).Within(Tolerance), $"element {i}");
    }

    static void AssertVector(Vector3 actual, Vector3 expected, string message = null)
    {
        Assert.That(Vector3.Distance(actual, expected), Is.LessThan(Tolerance), $"{message} actual={actual} expected={expected}");
    }

    static void AssertVector(Vector2 actual, Vector2 expected, string message = null)
    {
        Assert.That(Vector2.Distance(actual, expected), Is.LessThan(Tolerance), $"{message} actual={actual} expected={expected}");
    }
}
#endif

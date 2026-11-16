#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

public class MovePlaybackTests
{
    const double Frame = 1.0 / 60.0;
    const float Speed = 5f;
    const float Step = Speed / 60f;
    const float MaxExtrapolation = 0.12f;
    // 보낸 쪽과 받는 쪽은 시계의 기준이 다르다.
    const double ClockOffset = 1000.0;
    // 3프레임보다 조금 짧아, 보낸 지 3프레임 뒤의 화면에 도착한다.
    const double Latency = 3 * Frame - 0.001;

    // 초당 60프레임으로 보내는 소유자와 초당 60프레임으로 그리는 상대 화면을 한 프레임씩 돌린다.
    sealed class Link
    {
        struct Packet
        {
            public double Arrive, Sent;
            public Vector3 Position, Velocity;
            public float Yaw;
        }

        public readonly MovePlayback Playback = new MovePlayback(MaxExtrapolation, 0.06f, 0.03f, 2f, 0f);
        public Func<int, Vector3> Position = _ => Vector3.zero;
        public Func<int, float> Yaw = _ => 0f;
        // 음수면 잃어버린 패킷이다.
        public Func<int, double> SendLatency = _ => Latency;
        public Func<int, double> SenderClock = _ => ClockOffset;
        readonly List<Packet> flying = new List<Packet>();

        public Link() => Playback.Reset(Vector3.zero, 0f);

        public int FrameIndex { get; private set; }
        public Vector3 Shown { get; private set; }
        public float ShownYaw { get; private set; }
        // 가장 최근에 도착한 상태의 자리.
        public Vector3 Newest { get; private set; }

        public void Run(int frames, Action<int> each = null)
        {
            for (int i = 0; i < frames; i++)
            {
                int frame = ++FrameIndex;
                double now = frame * Frame;
                double latency = SendLatency(frame);
                if (latency >= 0.0)
                {
                    Vector3 moved = Position(frame) - Position(frame - 1);
                    // 소유자는 한 프레임에 1m 넘게 옮겨지면 속도를 0으로 보낸다.
                    flying.Add(new Packet
                    {
                        Arrive = now + latency, Sent = now + SenderClock(frame), Position = Position(frame),
                        Velocity = moved.sqrMagnitude < 1f ? moved / (float)Frame : Vector3.zero, Yaw = Yaw(frame)
                    });
                }
                flying.Sort((a, b) => a.Arrive != b.Arrive ? a.Arrive.CompareTo(b.Arrive) : a.Sent.CompareTo(b.Sent));
                while (flying.Count > 0 && flying[0].Arrive <= now)
                {
                    Packet packet = flying[0];
                    flying.RemoveAt(0);
                    if (Playback.Push(packet.Sent, now, packet.Position, packet.Velocity, packet.Yaw)) Newest = packet.Position;
                }
                Playback.Advance(now, (float)Frame, out Vector3 shown, out float yaw);
                Shown = shown;
                ShownYaw = yaw;
                each?.Invoke(frame);
            }
        }
    }

    // from 프레임부터 to 프레임까지 x 방향으로 걷고 그 뒤로는 서 있는다.
    static Vector3 Walk(int frame, int from, int to = int.MaxValue) =>
        new Vector3(Step * (Mathf.Clamp(frame, from, to) - from), 0f, 0f);

    [Test]
    public void BeforeTheFirstState_StaysAtTheSpawnPose()
    {
        var playback = new MovePlayback(MaxExtrapolation, 0.06f, 0.03f, 2f, 0f);
        playback.Reset(new Vector3(3f, 0f, -2f), 90f);
        Assert.That(playback.Advance(1.0, (float)Frame, out Vector3 position, out float yaw), Is.False);
        Assert.That(position, Is.EqualTo(new Vector3(3f, 0f, -2f)));
        Assert.That(yaw, Is.EqualTo(90f));
        Assert.That(playback.HasSamples, Is.False);
    }

    [Test]
    public void SteadyWalk_ShowsTheNewestStateEveryFrame()
    {
        var link = new Link { Position = frame => Walk(frame, 30) };
        link.Run(120);
        link.Run(120, frame =>
        {
            // 쌓아 두고 늦게 재생하지 않는다. 전송 지연(3프레임)만큼 전의 소유자 자리가 그대로 보인다.
            Assert.That(Vector3.Distance(link.Shown, link.Position(frame - 3)), Is.LessThan(0.001f), "frame " + frame);
            Assert.That(link.Playback.Velocity.x, Is.EqualTo(Speed).Within(0.01f));
        });
    }

    [Test]
    public void LatePackets_DoNotChangeTheWalkingSpeed()
    {
        var link = new Link { Position = frame => Walk(frame, 30) };
        // 네 프레임의 상태가 40ms 늦게 온다. 그동안 마지막 속도로 이어 그려 멈칫하지 않는다.
        link.SendLatency = frame => frame >= 150 && frame < 154 ? Latency + 0.04 : Latency;
        link.Run(120);
        link.Run(120, frame =>
            Assert.That(Vector3.Distance(link.Shown, link.Position(frame - 3)), Is.LessThan(0.001f), "frame " + frame));
    }

    [Test]
    public void Stop_ArrivingOnTime_DoesNotOvershoot()
    {
        var link = new Link { Position = frame => Walk(frame, 30, 150) };
        float stop = Walk(150, 30).x;
        link.Run(240, frame => Assert.That(link.Shown.x, Is.LessThan(stop + 0.001f), "frame " + frame));
        Assert.That(link.Shown.x, Is.EqualTo(stop).Within(0.001f));
        Assert.That(link.Playback.Velocity, Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void Stop_ArrivingLate_OvershootsBrieflyAndSlidesBack()
    {
        var link = new Link { Position = frame => Walk(frame, 30, 150) };
        // 멈췄다는 상태가 두 프레임 늦게 온다. 그 두 프레임만큼은 걷던 속도로 더 나간다.
        link.SendLatency = frame => frame == 151 || frame == 152 ? Latency + 2 * Frame : Latency;
        float stop = Walk(150, 30).x;
        float overshoot = 0f;
        float previous = 0f;
        link.Run(155, frame =>
        {
            overshoot = Mathf.Max(overshoot, link.Shown.x - stop);
            previous = link.Shown.x;
        });
        Assert.That(overshoot, Is.GreaterThan(Step).And.LessThan(2 * Step + 0.001f));

        link.Run(18, frame =>
        {
            // 멈춘 것을 안 뒤로는 걷지 않는 것으로 보고, 넘어간 만큼만 순간이동 없이 되돌아온다.
            Assert.That(link.Playback.Velocity, Is.EqualTo(Vector3.zero), "frame " + frame);
            Assert.That(link.Shown.x, Is.LessThan(previous).And.GreaterThan(stop - 0.001f), "frame " + frame);
            Assert.That(previous - link.Shown.x, Is.LessThan(Step), "frame " + frame);
            previous = link.Shown.x;
        });
        Assert.That(link.Shown.x, Is.EqualTo(stop).Within(0.01f));
    }

    [Test]
    public void Silence_StopsAfterTheExtrapolationLimit()
    {
        var link = new Link { Position = frame => Walk(frame, 30) };
        // 150프레임의 상태를 마지막으로 아무것도 오지 않는다.
        link.SendLatency = frame => frame <= 150 ? Latency : -1.0;
        link.Run(157);
        Assert.That(link.Playback.Velocity.x, Is.EqualTo(Speed).Within(0.01f), "Still extrapolating.");
        link.Run(30);
        Assert.That(link.Shown.x, Is.EqualTo(Walk(150, 30).x + Speed * MaxExtrapolation).Within(0.001f));
        Assert.That(link.Playback.Velocity, Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void OlderState_IsIgnored()
    {
        var playback = new MovePlayback(MaxExtrapolation, 0.06f, 0.03f, 2f, 0f);
        playback.Reset(Vector3.zero, 0f);
        Assert.That(playback.Push(10.0, 0.05, Vector3.right, Vector3.zero, 0f), Is.True);
        Assert.That(playback.Push(9.99, 0.06, Vector3.right * 50f, Vector3.zero, 0f), Is.False);
        Assert.That(playback.Push(10.0, 0.06, Vector3.right * 50f, Vector3.zero, 0f), Is.False, "Same time.");
        for (int i = 0; i < 30; i++) playback.Advance(0.06 + i * Frame, (float)Frame, out _, out _);
        playback.Advance(1.0, (float)Frame, out Vector3 position, out _);
        Assert.That(Vector3.Distance(position, Vector3.right), Is.LessThan(0.001f));
    }

    [Test]
    public void Teleport_SnapsInsteadOfSliding()
    {
        var target = new Vector3(10f, 0f, 4f);
        var link = new Link { Position = frame => frame < 60 ? Vector3.zero : target };
        link.Run(62);
        Assert.That(link.Shown, Is.EqualTo(Vector3.zero));
        link.Run(1);
        Assert.That(Vector3.Distance(link.Shown, target), Is.LessThan(0.001f));
    }

    [Test]
    public void SenderClockJump_ContinuesFromTheShownPosition()
    {
        var link = new Link { Position = frame => Walk(frame, 30) };
        // 보낸 쪽 시계가 10초 건너뛴다(창이 멈췄다 돌아온 경우).
        link.SenderClock = frame => frame < 150 ? ClockOffset : ClockOffset + 10.0;
        link.Run(149);
        float previous = link.Shown.x;
        link.Run(60, frame =>
        {
            Assert.That(link.Shown.x - previous, Is.GreaterThan(0f).And.LessThan(2 * Step), "frame " + frame);
            previous = link.Shown.x;
        });
        Assert.That(Vector3.Distance(link.Shown, link.Position(link.FrameIndex - 3)), Is.LessThan(0.001f));
    }

    [TestCase(0.03)]
    [TestCase(-0.03)]
    public void LatencyChange_IsFollowedByPlaybackSpeedNotByAJump(double change)
    {
        var link = new Link { Position = frame => Walk(frame, 30) };
        link.SendLatency = frame => Latency + 0.04 + (frame < 200 ? 0.0 : change);
        link.Run(199);
        float previous = link.Shown.x;
        link.Run(300, frame =>
        {
            Assert.That(link.Shown.x - previous, Is.GreaterThan(0.88f * Step).And.LessThan(1.12f * Step), "frame " + frame);
            previous = link.Shown.x;
        });
        Assert.That(Vector3.Distance(link.Shown, link.Newest), Is.LessThan(0.001f), "Shows the newest state again.");
    }

    [Test]
    public void Yaw_TurnsTheShortWayAcrossZero()
    {
        var link = new Link { Yaw = frame => frame < 60 ? 350f : 10f };
        link.Run(59);
        Assert.That(Mathf.DeltaAngle(350f, link.ShownYaw), Is.EqualTo(0f).Within(0.01f));
        link.Run(24, frame =>
            Assert.That(Mathf.DeltaAngle(350f, link.ShownYaw), Is.InRange(-0.01f, 20.01f), "frame " + frame));
        Assert.That(Mathf.DeltaAngle(10f, link.ShownYaw), Is.EqualTo(0f).Within(0.1f));
    }

    [Test]
    public void Yaw_KeepsTurningWhileStatesAreLate()
    {
        // 한 프레임에 3도씩(초당 180도) 돈다.
        var link = new Link { Yaw = frame => Mathf.Repeat(3f * frame, 360f) };
        // 네 프레임의 상태가 30ms 늦게 온다. 그동안 돌던 빠르기로 이어 돌려 멈칫하지 않는다.
        link.SendLatency = frame => frame >= 150 && frame < 154 ? Latency + 0.03 : Latency;
        link.Run(120);
        link.Run(120, frame =>
            Assert.That(Mathf.DeltaAngle(link.Yaw(frame - 3), link.ShownYaw), Is.EqualTo(0f).Within(0.01f), "frame " + frame));
    }

    [Test]
    public void Yaw_StopArrivingLate_OvershootsBrieflyAndTurnsBack()
    {
        var link = new Link { Yaw = frame => 3f * Mathf.Clamp(frame - 60, 0, 90) };
        // 다 돌았다는 상태가 두 프레임 늦게 온다. 그 두 프레임만큼은 돌던 빠르기로 더 돈다.
        link.SendLatency = frame => frame == 151 || frame == 152 ? Latency + 2 * Frame : Latency;
        float overshoot = 0f;
        float previous = 0f;
        link.Run(155, frame =>
        {
            overshoot = Mathf.Max(overshoot, link.ShownYaw - 270f);
            previous = link.ShownYaw;
        });
        Assert.That(overshoot, Is.GreaterThan(3f).And.LessThan(6.01f));

        link.Run(18, frame =>
        {
            Assert.That(link.ShownYaw, Is.LessThan(previous).And.GreaterThan(269.99f), "frame " + frame);
            previous = link.ShownYaw;
        });
        Assert.That(link.ShownYaw, Is.EqualTo(270f).Within(0.01f));
    }

    [Test]
    public void Yaw_FlickFollowedBySilence_TurnsOnlyALittleFurther()
    {
        // 한 프레임에 90도를 돌린 직후 12프레임의 상태를 잃어버린다.
        var link = new Link { Yaw = frame => frame < 60 ? 0f : 90f };
        link.SendLatency = frame => frame > 60 && frame <= 72 ? -1.0 : Latency;
        // 휙 돌린 빠르기를 그대로 믿지 않는다. 초당 720도로 0.05초까지만 이어 돌린다.
        link.Run(75, frame => Assert.That(link.ShownYaw, Is.InRange(-0.01f, 126.01f), "frame " + frame));
        link.Run(30);
        Assert.That(link.ShownYaw, Is.EqualTo(90f).Within(0.01f));
    }

    [Test]
    public void State_SerializesTo26BytesAndRoundTrips()
    {
        var state = new DuelMoveState
        {
            Time = 12345.678901, Position = new Vector3(-12.345f, 0.5f, 67.891f),
            Velocity = new Vector2(-3.5355f, 3.5355f), Yaw = 359.999f
        };
        using var writer = new FastBufferWriter(64, Allocator.Temp);
        writer.WriteNetworkSerializable(state);
        Assert.That(writer.Length, Is.EqualTo(26));
        using var reader = new FastBufferReader(writer, Allocator.Temp);
        reader.ReadNetworkSerializable(out DuelMoveState read);
        Assert.That(read.Time, Is.EqualTo(state.Time));
        Assert.That(read.Position, Is.EqualTo(state.Position));
        Assert.That(Vector2.Distance(read.Velocity, state.Velocity), Is.LessThan(0.001f));
        Assert.That(Mathf.DeltaAngle(read.Yaw, state.Yaw), Is.EqualTo(0f).Within(0.01f));
        Assert.That(read.IsFinite, Is.True);
        Assert.That(new DuelMoveState { Position = new Vector3(0f, float.NaN, 0f) }.IsFinite, Is.False);
        Assert.That(new DuelMoveState { Time = double.PositiveInfinity }.IsFinite, Is.False);
    }

    [TestCase("Assets/Game/Player/Prefabs/DuelWizard-1.prefab")]
    [TestCase("Assets/Game/Player/Prefabs/DuelWizard-2.prefab")]
    public void DuelPlayerPrefab_SyncsMovementWithDuelMoveSyncOnly(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.GetComponent<DuelMoveSync>(), Is.Not.Null);
        // 둘이 함께 있으면 같은 Transform을 서로 덮어쓴다.
        Assert.That(prefab.GetComponent<NetworkTransform>(), Is.Null);
        Assert.That(prefab.GetComponent<NetworkObject>().SynchronizeTransform, Is.True, "The spawn pose comes with the spawn message.");
    }
}
#endif

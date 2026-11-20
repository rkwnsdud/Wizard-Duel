using UnityEngine;

// Shared firing contract. Authority supplies presentation and damage transport.
// DuelPlayer supplies authority and transport; weapons do not depend on Netcode.
public interface IWeapon
{
    // Called once by the attack owner at FireMagic. Non-charging weapons receive 0.
    // attack은 공격을 시작할 때 정한 번호와 무기 id다. 이 공격이 내는 연출(ShotEvent)과
    // 명중 보고(sink.Hit)에는 모두 이 값을 그대로 싣는다. 날아가는 투사체도 이 값을 들고 있다가
    // 나중의 명중·폭발을 같은 sink로 보고한다.
    // Report damage only through sink.Hit.
    void Fire(AttackInfo attack, Vector3 direction, float charge, IShotSink sink);
    // Visuals only on both clients (including the owner). Do not run hit detection
    // or apply damage from Present.
    void Present(ShotEvent shot);
}

public interface IShotSink
{
    // 명중으로 승부가 나면 바로 정리될 수 있으므로 연출을 명중보다 먼저 보낸다.
    void Broadcast(ShotEvent shot);
    // 피해는 (공격 정보, hitIndex)마다 대상에게 한 번 들어간다. 장판 틱처럼 여러 번 때리면 hitIndex를 늘린다.
    // A single-hit projectile uses hitIndex 0 and stops after its first hit.
    void Hit(AttackInfo attack, int hitIndex, PlayerHealth target, float damage);
}

public interface IEnvironmentShotSink
{
    void EnvironmentHit(AttackInfo attack, IEnvironmentDestructible target, bool burning);
}

// 연출 한 건. Kind(발사·끝 등)는 무기가 정하고, Point는 Kind에 따라 끝점·방향·중심을 담는다.
// Attack은 이 연출을 낸 공격이다. 받는 쪽은 지금 든 무기가 아니라 Attack.WeaponId의 무기로 연출한다.
// Do not reinterpret Charge as speed/time or replay remote collision simulation.
public readonly struct ShotEvent
{
    public readonly AttackInfo Attack;
    public readonly byte Kind;
    public readonly Vector3 Start, Point;
    public readonly float Charge;

    public uint AttackId => Attack.Id;
    public byte WeaponId => Attack.WeaponId;

    public ShotEvent(AttackInfo attack, byte kind, Vector3 start, Vector3 point, float charge)
    {
        Attack = attack;
        Kind = kind;
        Start = start;
        Point = point;
        Charge = charge;
    }
}

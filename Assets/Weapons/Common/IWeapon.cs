using UnityEngine;

// Online-only contract, separate from WeaponBase.Fire/ IChargeableWeapon.FireCharged.
// DuelPlayer supplies authority and transport; weapons do not depend on Netcode.
public interface IWeapon
{
    // Called once by the attack owner at FireMagic. Non-charging weapons receive 0.
    // Report damage only through sink.Hit; never call PlayerHealth.TakeDamage or
    // the offline Fire method. A future moving projectile must retain this attackId
    // and report its eventual hit through the same sink.
    void Fire(uint attackId, Vector3 direction, float charge, IShotSink sink);
    // Visuals only on both clients (including the owner). Do not run hit detection
    // or spawn an offline projectile which applies damage from Present.
    void Present(ShotEvent shot);
}

public interface IShotSink
{
    // 명중으로 승부가 나면 바로 정리될 수 있으므로 연출을 명중보다 먼저 보낸다.
    void Broadcast(ShotEvent shot);
    // 피해는 (공격 id, hitIndex)마다 한 번 들어간다. 장판 틱처럼 여러 번 때리면 hitIndex를 늘린다.
    // A single-hit projectile uses hitIndex 0 and stops after its first hit.
    void Hit(uint attackId, int hitIndex, PlayerHealth target, float damage);
}

// 연출 한 건. Kind(발사·끝 등)는 무기가 정하고, Point는 Kind에 따라 끝점·방향·중심을 담는다.
// Current payload supports instantaneous lightning. Moving/reflected attacks also
// need timing, speed/path sequencing and attack-to-weapon routing before shipping;
// do not reinterpret Charge as speed/time or replay remote collision simulation.
public readonly struct ShotEvent
{
    public readonly uint AttackId;
    public readonly byte Kind;
    public readonly Vector3 Start, Point;
    public readonly float Charge;

    public ShotEvent(uint attackId, byte kind, Vector3 start, Vector3 point, float charge)
    {
        AttackId = attackId;
        Kind = kind;
        Start = start;
        Point = point;
        Charge = charge;
    }
}

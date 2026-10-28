using UnityEngine;

// 공격자의 명중 판정과 각 화면의 연출을 분리한다. 무기는 Netcode에 의존하지 않는다.
public interface IWeapon
{
    // 온라인에서는 공격한 클라이언트에서만 불린다.
    void Fire(uint attackId, Vector3 direction, float charge, IShotSink sink);
    // sink가 보낸 연출을 그린다. 피해는 주지 않는다.
    void Present(ShotEvent shot);
}

public interface IShotSink
{
    // 명중으로 승부가 나면 바로 정리될 수 있으므로 연출을 명중보다 먼저 보낸다.
    void Broadcast(ShotEvent shot);
    // 피해는 (공격 id, hitIndex)마다 한 번 들어간다. 장판 틱처럼 여러 번 때리면 hitIndex를 늘린다.
    void Hit(uint attackId, int hitIndex, PlayerHealth target, float damage);
}

// 연출 한 건. Kind(발사·끝 등)는 무기가 정하고, Point는 Kind에 따라 끝점·방향·중심을 담는다.
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


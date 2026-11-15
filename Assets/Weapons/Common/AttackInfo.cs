using System;

// 공격 하나를 가리키는 정보. 공격을 시작할 때 한 번 정하고, 그 공격의 이후 이벤트
// (공격 시작, 발사 연출, 명중·폭발 같은 후속 연출, 명중 보고, 적중 효과)에 모두 그대로 싣는다.
// 네트워크로는 Id와 WeaponId 두 정수만 보낸다.
//
// 타격 번호(hitIndex)는 이 공격 안에서 피해 한 번을 가리킨다. 서버는 (공격자, Id, hitIndex)를
// 맞은 대상마다 한 번만 받는다. 범위 공격은 대상마다 다른 번호를, 여러 번 때리는 공격은 같은
// 대상에게 늘어나는 번호를 쓴다. 한 번 맞히고 끝나는 공격은 0이다.
public readonly struct AttackInfo : IEquatable<AttackInfo>
{
    // 상태이상 틱 번호는 최상위 비트를 켠다(StatusEffectState.DotAttackIdFlag). 공격 번호는 그 아래에서 돈다.
    public const uint MaxId = 0x7FFFFFFF;

    // 공격한 플레이어 오브젝트마다 1부터 1씩 는다. 0은 공격 정보가 없다는 뜻이다.
    // 판마다 플레이어 오브젝트를 새로 만들므로 번호도 판마다 1부터 다시 센다.
    public readonly uint Id;
    // 공격을 시작한 무기의 정의 id(WeaponDefinition.id). 0은 빈 무기다.
    public readonly byte WeaponId;

    public AttackInfo(uint id, byte weaponId)
    {
        Id = id;
        WeaponId = weaponId;
    }

    public bool IsValid => Id != 0 && WeaponId != 0;

    // 다음 공격 번호. 0과 상태이상 틱 번호를 건너뛴다.
    public static uint NextId(uint previous) => previous >= MaxId ? 1u : previous + 1u;

    public bool Equals(AttackInfo other) => Id == other.Id && WeaponId == other.WeaponId;
    public override bool Equals(object obj) => obj is AttackInfo other && Equals(other);
    public override int GetHashCode() => (int)Id * 397 ^ WeaponId;
    public static bool operator ==(AttackInfo a, AttackInfo b) => a.Equals(b);
    public static bool operator !=(AttackInfo a, AttackInfo b) => !a.Equals(b);
    public override string ToString() => $"attack={Id} weapon={WeaponId}";
}

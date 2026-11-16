using System;

// 공격 하나를 가리키는 정보. 공격을 시작할 때 한 번 정하고, 그 공격의 이후 이벤트
// (공격 시작, 발사 연출, 명중·폭발 같은 후속 연출, 명중 보고, 적중 효과)에 모두 그대로 싣는다.
// 네트워크로는 Id와 WeaponId 두 정수만 보낸다.
//
// 타격 번호(hitIndex)는 이 공격 안에서 피해 한 번을 가리킨다. 서버는 (공격자, Id, hitIndex)를
// 맞은 대상마다 한 번만 받는다. 범위 공격은 대상마다 다른 번호를, 여러 번 때리는 공격은 같은
// 대상에게 늘어나는 번호를 쓴다. 한 번 맞히고 끝나는 공격은 0이다.
//
// 이벤트를 받는 쪽의 규칙. 공격한 쪽 화면, 상대 화면, 서버가 모두 같다.
//  - 공격 이벤트는 지금 든 무기가 아니라 WeaponId의 무기(PlayerAttack.FindWeapon)로 처리한다. 교체한 무기도
//    꺼진 채 남아 있으므로, 교체 뒤 늦게 온 연출·명중 보고·적중 효과도 쏜 무기가 처리한다.
//  - 무기 id가 0이거나 이 화면에서 그 무기를 끝내 찾지 못한 이벤트는 다른 무기로 대신하지 않는다. 연출과
//    상대 화면의 공격 시작은 버리고, 서버의 적중 효과는 건너뛴다(피해는 쏜 쪽 보고대로 이미 들어갔다).
//    어느 경우든 공격 정보를 담은 경고 로그를 한 번 남긴다.
//  - 판이 끝나 전투가 꺼지면 모든 화면에서 진행 중인 공격 모션과 예고·차징 미리보기를 거둔다. 이미 날아가는
//    투사체 연출은 1초 안팎에 스스로 끝나므로 그대로 둔다(마지막 일격의 연출이 잘리지 않게). 그 뒤에 온
//    명중 보고는 서버가 받지 않는다.
//  - 공격자가 디스폰되면 그 공격의 투사체와 화염 공(판정용·연출용)은 명중을 보고하거나 띄우지 않고 사라진다.
//    무기에 딸린 예고·미리보기는 무기와 함께 지워지고, 디스폰된 오브젝트로 늦게 온 RPC는 NGO가 버린다.
//  - 판마다 플레이어 오브젝트를 새로 만들므로 지난 판의 공격 이벤트는 새 판의 플레이어에게 닿지 않는다.
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

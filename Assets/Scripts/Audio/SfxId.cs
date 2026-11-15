using UnityEngine;

// 효과음 이름. 숫자가 SfxLibrary 에셋에 저장되므로 바꾸지 않고, 새 소리는 끝에 번호를 이어 붙인다.
// InspectorName은 효과음 패널에 보이는 이름이다.
public enum SfxId
{
    [InspectorName("기본 지팡이 발사")] WandFire = 0,
    [InspectorName("화염 향로 발사")] CenserFire = 1,
    [InspectorName("빛 구슬 발사")] LightOrbFire = 2,
    [InspectorName("빛 구슬 반사")] LightOrbReflect = 3,
    [InspectorName("번개")] Thunder = 4,
    [InspectorName("폭발 마법 시전")] ExplosionCast = 5,
    [InspectorName("폭발 마법 폭발")] Explosion = 6,
    [InspectorName("무기 줍기")] WeaponPickup = 7,
    [InspectorName("무기 교체")] WeaponSwap = 8,
    [InspectorName("무기 등장 알림")] WeaponSpawnChime = 9,
}

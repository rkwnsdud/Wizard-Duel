using UnityEngine;

public enum WeaponEnvironmentEffect
{
    None,
    BreakDestructibleWalls,
    BurnBushes
}

// 무기 하나의 식별자, 표시 정보와 전시 설정. 전투·로비·HUD가 같은 정의를 읽는다.
[CreateAssetMenu(menuName = "Wizard Duel/Weapon Definition")]
public sealed class WeaponDefinition : ScriptableObject
{
    public byte id;
    [Tooltip("서버가 환경 블록 파괴 요청을 검증할 때 사용하는 효과입니다.")]
    public WeaponEnvironmentEffect environmentEffect;
    public WeaponBase prefab;
    public string displayName;
    public string hudDisplayName;
    public Sprite icon;
    public bool canSpawn = true;
    public bool alignLongestAxis = true;
    public Vector3 displayRotation;
    [Min(.1f)] public float displaySize = 1f;
    public float rotationSpeed = 30f;

    [Header("Icon baking")]
    public string iconFile;
    public Vector3 iconRotation;
    public bool iconMipmaps = true;
}

using UnityEngine;

// 무기 하나의 식별자, 표시 정보와 전시 설정. 전투·로비·HUD가 같은 정의를 읽는다.
[CreateAssetMenu(menuName = "Wizard Duel/Weapon Definition")]
public sealed class WeaponDefinition : ScriptableObject
{
    public byte id;
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

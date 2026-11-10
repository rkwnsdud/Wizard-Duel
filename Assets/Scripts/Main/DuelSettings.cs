using System;

// 대기실에서 방장이 정하는 대결 설정.
public sealed class DuelSettings
{
    // 고정 크기로 고를 수 있는 한 변의 칸 수. 이 범위면 어떤 형태든 생성기가 받는다.
    public const int MinSize = 25;
    public const int MaxSize = 61;
    public const float MaxObstacleDensity = 0.25f;
    public const float MaxBushDensity = 0.2f;

    // 노이즈는 강도만 고른다. 스케일은 강도에 따라 변형이 가장 고르게 커지는 생성기 기본값(0.75)을 그대로 쓴다.
    public MapGenerationSettings Map = new MapGenerationSettings();

    public DuelSettings Clone() => new DuelSettings { Map = ToMapSettings() };

    // 미리보기와 전투가 같은 맵을 만들도록 확정한 시드를 복사한다.
    public MapGenerationSettings ToMapSettings() => new MapGenerationSettings
    {
        Seed = Map.Seed,
        Size = Map.Size,
        Shape = Map.Shape,
        ObstacleDensity = Map.ObstacleDensity,
        BushDensity = Map.BushDensity,
        Symmetry = Map.Symmetry,
        NoiseStrength = Map.NoiseStrength,
        NoiseScale = Map.NoiseScale
    };

    public static bool ValidateSettings(DuelSettings requested, out string reason)
    {
        reason = "Room settings are invalid.";
        if (requested == null || requested.Map == null) return false;
        MapGenerationSettings map = requested.Map;
        if (map.Size.HasValue &&
            (map.Size.Value.x < DuelSettings.MinSize || map.Size.Value.x > DuelSettings.MaxSize ||
             map.Size.Value.y < DuelSettings.MinSize || map.Size.Value.y > DuelSettings.MaxSize)) return false;
        if (!Enum.IsDefined(typeof(MapShape), map.Shape) ||
            !Enum.IsDefined(typeof(MapSymmetry), map.Symmetry)) return false;
        // 양방향 범위 비교는 NaN과 무한대도 거절한다.
        if (!(map.ObstacleDensity >= 0 && map.ObstacleDensity <= DuelSettings.MaxObstacleDensity) ||
            !(map.BushDensity >= 0 && map.BushDensity <= DuelSettings.MaxBushDensity) ||
            !(map.NoiseStrength >= 0 && map.NoiseStrength <= 1) ||
            !(map.NoiseScale >= 0.25f && map.NoiseScale <= 2)) return false;
        reason = string.Empty;
        return true;
    }
}

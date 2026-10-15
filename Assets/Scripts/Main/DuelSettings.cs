// 대기실에서 방장이 정하는 대결 설정. 맵 생성 설정에 "시드 고정 여부"를 더한 것이다.
public sealed class DuelSettings
{
    // 고정 크기로 고를 수 있는 한 변의 칸 수. 이 범위면 어떤 형태든 생성기가 받는다.
    public const int MinSize = 25;
    public const int MaxSize = 61;
    public const float MaxObstacleDensity = 0.35f;

    // 꺼져 있으면 Map.Seed는 쓰지 않고, 전투마다 서버가 시드를 새로 정한다.
    public bool SeedFixed;
    // 노이즈는 강도만 고른다. 스케일은 강도에 따라 변형이 가장 고르게 커지는 생성기 기본값(0.75)을 그대로 쓴다.
    public MapGenerationSettings Map = new MapGenerationSettings();

    public DuelSettings Clone() => new DuelSettings { SeedFixed = SeedFixed, Map = ToMapSettings(Map.Seed) };

    // 맵을 실제로 만들 때 넘길 설정. 시드를 고정하지 않았으면 받은 시드를 쓴다.
    public MapGenerationSettings ToMapSettings(int seed) => new MapGenerationSettings
    {
        Seed = SeedFixed ? Map.Seed : seed,
        Size = Map.Size,
        Shape = Map.Shape,
        ObstacleDensity = Map.ObstacleDensity,
        Symmetry = Map.Symmetry,
        NoiseStrength = Map.NoiseStrength,
        NoiseScale = Map.NoiseScale
    };
}

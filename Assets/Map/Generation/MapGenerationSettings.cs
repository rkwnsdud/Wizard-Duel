using UnityEngine;

public enum MapShape
{
    Random,
    Circle,
    Rectangle,
    Elongated,
    Donut
}

public enum MapSymmetry
{
    None,
    Rotate180
}

public sealed class MapGenerationSettings
{
    public int Seed { get; set; } = 7301;

    // 생략하면 형태별 범위에서 크기를 선택한다.
    public Vector2Int? Size { get; set; }
    public MapShape Shape { get; set; } = MapShape.Random;
    // 벽과 엄폐물 칸 수 / 장애물 배치 전 내부 바닥 수. 부시는 따로 BushDensity로 정한다. 범위: 0~1.
    public float ObstacleDensity { get; set; } = 0.15f;

    // 부시 칸 수 / 장애물 배치 전 내부 바닥 수. 벽과 엄폐물을 다 세운 뒤 남은 바닥에 두므로
    // 이 값만 바꾸면 벽과 엄폐물은 그대로다. 0이면 부시가 없다. 범위: 0~1.
    public float BushDensity { get; set; } = 0.05f;
    public MapSymmetry Symmetry { get; set; } = MapSymmetry.Rotate180;

    // 0이면 변형 없음, 1이면 가장 강한 변형.
    public float NoiseStrength { get; set; } = 0.5f;

    // 작은 값은 작은 요철, 큰 값은 넓은 굴곡을 만든다. 범위: 0.25~2.
    public float NoiseScale { get; set; } = 0.75f;
}

public enum BotDifficulty { Easy, Normal, Hard }

// AI 대전 로비 화면의 "게임 설정" 탭에서 고르는 값. 이 클라이언트 혼자 쓰므로 서버와 주고받는 DuelSettings에는 넣지 않는다.
public sealed class AiDuelOptions
{
    // 기본 완드. 온라인 대전에서 들고 시작하는 무기와 같다.
    public const byte DefaultWeapon = 1;

    // WeaponSpawnCatalog의 무기 id.
    public byte MyWeapon = DefaultWeapon;
    public byte OpponentWeapon = DefaultWeapon;
    public BotDifficulty Difficulty = BotDifficulty.Normal;

    public AiDuelOptions Clone() => (AiDuelOptions)MemberwiseClone();
}

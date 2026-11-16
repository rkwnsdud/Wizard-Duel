// 로비에서 서버와 클라이언트가 주고받는 이름 붙은 메시지 정의 표다.
internal static class LobbyMessages
{
    public const int BufferSize = 256;

    public static readonly NamedMessageChannel Snapshot =
        new NamedMessageChannel("WizardDuel.LobbySnapshot", BufferSize, BufferSize);

    public static readonly NamedMessageChannel Settings =
        new NamedMessageChannel("WizardDuel.LobbySettings", BufferSize, BufferSize);

    public static readonly NamedMessageChannel Error =
        new NamedMessageChannel("WizardDuel.LobbyError", BufferSize, BufferSize);

    public static readonly NamedMessageChannel Start =
        new NamedMessageChannel("WizardDuel.LobbyStart", BufferSize, BufferSize);

    public static readonly NamedMessageChannel StartRejected =
        new NamedMessageChannel("WizardDuel.LobbyStartRejected", BufferSize, BufferSize);

    public static readonly NamedMessageChannel MapReady =
        new NamedMessageChannel("WizardDuel.LobbyMapReady", BufferSize, BufferSize);
}

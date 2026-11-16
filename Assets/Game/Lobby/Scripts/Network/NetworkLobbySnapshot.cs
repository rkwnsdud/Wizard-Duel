using System;
using Unity.Netcode;

[Serializable]
public struct NetworkLobbySnapshot : INetworkSerializable
{
    public string Code;
    public ulong HostClientId;
    public bool OpponentPresent;
    public ulong GuestClientId;
    public uint SettingsRevision;
    // 이 방에서 몇 번째 판인지. 결과 뒤 로비로 돌아갈 때만 오른다.
    public uint Round;
    public LobbyPhase Phase;
    // 결과 단계의 승자. 0은 방장(파랑), 1은 참가자(빨강). 다른 단계에서는 0이다.
    public byte WinnerSlot;
    public NetworkMapSettings Settings;

    public bool IsValidFor(ulong clientId)
    {
        bool isMember = HostClientId == clientId || (OpponentPresent && GuestClientId == clientId);
        if (!isMember || (OpponentPresent && GuestClientId == HostClientId)) return false;
        if (!LobbySession.IsValidCode(Code)) return false;
        // 전투 준비와 전투, 결과는 두 명이 모두 있어야 생긴다.
        if (!Enum.IsDefined(typeof(LobbyPhase), Phase) || (Phase != LobbyPhase.Lobby && !OpponentPresent)) return false;
        if (WinnerSlot > 1 || (Phase != LobbyPhase.Result && WinnerSlot != 0)) return false;
        return DuelSettings.ValidateSettings(new DuelSettings { Map = Settings.ToSettings() }, out _);
    }

    // 같은 방에서 previous 다음에 올 수 있는 스냅샷인지. 설정 번호는 줄지 않고,
    // 단계는 한 판 안에서 앞으로만 가거나, 결과를 본 뒤 바로 다음 판의 로비로 돌아간다.
    public bool Follows(NetworkLobbySnapshot previous)
    {
        if (Code != previous.Code || HostClientId != previous.HostClientId ||
            SettingsRevision < previous.SettingsRevision) return false;
        if (Round == previous.Round) return Phase >= previous.Phase;
        return Round == previous.Round + 1 && previous.Phase == LobbyPhase.Result && Phase == LobbyPhase.Lobby;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        // 기본 구조체도 안전하게 전송할 수 있도록 null 코드는 빈 문자열로 보낸다.
        if (serializer.IsWriter && Code == null) Code = string.Empty;
        serializer.SerializeValue(ref Code);
        serializer.SerializeValue(ref HostClientId);
        serializer.SerializeValue(ref OpponentPresent);
        serializer.SerializeValue(ref GuestClientId);
        serializer.SerializeValue(ref SettingsRevision);
        serializer.SerializeValue(ref Round);
        serializer.SerializeValue(ref Phase);
        serializer.SerializeValue(ref WinnerSlot);
        serializer.SerializeValue(ref Settings);
    }
}

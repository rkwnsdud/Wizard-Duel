using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// 이름 붙은 Netcode 메시지의 채널 정의, 송신 버퍼 관리, 안전한 수신 역직렬화를 한곳에 모은다.
public readonly struct NamedMessageChannel
{
    public NamedMessageChannel(
        string name,
        int bufferSize,
        int maxReceiveBytes = 0,
        NetworkDelivery delivery = NetworkDelivery.ReliableSequenced)
    {
        Name = name;
        BufferSize = bufferSize;
        MaxReceiveBytes = maxReceiveBytes;
        Delivery = delivery;
    }

    public string Name { get; }
    public int BufferSize { get; }
    public int MaxReceiveBytes { get; }
    public NetworkDelivery Delivery { get; }

    public NamedMessageBinding Bind(CustomMessagingManager.HandleNamedMessageDelegate handler) =>
        new NamedMessageBinding(this, handler);

    public void Send(NetworkManager manager, ulong clientId, Action<FastBufferWriter> write)
    {
        if (manager == null || manager.ShutdownInProgress || manager.CustomMessagingManager == null) return;
        using var writer = new FastBufferWriter(BufferSize, Allocator.Temp);
        write(writer);
        manager.CustomMessagingManager.SendNamedMessage(Name, clientId, writer, Delivery);
    }

    public void SendToRecipients(
        NetworkManager manager,
        ReadOnlySpan<ulong> clientIds,
        Func<ulong, bool> canSend,
        Action<FastBufferWriter> write)
    {
        if (manager == null || manager.ShutdownInProgress || manager.CustomMessagingManager == null) return;
        using var writer = new FastBufferWriter(BufferSize, Allocator.Temp);
        write(writer);
        for (int i = 0; i < clientIds.Length; i++)
        {
            ulong clientId = clientIds[i];
            if (canSend == null || canSend(clientId))
                manager.CustomMessagingManager.SendNamedMessage(Name, clientId, writer, Delivery);
        }
    }

    public bool TryRead(FastBufferReader reader, Func<FastBufferReader, bool> read, string errorContext)
    {
        try
        {
            if (MaxReceiveBytes > 0 && reader.Length - reader.Position > MaxReceiveBytes)
                throw new InvalidOperationException("The message exceeds the maximum buffer size.");
            if (!read(reader)) return false;
            if (reader.Position != reader.Length)
                throw new InvalidOperationException("The message payload length is invalid.");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"{errorContext}: {exception.Message}");
            return false;
        }
    }
}

public readonly struct NamedMessageBinding
{
    public NamedMessageBinding(
        NamedMessageChannel channel,
        CustomMessagingManager.HandleNamedMessageDelegate handler)
    {
        Channel = channel;
        Handler = handler;
    }

    public NamedMessageChannel Channel { get; }
    public CustomMessagingManager.HandleNamedMessageDelegate Handler { get; }
}

// 한 역할이 다루는 이름 붙은 메시지 표를 일괄 등록·해제한다.
public sealed class NamedMessageGroup
{
    readonly NamedMessageBinding[] bindings;
    NetworkManager registeredManager;

    public NamedMessageGroup(params NamedMessageBinding[] bindings)
    {
        this.bindings = bindings ?? Array.Empty<NamedMessageBinding>();
    }

    public bool IsRegistered => registeredManager != null;

    public void Register(NetworkManager manager)
    {
        if (manager == null || manager.CustomMessagingManager == null) return;
        if (registeredManager == manager) return;
        Unregister();
        for (int i = 0; i < bindings.Length; i++)
            manager.CustomMessagingManager.RegisterNamedMessageHandler(bindings[i].Channel.Name, bindings[i].Handler);
        registeredManager = manager;
    }

    public void Unregister()
    {
        if (registeredManager != null && registeredManager.CustomMessagingManager != null)
        {
            for (int i = 0; i < bindings.Length; i++)
                registeredManager.CustomMessagingManager.UnregisterNamedMessageHandler(bindings[i].Channel.Name);
        }
        registeredManager = null;
    }
}

#if UNITY_INCLUDE_TESTS
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

public class NamedMessageChannelTests
{
    [Test]
    public void TryRead_ReadsExactPayloadAndRejectsTrailingOrOversizeOrCorruptPayloads()
    {
        var channel = new NamedMessageChannel("WizardDuel.TestChannel", 64, maxReceiveBytes: 8);

        using (var okWriter = new FastBufferWriter(16, Allocator.Temp))
        {
            okWriter.WriteValueSafe(42u);
            using var okReader = new FastBufferReader(okWriter, Allocator.Temp);
            uint value = 0;
            bool ok = channel.TryRead(okReader, r =>
            {
                r.ReadValueSafe(out value);
                return true;
            }, "[Test]");
            Assert.That(ok, Is.True);
            Assert.That(value, Is.EqualTo(42u));
        }

        using (var trailingWriter = new FastBufferWriter(16, Allocator.Temp))
        {
            trailingWriter.WriteValueSafe(42u);
            trailingWriter.WriteValueSafe((byte)1);
            using var trailingReader = new FastBufferReader(trailingWriter, Allocator.Temp);
            LogAssert.Expect(LogType.Warning, new Regex("\\[Test\\]"));
            Assert.That(channel.TryRead(trailingReader, r =>
            {
                r.ReadValueSafe(out uint _);
                return true;
            }, "[Test]"), Is.False);
        }

        using (var oversizeWriter = new FastBufferWriter(32, Allocator.Temp))
        {
            oversizeWriter.WriteValueSafe(1u);
            oversizeWriter.WriteValueSafe(2u);
            oversizeWriter.WriteValueSafe(3u);
            using var oversizeReader = new FastBufferReader(oversizeWriter, Allocator.Temp);
            LogAssert.Expect(LogType.Warning, new Regex("\\[Test\\]"));
            Assert.That(channel.TryRead(oversizeReader, r => true, "[Test]"), Is.False);
        }

        using (var emptyReader = new FastBufferReader(System.Array.Empty<byte>(), Allocator.Temp))
        {
            LogAssert.Expect(LogType.Warning, new Regex("\\[Test\\]"));
            Assert.That(channel.TryRead(emptyReader, r =>
            {
                r.ReadValueSafe(out uint _);
                return true;
            }, "[Test]"), Is.False);
        }
    }

    [Test]
    public void NamedMessageGroup_IgnoresNullManagerSafely()
    {
        var channel = new NamedMessageChannel("WizardDuel.TestGroup", 32);
        var group = new NamedMessageGroup(channel.Bind((_, __) => { }));
        group.Register(null);
        Assert.That(group.IsRegistered, Is.False);
        group.Unregister();
        Assert.That(group.IsRegistered, Is.False);
    }
}
#endif

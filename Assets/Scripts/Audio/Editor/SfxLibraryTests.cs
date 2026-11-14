#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using NUnit.Framework;

// 효과음 표(SfxLibrary)에 빠진 소리가 없는지 본다. 표에 없는 소리는 게임에서 오류 없이 그냥 안 난다.
public class SfxLibraryTests
{
    [Test]
    public void EverySfxId_HasOneEntryWithAClip()
    {
        SfxLibrary library = Sfx.Library;
        Assert.That(library, Is.Not.Null, "Assets/Resources/SfxLibrary.asset");
        foreach (SfxId id in Enum.GetValues(typeof(SfxId)))
        {
            Assert.That(library.Entries.Count(entry => entry.id == id), Is.EqualTo(1), id.ToString());
            Assert.That(library.Find(id).clip, Is.Not.Null, id.ToString());
        }
    }
}
#endif

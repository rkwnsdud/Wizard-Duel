using System;
using UnityEngine;

// 게임의 모든 효과음과 그 크기·속도·음 높이를 모은 표. Assets/Resources/SfxLibrary.asset 하나만 쓴다.
// 값은 효과음 패널(Tools/Wizard Duel/효과음 패널)에서 들으면서 고친다.
public sealed class SfxLibrary : ScriptableObject
{
    public const string ResourcePath = "SfxLibrary";

    [Serializable]
    public sealed class Entry
    {
        public SfxId id;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
        [Range(.5f, 2f), Tooltip("1보다 크면 빨라지고 짧아진다. 음 높이는 그대로다.")]
        public float speed = 1f;
        [Range(.5f, 2f), Tooltip("1보다 크면 음이 높아진다. 길이는 그대로다.")]
        public float pitch = 1f;
        [Range(0f, .3f), Tooltip("재생할 때마다 속도와 음 높이를 함께 ±이 비율만큼 무작위로 바꾼다.")]
        public float pitchVariation;
    }

    [SerializeField] Entry[] entries = Array.Empty<Entry>();
    public Entry[] Entries => entries;

    public Entry Find(SfxId id) => Array.Find(entries, entry => entry.id == id);
}

using System.Collections.Generic;
using UnityEngine;

// 효과음을 내는 유일한 통로. 크기·속도·음 높이는 SfxLibrary에서만 읽으므로, 플레이 중에 표를 고치면 다음 재생부터 바로 반영된다.
// 전부 2D로 낸다. 카메라(리스너)가 캐릭터에서 13m쯤 떨어져 있어 3D 소리는 6%쯤으로 줄어 거의 안 들린다.
// 소리마다 AudioSource를 따로 써서 높이를 따로 줄 수 있고, 무기를 바꿔 무기 오브젝트가 꺼져도 소리가 끊기지 않는다.
public static class Sfx
{
    const int MaxVoices = 16;

    sealed class Voice
    {
        public AudioSource source;
        public SfxId id;
    }

    sealed class Stretched
    {
        public AudioClip source, made;
        public float factor;
    }

    static SfxLibrary library;
    static GameObject host;
    static readonly Dictionary<SfxId, Stretched> stretched = new Dictionary<SfxId, Stretched>();
    // 재생을 시작한 순서대로 둔다. 맨 앞이 가장 오래된 소리다.
    static readonly List<Voice> voices = new List<Voice>();

    public static SfxLibrary Library =>
        library != null ? library : library = Resources.Load<SfxLibrary>(SfxLibrary.ResourcePath);

    public static void Play(SfxId id)
    {
        // 편집 모드 테스트가 무기를 쏠 때와 창 없이 도는 서버에서는 소리를 내지 않는다.
        if (!Application.isPlaying || Application.isBatchMode) return;
        SfxLibrary.Entry entry = Library != null ? Library.Find(id) : null;
        if (entry == null || entry.clip == null) return;
        Voice voice = TakeVoice();
        voice.id = id;
        Apply(voice.source, entry);
        voice.source.Play();
    }

    // 다시 울리면 안 되는 소리(알림)를 끊을 때 쓴다.
    public static void Stop(SfxId id)
    {
        if (host == null) return;
        foreach (Voice voice in voices)
            if (voice.id == id && voice.source.isPlaying) voice.source.Stop();
    }

    // 효과음 패널의 미리 듣기도 게임과 같은 값으로 들리도록 여기서 한 번에 정한다.
    // AudioSource.pitch는 속도와 음 높이를 함께 바꾼다. 그래서 pitch에는 음 높이를 주고,
    // 그 때문에 같이 변한 길이는 클립을 미리 늘이거나 줄여서(ClipFor) 속도 값에 맞춘다.
    public static void Apply(AudioSource source, SfxLibrary.Entry entry)
    {
        source.clip = ClipFor(entry);
        source.volume = entry.volume;
        source.pitch = entry.pitch * (1f + Random.Range(-entry.pitchVariation, entry.pitchVariation));
    }

    // 속도와 음 높이가 같으면 원본을 그대로 쓴다. 다르면 길이를 (음 높이 ÷ 속도)배로 바꾼 클립을 만들어 두고 쓴다.
    static AudioClip ClipFor(SfxLibrary.Entry entry)
    {
        float factor = Mathf.Round(entry.pitch / Mathf.Max(.01f, entry.speed) * 100f) / 100f;
        if (Mathf.Approximately(factor, 1f)) return entry.clip;
        if (stretched.TryGetValue(entry.id, out Stretched cached))
        {
            if (cached.made != null && cached.source == entry.clip && cached.factor == factor) return cached.made;
            // 표의 값이 바뀌었다. 전에 만든 클립은 버린다.
            if (cached.made != null && cached.made != cached.source) Discard(cached.made);
        }
        AudioClip made = Stretch(entry.clip, factor);
        stretched[entry.id] = new Stretched { source = entry.clip, factor = factor, made = made };
        return made;
    }

    static AudioClip Stretch(AudioClip clip, float factor)
    {
        if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
        var samples = new float[clip.samples * clip.channels];
        if (!clip.GetData(samples, 0))
        {
            // 압축한 채로 메모리에 두거나 스트리밍하는 클립은 읽을 수 없다. Load Type을 Decompress On Load로 둔다.
            Debug.LogWarning($"Sfx: '{clip.name}'의 소리 데이터를 읽지 못해 속도와 음 높이를 따로 줄 수 없습니다.", clip);
            return clip;
        }
        float[] result = SfxStretch.Stretch(samples, clip.channels, clip.frequency, factor);
        AudioClip made = AudioClip.Create(clip.name, result.Length / clip.channels, clip.channels, clip.frequency, false);
        made.SetData(result, 0);
        return made;
    }

    static void Discard(AudioClip clip)
    {
        if (Application.isPlaying) Object.Destroy(clip);
        else Object.DestroyImmediate(clip);
    }

    // 클립을 늘이고 줄이는 데 수십 ms가 걸린다. 전투 중 첫 소리에서 끊기지 않게 시작할 때 만들어 둔다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Warm()
    {
        if (Application.isBatchMode || Library == null) return;
        foreach (SfxLibrary.Entry entry in Library.Entries)
            if (entry.clip != null) ClipFor(entry);
    }

    static Voice TakeVoice()
    {
        if (host == null)
        {
            host = new GameObject("Sfx");
            Object.DontDestroyOnLoad(host);
            voices.Clear();
        }
        int index = voices.FindIndex(voice => !voice.source.isPlaying);
        Voice taken;
        if (index >= 0) taken = voices[index];
        else if (voices.Count < MaxVoices)
        {
            AudioSource source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            taken = new Voice { source = source };
        }
        else
        {
            // 전부 울리는 중이면 가장 오래된 소리를 끊고 쓴다.
            index = 0;
            taken = voices[0];
            taken.source.Stop();
        }
        if (index >= 0) voices.RemoveAt(index);
        voices.Add(taken);
        return taken;
    }
}

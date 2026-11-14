#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

// 속도와 음 높이를 따로 주는 처리. 길이만 바뀌고 음 높이·크기·앞머리는 그대로여야 한다.
public class SfxStretchTests
{
    const int Rate = 48000;
    const float Hertz = 440f;

    // 첫 샘플부터 최대 크기로 시작하는 음. 타격음처럼 앞머리가 바로 나온다.
    static float[] Tone(float seconds, int channels)
    {
        int frames = Mathf.RoundToInt(Rate * seconds);
        var samples = new float[frames * channels];
        for (int i = 0; i < frames; i++)
        for (int c = 0; c < channels; c++)
            samples[i * channels + c] = .5f * Mathf.Cos(2f * Mathf.PI * Hertz * i / Rate);
        return samples;
    }

    // 첫 채널이 아래에서 위로 0을 지나는 횟수로 음 높이를 잰다.
    static float MeasuredHertz(float[] samples, int channels)
    {
        int frames = samples.Length / channels, crossings = 0;
        for (int i = 1; i < frames; i++)
            if (samples[(i - 1) * channels] < 0f && samples[i * channels] >= 0f) crossings++;
        return crossings * (float)Rate / frames;
    }

    static float Rms(float[] samples)
    {
        double sum = 0;
        foreach (float sample in samples) sum += sample * sample;
        return Mathf.Sqrt((float)(sum / samples.Length));
    }

    [TestCase(.5f)]
    [TestCase(.8f)]
    [TestCase(1.25f)]
    [TestCase(2f)]
    public void Stretch_ChangesLength_KeepsPitchAndLoudness(float factor)
    {
        float[] tone = Tone(1f, 2);
        float[] result = SfxStretch.Stretch(tone, 2, Rate, factor);
        Assert.That(result.Length, Is.EqualTo(Mathf.RoundToInt(Rate * factor) * 2));
        Assert.That(MeasuredHertz(result, 2), Is.EqualTo(Hertz).Within(Hertz * .02f));
        Assert.That(Rms(result), Is.EqualTo(Rms(tone)).Within(Rms(tone) * .1f));
    }

    [Test]
    public void Stretch_KeepsTheAttackAtTheStart()
    {
        float[] tone = Tone(.5f, 1);
        float[] result = SfxStretch.Stretch(tone, 1, Rate, 1.5f);
        for (int i = 0; i < Rate / 200; i++) Assert.That(result[i], Is.EqualTo(tone[i]).Within(1e-3f), "sample " + i);
    }

    // 표의 속도·음 높이가 실제 재생에 어떻게 옮겨지는지: 음 높이는 AudioSource.pitch로, 길이는 (원래 길이 ÷ 속도)로.
    [TestCase(1f, 1.5f)]
    [TestCase(2f, 1f)]
    [TestCase(.8f, 1.25f)]
    [TestCase(1.1f, 1.1f)]
    public void Apply_PlaysAtThePitch_ForLengthOverSpeed(float speed, float pitch)
    {
        AudioClip clip = AudioClip.Create("tone", Rate, 1, Rate, false);
        clip.SetData(Tone(1f, 1), 0);
        var holder = new GameObject("source", typeof(AudioSource)) { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var source = holder.GetComponent<AudioSource>();
            Sfx.Apply(source, new SfxLibrary.Entry { id = SfxId.WandFire, clip = clip, speed = speed, pitch = pitch });
            Assert.That(source.pitch, Is.EqualTo(pitch).Within(1e-4f));
            Assert.That(source.clip.length / source.pitch, Is.EqualTo(1f / speed).Within(.01f));
            // 속도와 음 높이가 같으면 원본을 그대로 쓴다(예전 pitch와 같은 소리).
            if (speed == pitch) Assert.That(source.clip, Is.SameAs(clip));
        }
        finally
        {
            Object.DestroyImmediate(holder);
            Object.DestroyImmediate(clip);
        }
    }
}
#endif

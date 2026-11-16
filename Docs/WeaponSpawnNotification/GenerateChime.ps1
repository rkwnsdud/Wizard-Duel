param([string]$OutputPath = (Join-Path $PSScriptRoot '../../Assets/Audio/Clips/Notifications/WeaponSpawnChime.wav'))
# Original additive synthesis; no external recordings or samples. License: 0BSD.
Add-Type -TypeDefinition @'
using System;
using System.IO;
public static class WeaponSpawnChimeGenerator
{
    public static void Generate(string path)
    {
        const int rate = 48000;
        const double duration = 1.8;
        int count = (int)(rate * duration);
        var dry = new double[count];
        var samples = new double[count];
        double[] tones = { 523.251, 783.991, 1046.502 };
        double[] onsets = { 0, .075, .15 };
        for (int i = 0; i < count; i++)
        {
            double t = i / (double)rate;
            for (int tone = 0; tone < tones.Length; tone++)
            {
                double age = t - onsets[tone];
                if (age < 0) continue;
                double attack = 1 - Math.Exp(-age / .035);
                double envelope = attack * attack * Math.Exp(-age / .45);
                double phase = 2 * Math.PI * tones[tone] * age;
                dry[i] += envelope * (Math.Sin(phase + .08 * Math.Sin(2 * Math.PI * 3.1 * age))
                    + .18 * Math.Sin(phase * 2.002) + .06 * Math.Sin(phase * 3)) / (1 + tone * .35);
            }
        }
        double peak = 0;
        for (int i = 0; i < count; i++)
        {
            double value = dry[i];
            if (i >= 3840) value += dry[i - 3840] * .18;
            if (i >= 7440) value += dry[i - 7440] * .10;
            double tail = Math.Min(1, (count - 1 - i) / (rate * .35));
            samples[i] = value * tail * tail;
            peak = Math.Max(peak, Math.Abs(samples[i]));
        }
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
            writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
            foreach (double sample in samples) writer.Write((short)Math.Round(sample / peak * .35 * short.MaxValue));
        }
    }
}
'@
[WeaponSpawnChimeGenerator]::Generate([IO.Path]::GetFullPath($OutputPath))

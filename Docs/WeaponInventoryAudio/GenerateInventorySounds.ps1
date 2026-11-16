param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../../Assets/Audio/Clips/Inventory'))
# Original procedural synthesis. No recordings, samples, or external libraries. License: 0BSD.
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;

public static class WeaponInventorySoundGenerator
{
    const int Rate = 48000;

    public static void Generate(string directory)
    {
        Directory.CreateDirectory(directory);
        WriteWave(Path.Combine(directory, "WeaponPickup.wav"), Pickup(), .62);
    }

    static double[] Pickup()
    {
        var samples = new double[(int)(Rate * .54)];
        double[] notes = { 523.25, 659.25, 880 };
        double[] starts = { .006, .082, .158 };
        double[] decays = { .062, .075, .087 };
        double[] strengths = { .68, .78, .58 };
        for (int i = 0; i < samples.Length; i++)
        {
            double t = i / (double)Rate;
            for (int n = 0; n < notes.Length; n++)
            {
                double age = t - starts[n];
                if (age < 0) continue;
                double envelope = (1 - Math.Exp(-age / .0025)) * Math.Exp(-age / decays[n]);
                double phase = 2 * Math.PI * notes[n] * age;
                samples[i] += strengths[n] * envelope *
                    (Math.Sin(phase) + .16 * Math.Sin(2.37 * phase) + .06 * Math.Sin(4.11 * phase));
                samples[i] += .035 * Math.Exp(-age / .006) * Math.Sin(2 * Math.PI * 1450 * age);
            }
            // The final note has already decayed before this long, smooth release ends.
            if (t > .36) samples[i] *= .5 * (1 + Math.Cos(Math.PI * (t - .36) / .18));
        }
        return samples;
    }

    static void WriteWave(string path, double[] samples, double targetPeak)
    {
        double peak = 0;
        foreach (double sample in samples) peak = Math.Max(peak, Math.Abs(sample));
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples.Length * 2);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(Rate); writer.Write(Rate * 2);
            writer.Write((short)2); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples.Length * 2);
            for (int i = 0; i < samples.Length; i++)
            {
                double fade = Math.Min(1, (samples.Length - 1 - i) / (Rate * .035));
                writer.Write((short)Math.Round(samples[i] / peak * targetPeak * fade * short.MaxValue));
            }
        }
    }
}
'@
[WeaponInventorySoundGenerator]::Generate([IO.Path]::GetFullPath($OutputDirectory))

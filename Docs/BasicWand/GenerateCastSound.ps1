param([string]$OutputPath = (Join-Path $PSScriptRoot '../../Assets/Audio/Clips/BasicWand/BasicWandCast.wav'))
# Original procedural synthesis for the short, non-elemental Arcane Bolt launch sound.
# SPDX-License-Identifier: 0BSD
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;

public static class BasicWandCastSoundGenerator
{
    const int Rate = 48000;

    public static void Generate(string path)
    {
        var samples = new double[(int)(Rate * .34)];
        uint seed = 0x41ac3e17;
        double fastNoise = 0;
        double slowNoise = 0;

        for (int i = 0; i < samples.Length; i++)
        {
            double t = i / (double)Rate;
            seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
            double noise = seed / (double)uint.MaxValue * 2 - 1;
            fastNoise += .28 * (noise - fastNoise);
            slowNoise += .035 * (noise - slowNoise);
            double airyTexture = fastNoise - slowNoise;

            // A tiny breath of air leads the bolt; the bright, warm snap lands at 28 ms.
            double breath = .4 * airyTexture * (1 - Math.Exp(-t / .012)) * Math.Exp(-t / .075);
            double age = t - .028;
            double launch = 0;
            if (age >= 0)
            {
                double onset = 1 - Math.Exp(-age / .0015);
                double snap = (.45 * airyTexture + .12 * Math.Sin(2 * Math.PI * 1750 * age))
                    * Math.Exp(-age / .018);
                double core = (.46 * Math.Sin(2 * Math.PI * 698.46 * age)
                    + .19 * Math.Sin(2 * Math.PI * 1058.5 * age)
                    + .065 * Math.Sin(2 * Math.PI * 1680 * age)) * Math.Exp(-age / .065);
                double body = .23 * Math.Sin(2 * Math.PI * 247 * age) * Math.Exp(-age / .033);
                launch = onset * (snap + core + body);
            }

            samples[i] = breath + launch;
            if (t > .26) samples[i] *= .5 * (1 + Math.Cos(Math.PI * (t - .26) / .08));
        }

        double peak = 0;
        foreach (double sample in samples) peak = Math.Max(peak, Math.Abs(sample));
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples.Length * 2);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(Rate); writer.Write(Rate * 2);
            writer.Write((short)2); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples.Length * 2);
            foreach (double sample in samples)
                writer.Write((short)Math.Round(sample / peak * .68 * short.MaxValue));
        }
    }
}
'@
[BasicWandCastSoundGenerator]::Generate([IO.Path]::GetFullPath($OutputPath))

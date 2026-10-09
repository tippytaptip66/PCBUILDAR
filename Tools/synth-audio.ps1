param([string]$OutDir)

Add-Type -TypeDefinition @'
using System;
using System.IO;

public static class Synth
{
    public const int Rate = 44100;

    public static void WriteWav(string path, float[] s)
    {
        using (var fs = new FileStream(path, FileMode.Create))
        using (var w = new BinaryWriter(fs))
        {
            int dataBytes = s.Length * 2;
            w.Write(new char[] { 'R', 'I', 'F', 'F' });
            w.Write(36 + dataBytes);
            w.Write(new char[] { 'W', 'A', 'V', 'E' });
            w.Write(new char[] { 'f', 'm', 't', ' ' });
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(Rate);
            w.Write(Rate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write(new char[] { 'd', 'a', 't', 'a' });
            w.Write(dataBytes);
            for (int i = 0; i < s.Length; i++)
            {
                float v = s[i];
                if (v > 1f) v = 1f;
                if (v < -1f) v = -1f;
                w.Write((short)(v * 32767f));
            }
        }
    }

    static void Normalize(float[] s, float peak)
    {
        float max = 0f;
        for (int i = 0; i < s.Length; i++) { float a = Math.Abs(s[i]); if (a > max) max = a; }
        if (max <= 0.0001f) return;
        float g = peak / max;
        for (int i = 0; i < s.Length; i++) s[i] *= g;
    }

    // ---------------------------------------------------------------- confetti
    public static void Confetti(string path)
    {
        int n = (int)(1.15 * Rate);
        var s = new float[n];
        var rnd = new Random(20260918);

        // Rising major-pentatonic sparkle chime.
        double[] notes = { 1046.5, 1318.5, 1568.0, 1760.0, 2093.0, 2637.0 };
        for (int k = 0; k < notes.Length; k++)
        {
            double f = notes[k];
            int start = (int)(k * 0.055 * Rate);
            double amp = 0.9 - k * 0.07;
            for (int i = start; i < n; i++)
            {
                double t = (i - start) / (double)Rate;
                double env = Math.Min(1.0, t / 0.004) * Math.Exp(-t / 0.28);
                if (t > 0.05 && env < 0.0005) break;
                double v = Math.Sin(2 * Math.PI * f * t)
                         + 0.45 * Math.Sin(4 * Math.PI * f * t)
                         + 0.20 * Math.Sin(6 * Math.PI * f * t);
                s[i] += (float)(v * env * amp * 0.33);
            }
        }

        // Glitter.
        for (int g = 0; g < 46; g++)
        {
            double at = 0.03 + rnd.NextDouble() * 0.85;
            double f = 2600 + rnd.NextDouble() * 3600;
            int start = (int)(at * Rate);
            double amp = 0.12 + rnd.NextDouble() * 0.12;
            for (int i = start; i < n; i++)
            {
                double t = (i - start) / (double)Rate;
                double env = Math.Exp(-t / 0.035);
                if (env < 0.001) break;
                s[i] += (float)(Math.Sin(2 * Math.PI * f * t) * env * amp);
            }
        }

        // Soft popper at the very start.
        double lp = 0;
        for (int i = 0; i < (int)(0.09 * Rate); i++)
        {
            double t = i / (double)Rate;
            lp += ((rnd.NextDouble() * 2 - 1) - lp) * 0.25;
            s[i] += (float)(lp * Math.Exp(-t / 0.02) * 0.5);
        }

        Normalize(s, 0.88f);
        int fade = (int)(0.03 * Rate);
        for (int i = 0; i < fade; i++) s[n - 1 - i] *= (float)(i / (double)fade);
        WriteWav(path, s);
    }

    // ------------------------------------------------------------------- music
    static Random _r = new Random(777);

    static void Pad(float[] s, double t0, double f, double amp)
    {
        double len = 7.2;
        int start = (int)(t0 * Rate);
        int count = (int)(len * Rate);
        for (int i = 0; i < count; i++)
        {
            int idx = start + i;
            if (idx >= s.Length) break;
            double t = i / (double)Rate;
            double env = Math.Min(1.0, t / 1.6) * Math.Min(1.0, (len - t) / 1.8);
            if (env <= 0) continue;
            double v = Math.Sin(2 * Math.PI * f * t)
                     + 0.30 * Math.Sin(2 * Math.PI * f * 2 * t)
                     + 0.12 * Math.Sin(2 * Math.PI * f * 3 * t)
                     + 0.50 * Math.Sin(2 * Math.PI * f * 1.004 * t);
            s[idx] += (float)(v * env * amp);
        }
    }

    static void Bells(float[] s, double t0, double[] chord)
    {
        for (int k = 0; k < 4; k++)
        {
            double at = t0 + 0.6 + k * 1.45;
            double f = chord[1 + _r.Next(3)] * 4;
            int start = (int)(at * Rate);
            double amp = 0.10 + _r.NextDouble() * 0.05;
            for (int i = 0; i < (int)(1.6 * Rate); i++)
            {
                int idx = start + i;
                if (idx >= s.Length) break;
                double t = i / (double)Rate;
                double env = Math.Min(1.0, t / 0.012) * Math.Exp(-t / 0.5);
                s[idx] += (float)((Math.Sin(2 * Math.PI * f * t) + 0.25 * Math.Sin(4 * Math.PI * f * t)) * env * amp);
            }
        }
    }

    public static void Music(string path)
    {
        double loop = 24.0, extra = 1.5, chordLen = 6.0;
        int n = (int)((loop + extra) * Rate);
        var s = new float[n];

        double[][] chords = {
            new double[] { 130.81, 164.81, 196.00, 261.63 }, // C
            new double[] { 110.00, 130.81, 164.81, 220.00 }, // Am
            new double[] {  87.31, 110.00, 130.81, 174.61 }, // F
            new double[] {  98.00, 123.47, 146.83, 196.00 }, // G
        };

        for (int c = 0; c < 5; c++)                       // the 5th repeats C, for the loop join
        {
            double[] ch = chords[c % 4];
            double t0 = c * chordLen;
            if (t0 >= loop + extra) break;
            for (int v = 0; v < ch.Length; v++) Pad(s, t0, ch[v], 0.05);
            Pad(s, t0, ch[0] / 2.0, 0.045);               // drone
            Bells(s, t0, ch);
        }

        // Crossfade the overhang onto the head so the clip loops without a seam.
        int fade = (int)(extra * Rate);
        int loopSamples = (int)(loop * Rate);
        for (int i = 0; i < fade; i++)
        {
            double x = i / (double)fade;
            s[i] = (float)(s[i] * x + s[loopSamples + i] * (1 - x));
        }

        var outp = new float[loopSamples];
        Array.Copy(s, outp, loopSamples);
        Normalize(outp, 0.55f);
        WriteWav(path, outp);
    }
}
'@

[Synth]::Confetti((Join-Path $OutDir "confetti.wav"))
[Synth]::Music((Join-Path $OutDir "bgmusic.wav"))
Get-ChildItem $OutDir -Filter *.wav | Select-Object Name, Length

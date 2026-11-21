# Hallow Browser 1.6 - synthesises the optional UI sounds and the ambience loop at build time (no recorded or downloaded sounds):
# plain sine tones with envelopes, written as 16-bit mono WAVs. build.ps1 runs this and embeds the files.
#   .\tools\gen-sounds.ps1 -Out <folder>
param([Parameter(Mandatory)] [string]$Out)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Out | Out-Null
if (-not ('HallowSynth' -as [type])) {
Add-Type -TypeDefinition @'
using System; using System.IO;
public static class HallowSynth
{
    public const int Rate = 22050;
    static void Wav(string path, double[] s, double gain, int rate = Rate)
    {
        using (var f = File.Create(path)) using (var w = new BinaryWriter(f))
        {
            int n = s.Length;
            w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' }); w.Write(36 + n * 2);
            w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' }); w.Write(n * 2);
            foreach (var v in s) { var x = Math.Max(-1.0, Math.Min(1.0, v * gain)); w.Write((short)Math.Round(x * 32000)); }
        }
    }
    // a tone gliding from f0 to f1 with a soft attack and an exponential decay, plus a quiet octave for a little shine
    static void Tone(double[] s, int start, double secs, double f0, double f1, double amp, double decay)
    {
        int n = (int)(secs * Rate); double ph = 0, ph2 = 0;
        for (int i = 0; i < n && start + i < s.Length; i++)
        {
            double t = (double)i / Rate, k = (double)i / n, f = f0 + (f1 - f0) * k;
            ph += 2 * Math.PI * f / Rate; ph2 += 2 * Math.PI * f * 2 / Rate;
            double env = Math.Min(1.0, t / 0.006) * Math.Exp(-t * decay) * (1 - Math.Pow(k, 8));
            s[start + i] += amp * env * (Math.Sin(ph) + 0.18 * Math.Sin(ph2));
        }
    }
    public static void All(string dir)
    {
        var open = new double[(int)(0.16 * Rate)]; Tone(open, 0, 0.16, 620, 940, 0.55, 26); Wav(Path.Combine(dir, "open.wav"), open, 0.8);
        var close = new double[(int)(0.15 * Rate)]; Tone(close, 0, 0.15, 900, 540, 0.5, 28); Wav(Path.Combine(dir, "close.wav"), close, 0.8);
        var dl = new double[(int)(0.55 * Rate)]; Tone(dl, 0, 0.30, 659.25, 659.25, 0.45, 9); Tone(dl, (int)(0.12 * Rate), 0.43, 987.77, 987.77, 0.4, 8); Wav(Path.Combine(dir, "download.wav"), dl, 0.8);
        var game = new double[(int)(0.62 * Rate)]; double[] notes = { 523.25, 659.25, 783.99, 1046.5 };
        for (int i = 0; i < notes.Length; i++) Tone(game, (int)(i * 0.085 * Rate), 0.36, notes[i], notes[i] * 1.004, 0.32, 10);
        Wav(Path.Combine(dir, "game.wav"), game, 0.85);
        // ambience: a slow pad that loops without a seam - every frequency makes whole cycles in the 12 s loop (f * 12 is a whole number)
        const int AR = 11025; double L = 12.0; int m = (int)(L * AR); var amb = new double[m];
        double[] fs = { 110.0, 110.25, 164.75, 220.0, 277.25, 329.5 }; double[] am = { 0.30, 0.22, 0.20, 0.16, 0.08, 0.07 };
        for (int i = 0; i < m; i++)
        {
            double t = (double)i / AR, v = 0;
            for (int j = 0; j < fs.Length; j++)
            {
                double lfo = 0.6 + 0.4 * Math.Sin(2 * Math.PI * t * (j % 2 == 0 ? 1.0 / 6 : 1.0 / 4) + j);
                v += am[j] * lfo * Math.Sin(2 * Math.PI * fs[j] * t);
            }
            amb[i] = v;
        }
        Wav(Path.Combine(dir, "ambience.wav"), amb, 0.55, AR);
    }
}
'@
}
[HallowSynth]::All((Resolve-Path $Out).Path)
Get-ChildItem $Out -Filter *.wav | ForEach-Object { "$($_.Name) $($_.Length)" }

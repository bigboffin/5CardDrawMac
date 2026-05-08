using System.Threading.Channels;

namespace FiveCardDraw;

public static class SpeechHelper
{
    public static bool Enabled { get; set; } = true;

    private static readonly Channel<string> _queue =
        Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });

    private static System.Diagnostics.Process? _activeSpeech;
    private static int _pendingCount;   // items queued + currently speaking

    static SpeechHelper()
    {
        Task.Run(ConsumeAsync);
    }

    private static async Task ConsumeAsync()
    {
        await foreach (string args in _queue.Reader.ReadAllAsync())
        {
            try
            {
                if (!Enabled) continue;
                var psi = new System.Diagnostics.ProcessStartInfo("say", args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                _activeSpeech = System.Diagnostics.Process.Start(psi);
                if (_activeSpeech != null)
                    await _activeSpeech.WaitForExitAsync();
            }
            catch { }
            finally
            {
                _activeSpeech = null;
                Interlocked.Decrement(ref _pendingCount);
            }
        }
    }

    public static void Say(string text)
    {
        if (!Enabled) return;
        Interlocked.Increment(ref _pendingCount);
        _queue.Writer.TryWrite($"-v Alex \"{EscapeForShell(text)}\"");
    }

    public static void SayFemale(string text)
    {
        if (!Enabled) return;
        Interlocked.Increment(ref _pendingCount);
        _queue.Writer.TryWrite($"-v Samantha \"{EscapeForShell(text)}\"");
    }

    public static void PlayWinnerTune()
    {
        if (!Enabled) return;
        Task.Run(() =>
        {
            try
            {
                int[] notes = { 392, 523, 659, 784, 659, 784, 1047 };
                int[] durations = { 150, 150, 150, 300, 120, 250, 700 };
                byte[] wav = BuildToneSequence(notes, durations);
                string path = Path.ChangeExtension(Path.GetTempFileName(), ".wav");
                File.WriteAllBytes(path, wav);
                using var proc = System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo("afplay", $"\"{path}\"")
                    { RedirectStandardOutput = true, RedirectStandardError = true });
                proc?.WaitForExit();
                try { File.Delete(path); } catch { }
            }
            catch { }
        });
    }

    private static byte[] BuildToneSequence(int[] frequencies, int[] durationsMs)
    {
        const int sampleRate = 44100;
        const short bitsPerSample = 16;
        const short channels = 1;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
        bw.Write(0);
        bw.Write(new byte[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
        bw.Write(new byte[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
        bw.Write(16);
        bw.Write((short)1);
        bw.Write(channels);
        bw.Write(sampleRate);
        bw.Write(sampleRate * channels * bitsPerSample / 8);
        bw.Write((short)(channels * bitsPerSample / 8));
        bw.Write(bitsPerSample);
        bw.Write(new byte[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
        bw.Write(0);

        long dataStart = ms.Position;

        for (int n = 0; n < frequencies.Length; n++)
        {
            int samples = sampleRate * durationsMs[n] / 1000;
            int fadeSamples = Math.Min(samples / 8, sampleRate / 100);
            for (int i = 0; i < samples; i++)
            {
                double t = (double)i / sampleRate;
                double amp = 0.35;
                if (i < fadeSamples) amp *= (double)i / fadeSamples;
                else if (i > samples - fadeSamples) amp *= (double)(samples - i) / fadeSamples;
                double sample = amp * Math.Sin(2 * Math.PI * frequencies[n] * t);
                bw.Write((short)(sample * short.MaxValue));
            }
        }

        long dataEnd = ms.Position;
        ms.Position = 4; bw.Write((int)(dataEnd - 8));
        ms.Position = 40; bw.Write((int)(dataEnd - dataStart));
        return ms.ToArray();
    }

    public static async Task WaitForSpeechAsync()
    {
        if (!Enabled) return;
        await Task.Delay(300);
        while (Interlocked.CompareExchange(ref _pendingCount, 0, 0) > 0)
            await Task.Delay(100);
        await Task.Delay(150);
    }

    public static void Cleanup()
    {
        _queue.Writer.TryComplete();
        try { _activeSpeech?.Kill(); } catch { }
    }

    private static string EscapeForShell(string text) =>
        text.Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`");
}

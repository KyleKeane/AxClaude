namespace AxClaude.Core.Audio;

/// <summary>
/// The app's sounds as WAV data (16-bit mono PCM), generated in code so that no media file ships: the tick for a
/// new line and the chimes for a sent message and for Claude replying, finishing and asking (FR-7.3, FR-7.8).
/// </summary>
public static class WaveTone
{
    public const int SampleRate = 22050;

    /// <summary>The bytes of the WAV header before the samples.</summary>
    public const int HeaderLength = 44;

    /// <summary>
    /// A sequence of sine notes, each faded in and out over a few milliseconds so that the edges do not click.
    /// A frequency of 0 is a pause of that length.
    /// </summary>
    public static byte[] Notes(double amplitude, params (double Hertz, int Milliseconds)[] notes)
    {
        var total = 0;
        foreach (var note in notes)
        {
            total += Samples(note.Milliseconds);
        }

        var samples = new short[total];
        var at = 0;
        foreach (var (hertz, milliseconds) in notes)
        {
            var count = Samples(milliseconds);
            var fade = Math.Min(count / 2, Samples(4));
            for (var i = 0; i < count; i++)
            {
                var envelope = 1.0;
                if (i < fade)
                {
                    envelope = (double)i / fade;
                }
                else if (i >= count - fade)
                {
                    envelope = (double)(count - 1 - i) / fade;
                }

                samples[at + i] = Sample(hertz, i, amplitude * envelope);
            }

            at += count;
        }

        return Wav(samples);
    }

    /// <summary>
    /// A short tone that dies away at once: heard as one tick. It rises over the first 0.6 ms; a tone that starts
    /// at full volume is heard as a click followed by a separate falling note.
    /// </summary>
    public static byte[] Tick(double amplitude, double hertz, int milliseconds)
    {
        var count = Samples(milliseconds);
        var attack = Math.Min(count / 4, SampleRate / 1500);
        var samples = new short[count];
        for (var i = 0; i < count; i++)
        {
            // Down to about a quarter of a percent at the end, so the cut-off is not heard.
            var envelope = Math.Exp(-6.0 * i / count);
            if (i < attack)
            {
                envelope *= (double)i / attack;
            }

            samples[i] = Sample(hertz, i, amplitude * envelope);
        }

        return Wav(samples);
    }

    private static int Samples(int milliseconds) => milliseconds * SampleRate / 1000;

    private static short Sample(double hertz, int index, double amplitude) =>
        (short)(Math.Sin(2 * Math.PI * hertz * index / SampleRate) * amplitude * short.MaxValue);

    private static byte[] Wav(short[] samples)
    {
        var dataLength = samples.Length * 2;
        var wav = new byte[HeaderLength + dataLength];
        var span = wav.AsSpan();
        "RIFF"u8.CopyTo(span);
        BitConverter.TryWriteBytes(span[4..], 36 + dataLength);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BitConverter.TryWriteBytes(span[16..], 16);                 // fmt chunk length
        BitConverter.TryWriteBytes(span[20..], (short)1);           // PCM
        BitConverter.TryWriteBytes(span[22..], (short)1);           // mono
        BitConverter.TryWriteBytes(span[24..], SampleRate);
        BitConverter.TryWriteBytes(span[28..], SampleRate * 2);     // bytes per second
        BitConverter.TryWriteBytes(span[32..], (short)2);           // block align
        BitConverter.TryWriteBytes(span[34..], (short)16);          // bits per sample
        "data"u8.CopyTo(span[36..]);
        BitConverter.TryWriteBytes(span[40..], dataLength);
        for (var i = 0; i < samples.Length; i++)
        {
            BitConverter.TryWriteBytes(span[(HeaderLength + i * 2)..], samples[i]);
        }

        return wav;
    }
}

using System.Text;
using AxClaude.Core.Audio;

namespace AxClaude.Tests;

public class WaveToneTests
{
    [Fact]
    public void Notes_produce_a_valid_pcm_wav_of_the_requested_length()
    {
        // Lengths that are whole sample counts at 22050 Hz (40 ms is 882 samples; 50 ms would not be whole).
        var wav = WaveTone.Notes(0.5, (440, 100), (0, 40), (880, 60));

        var samples = 200 * WaveTone.SampleRate / 1000;
        Assert.Equal(WaveTone.HeaderLength + samples * 2, wav.Length);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
        Assert.Equal("WAVEfmt ", Encoding.ASCII.GetString(wav, 8, 8));
        Assert.Equal(16, BitConverter.ToInt32(wav, 16));
        Assert.Equal(1, BitConverter.ToInt16(wav, 20));
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));
        Assert.Equal(WaveTone.SampleRate, BitConverter.ToInt32(wav, 24));
        Assert.Equal(WaveTone.SampleRate * 2, BitConverter.ToInt32(wav, 28));
        Assert.Equal(2, BitConverter.ToInt16(wav, 32));
        Assert.Equal(16, BitConverter.ToInt16(wav, 34));
        Assert.Equal("data", Encoding.ASCII.GetString(wav, 36, 4));
        Assert.Equal(samples * 2, BitConverter.ToInt32(wav, 40));
    }

    [Fact]
    public void Notes_stay_within_the_amplitude_and_a_pause_is_silent()
    {
        var wav = WaveTone.Notes(0.5, (440, 100), (0, 40));
        var limit = (int)(0.5 * short.MaxValue) + 1;
        var loudest = 0;
        for (var i = WaveTone.HeaderLength; i < wav.Length; i += 2)
        {
            loudest = Math.Max(loudest, Math.Abs((int)BitConverter.ToInt16(wav, i)));
        }

        Assert.InRange(loudest, limit / 2, limit);

        var pauseStart = WaveTone.HeaderLength + 100 * WaveTone.SampleRate / 1000 * 2;
        for (var i = pauseStart; i < wav.Length; i += 2)
        {
            Assert.Equal(0, BitConverter.ToInt16(wav, i));
        }
    }

    [Fact]
    public void Tick_dies_away()
    {
        var wav = WaveTone.Tick(0.3, 3000, 12);
        var samples = (wav.Length - WaveTone.HeaderLength) / 2;
        Assert.Equal(12 * WaveTone.SampleRate / 1000, samples);

        var firstQuarter = 0;
        var lastQuarter = 0;
        for (var i = 0; i < samples; i++)
        {
            var value = Math.Abs((int)BitConverter.ToInt16(wav, WaveTone.HeaderLength + i * 2));
            if (i < samples / 4)
            {
                firstQuarter = Math.Max(firstQuarter, value);
            }
            else if (i >= samples * 3 / 4)
            {
                lastQuarter = Math.Max(lastQuarter, value);
            }
        }

        Assert.True(firstQuarter > lastQuarter * 4, $"{firstQuarter} should be well above {lastQuarter}");

        // The attack: the second sample is still far below the peak (a tone that starts at full volume clicks).
        var second = Math.Abs((int)BitConverter.ToInt16(wav, WaveTone.HeaderLength + 2));
        Assert.True(second < firstQuarter / 4, $"{second} should be well below {firstQuarter}");
    }
}

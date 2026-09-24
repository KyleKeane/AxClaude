using System.Runtime.InteropServices;
using AxClaude.Core.Audio;

namespace AxClaude;

/// <summary>
/// The app's sounds (FR-7.3, FR-7.8), played through one winmm wave-out device that is opened at the first sound
/// and stays open for the life of the process (D31). Every sound is generated once, pinned and prepared for the
/// device; playing it is a single <c>waveOutWrite</c>, which queues the buffer behind whatever is playing. A chime
/// clears the queue first, so it is never delayed and a tick never cuts it short. A tick is dropped while its own
/// buffer is still queued, so ticks never pile up, and a tick that arrives during a chime follows the chime. Until
/// 1.2.1 the sounds went through <c>PlaySound</c>, which opens and closes the device for every call: ticks in quick
/// succession stuttered and backed up. UI thread only.
/// </summary>
internal static class Sounds
{
    private const uint WAVE_MAPPER = 0xFFFFFFFF;
    private const uint WHDR_INQUEUE = 0x0010;
    private static readonly int HeaderSize = Marshal.SizeOf<WAVEHDR>();
    private static readonly int FlagsOffset = (int)Marshal.OffsetOf<WAVEHDR>(nameof(WAVEHDR.dwFlags));

    /// <summary>A sound: its samples, pinned, and the wave header the device reads and writes, in unmanaged memory.</summary>
    private sealed class Sound
    {
        public Sound(byte[] wav)
        {
            var data = GCHandle.Alloc(wav, GCHandleType.Pinned).AddrOfPinnedObject();
            Header = Marshal.AllocHGlobal(HeaderSize);
            var header = new WAVEHDR { lpData = data + WaveTone.HeaderLength, dwBufferLength = (uint)(wav.Length - WaveTone.HeaderLength) };
            Marshal.StructureToPtr(header, Header, false);
        }

        public nint Header { get; }

        /// <summary>The device has not finished with the buffer: written and not yet played to the end.</summary>
        public bool Queued => (Marshal.ReadInt32(Header, FlagsOffset) & WHDR_INQUEUE) != 0;
    }

    // A typewriter-like strike, a burst of noise over a 1.5 kHz tone that dies away within 10 ms: a line from Claude
    // arrived. Two lower knocks in a row, 12 ms each and rising: a tool call arrived.
    private static readonly Sound LineTick = new(WaveTone.Strikes(0.4, 10, 0.5, 1500));
    private static readonly Sound ToolTick = new(WaveTone.Strikes(0.5, 12, 0.5, 650, 850));

    // C6 alone, a high ping: Claude is ready. A4 alone, low: the message went out. C5, E5, G5 up to a long note:
    // Claude starts replying. The same notes down, G5, E5, C5: the turn is done. Two E5s: Claude needs an answer.
    // A quick C6-E6 trill: the answer notice opened in place of the conversation.
    private static readonly Sound ReadyChime = new(WaveTone.Notes(0.35, (1046.5, 120)));
    private static readonly Sound SentChime = new(WaveTone.Notes(0.35, (440, 80)));
    private static readonly Sound RespondingChime = new(WaveTone.Notes(0.35, (523.25, 90), (659.25, 90), (783.99, 220)));
    private static readonly Sound DoneChime = new(WaveTone.Notes(0.35, (783.99, 90), (659.25, 90), (523.25, 220)));
    private static readonly Sound QuestionChime = new(WaveTone.Notes(0.35, (659.25, 70), (0, 40), (659.25, 110)));
    private static readonly Sound NoticeChime = new(WaveTone.Notes(0.35, (1046.5, 45), (1318.5, 45), (1046.5, 45), (1318.5, 110)));
    private static readonly Sound[] All = [LineTick, ToolTick, ReadyChime, SentChime, RespondingChime, DoneChime, QuestionChime, NoticeChime];

    private static nint _device;
    private static bool _failed;

    /// <summary>A tiny typewriter-like tick: a line from Claude arrived.</summary>
    public static void Click() => Play(LineTick, replace: false);

    /// <summary>Two lower knocks: a tool call arrived.</summary>
    public static void Tool() => Play(ToolTick, replace: false);

    /// <summary>A single high ping: Claude is ready.</summary>
    public static void Ready() => Play(ReadyChime, replace: true);

    /// <summary>A single low note: the message went out.</summary>
    public static void Sent() => Play(SentChime, replace: true);

    /// <summary>Three rising notes ending on a long one: Claude started replying.</summary>
    public static void Responding() => Play(RespondingChime, replace: true);

    /// <summary>Three falling notes ending on a long one: the turn is done.</summary>
    public static void Done() => Play(DoneChime, replace: true);

    /// <summary>Two equal notes: Claude needs an answer.</summary>
    public static void Question() => Play(QuestionChime, replace: true);

    /// <summary>A quick high trill: the answer notice opened and waits for an answer.</summary>
    public static void Notice() => Play(NoticeChime, replace: true);

    /// <summary>Queues the sound; with <paramref name="replace"/> it first stops and drops whatever is queued, otherwise it is skipped while it is queued itself.</summary>
    private static void Play(Sound sound, bool replace)
    {
        if (_failed)
        {
            return;
        }

        try
        {
            if (_device == 0 && !Open())
            {
                return;
            }

            if (replace)
            {
                waveOutReset(_device);
            }
            else if (sound.Queued)
            {
                return;
            }

            var result = waveOutWrite(_device, sound.Header, HeaderSize);
            if (result != 0)
            {
                // The device went away (unplugged, switched): the next sound opens the current one.
                Log.Error($"waveOutWrite failed with {result}; the sound device is reopened at the next sound");
                Close();
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // No winmm on this Windows: log once and stay quiet; the announcements still work.
            _failed = true;
            Log.Error("Sounds are not available", ex);
        }
    }

    /// <summary>Opens the default output device for the sounds' format and prepares every sound's buffer for it.</summary>
    private static bool Open()
    {
        var format = new WAVEFORMATEX
        {
            wFormatTag = 1, // PCM
            nChannels = 1,
            nSamplesPerSec = WaveTone.SampleRate,
            nAvgBytesPerSec = WaveTone.SampleRate * 2,
            nBlockAlign = 2,
            wBitsPerSample = 16,
        };
        var result = waveOutOpen(out _device, WAVE_MAPPER, ref format, 0, 0, 0);
        if (result != 0)
        {
            _device = 0;
            _failed = true;
            Log.Error($"waveOutOpen failed with {result}; the sounds are off for this run");
            return false;
        }

        foreach (var sound in All)
        {
            result = waveOutPrepareHeader(_device, sound.Header, HeaderSize);
            if (result != 0)
            {
                Log.Error($"waveOutPrepareHeader failed with {result}; the sounds are off for this run");
                Close();
                _failed = true;
                return false;
            }
        }

        return true;
    }

    private static void Close()
    {
        waveOutReset(_device);
        foreach (var sound in All)
        {
            waveOutUnprepareHeader(_device, sound.Header, HeaderSize);
        }

        waveOutClose(_device);
        _device = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEHDR
    {
        public nint lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public nint dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public nint lpNext;
        public nint reserved;
    }

    [DllImport("winmm.dll")]
    private static extern uint waveOutOpen(out nint hwo, uint uDeviceID, ref WAVEFORMATEX pwfx, nint dwCallback, nint dwInstance, uint fdwOpen);

    [DllImport("winmm.dll")]
    private static extern uint waveOutPrepareHeader(nint hwo, nint pwh, int cbwh);

    [DllImport("winmm.dll")]
    private static extern uint waveOutUnprepareHeader(nint hwo, nint pwh, int cbwh);

    [DllImport("winmm.dll")]
    private static extern uint waveOutWrite(nint hwo, nint pwh, int cbwh);

    [DllImport("winmm.dll")]
    private static extern uint waveOutReset(nint hwo);

    [DllImport("winmm.dll")]
    private static extern uint waveOutClose(nint hwo);
}

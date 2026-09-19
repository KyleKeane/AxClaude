using System.Runtime.InteropServices;
using AxClaude.Core.Audio;

namespace AxClaude;

/// <summary>
/// The app's sounds (FR-7.3, FR-7.8), played from memory through winmm's PlaySound. A process plays one sound at a
/// time and a call replaces whatever is playing. A chime is never cut short by a tick: the tick yields for as long
/// as the chime lasts, by the clock, rather than asking winmm with SND_NOSTOP, which drops the
/// new sound whenever winmm still counts an earlier one as playing and would silence the ticks for good if the
/// device never finished one. The wave data is generated once and pinned, because PlaySound keeps reading it after
/// the call has returned.
/// </summary>
internal static class Sounds
{
    private const uint SND_ASYNC = 0x0001;
    private const uint SND_NODEFAULT = 0x0002;
    private const uint SND_MEMORY = 0x0004;

    /// <summary>A chime's pinned wave data and its length, for which the tick yields.</summary>
    private sealed record Chime(nint Data, int Milliseconds)
    {
        public Chime(params (double Hertz, int Milliseconds)[] notes)
            : this(Pin(WaveTone.Notes(0.35, notes)), notes.Sum(note => note.Milliseconds))
        {
        }
    }

    // A typewriter-like strike, a burst of noise over a 1.5 kHz tone that dies away within 10 ms: a line from Claude
    // arrived. Two lower strikes in a row, 5 ms each and rising, a tiny ratchet: a tool call arrived.
    private static readonly nint ClickData = Pin(WaveTone.Strikes(0.4, 10, 0.5, 1500));
    private static readonly nint ToolData = Pin(WaveTone.Strikes(0.4, 5, 0.5, 900, 1150));

    // C6 alone, a high ping: Claude is ready. A4 alone, low: the message went out. C5, E5, G5 up to a long note:
    // Claude starts replying. The same notes down, G5, E5, C5: the turn is done. Two E5s: Claude needs an answer.
    private static readonly Chime ReadyChime = new((1046.5, 120));
    private static readonly Chime SentChime = new((440, 80));
    private static readonly Chime RespondingChime = new((523.25, 90), (659.25, 90), (783.99, 220));
    private static readonly Chime DoneChime = new((783.99, 90), (659.25, 90), (523.25, 220));
    private static readonly Chime QuestionChime = new((659.25, 70), (0, 40), (659.25, 110));

    /// <summary>The clock reading at which the running chime ends; the tick waits for it.</summary>
    private static long _chimeEndsAt;
    private static bool _failed;

    /// <summary>A tiny typewriter-like tick: a line from Claude arrived. Skipped while a chime plays.</summary>
    public static void Click() => Tick(ClickData);

    /// <summary>A tiny ratchet, two lower strikes: a tool call arrived. Skipped while a chime plays.</summary>
    public static void Tool() => Tick(ToolData);

    private static void Tick(nint data)
    {
        if (Environment.TickCount64 < _chimeEndsAt)
        {
            return;
        }

        Play(data);
    }

    /// <summary>A single high ping: Claude is ready.</summary>
    public static void Ready() => Play(ReadyChime);

    /// <summary>A single low note: the message went out.</summary>
    public static void Sent() => Play(SentChime);

    /// <summary>Three rising notes ending on a long one: Claude started replying.</summary>
    public static void Responding() => Play(RespondingChime);

    /// <summary>Three falling notes ending on a long one: the turn is done.</summary>
    public static void Done() => Play(DoneChime);

    /// <summary>Two equal notes: Claude needs an answer.</summary>
    public static void Question() => Play(QuestionChime);

    private static void Play(Chime chime)
    {
        _chimeEndsAt = Environment.TickCount64 + chime.Milliseconds;
        Play(chime.Data);
    }

    private static void Play(nint data)
    {
        if (_failed)
        {
            return;
        }

        try
        {
            PlaySound(data, 0, SND_ASYNC | SND_MEMORY | SND_NODEFAULT);
        }
        catch (Exception ex)
        {
            // No winmm on this Windows: log once and stay quiet; the announcements still work.
            _failed = true;
            Log.Error("Sounds are not available", ex);
        }
    }

    private static nint Pin(byte[] wav) => GCHandle.Alloc(wav, GCHandleType.Pinned).AddrOfPinnedObject();

    [DllImport("winmm.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(nint pszSound, nint hmod, uint fdwSound);
}

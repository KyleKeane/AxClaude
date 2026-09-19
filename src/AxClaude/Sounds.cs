using System.Runtime.InteropServices;
using AxClaude.Core.Audio;

namespace AxClaude;

/// <summary>
/// The app's sounds (FR-7.3, FR-7.8), played from memory through winmm's PlaySound. A process plays one sound at a
/// time: a chime replaces whatever is playing, and the tick for a new line never cuts a chime short (SND_NOSTOP).
/// The wave data is generated once and pinned, because PlaySound keeps reading it after the call has returned.
/// </summary>
internal static class Sounds
{
    private const uint SND_ASYNC = 0x0001;
    private const uint SND_NODEFAULT = 0x0002;
    private const uint SND_MEMORY = 0x0004;
    private const uint SND_NOSTOP = 0x0010;

    // C6 alone, a high ping: Claude is ready. A4 alone, low: the message went out. C5, E5, G5 up to a long note:
    // Claude starts replying. The same notes down, G5, E5, C5: the turn is done. Two E5s: Claude needs an answer.
    private static readonly nint ClickData = Pin(WaveTone.Tick(0.35, 1800, 8));
    private static readonly nint ReadyData = Pin(WaveTone.Notes(0.35, (1046.5, 120)));
    private static readonly nint SentData = Pin(WaveTone.Notes(0.35, (440, 80)));
    private static readonly nint RespondingData = Pin(WaveTone.Notes(0.35, (523.25, 90), (659.25, 90), (783.99, 220)));
    private static readonly nint DoneData = Pin(WaveTone.Notes(0.35, (783.99, 90), (659.25, 90), (523.25, 220)));
    private static readonly nint QuestionData = Pin(WaveTone.Notes(0.35, (659.25, 70), (0, 40), (659.25, 110)));
    private static bool _failed;

    /// <summary>A tiny tick: a line from Claude arrived.</summary>
    public static void Click() => Play(ClickData, SND_NOSTOP);

    /// <summary>A single high ping: Claude is ready.</summary>
    public static void Ready() => Play(ReadyData, 0);

    /// <summary>A single low note: the message went out.</summary>
    public static void Sent() => Play(SentData, 0);

    /// <summary>Three rising notes ending on a long one: Claude started replying.</summary>
    public static void Responding() => Play(RespondingData, 0);

    /// <summary>Three falling notes ending on a long one: the turn is done.</summary>
    public static void Done() => Play(DoneData, 0);

    /// <summary>Two equal notes: Claude needs an answer.</summary>
    public static void Question() => Play(QuestionData, 0);

    private static void Play(nint data, uint extra)
    {
        if (_failed)
        {
            return;
        }

        try
        {
            PlaySound(data, 0, SND_ASYNC | SND_MEMORY | SND_NODEFAULT | extra);
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

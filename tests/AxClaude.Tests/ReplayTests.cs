using System.Text;
using AxClaude.Core.Transcript;

namespace AxClaude.Tests;

/// <summary>
/// Replays recordings chunk by chunk with the app's own send timing: the exchange block is printed before the text
/// is written, the draft echo arrives without a frame bracket, and a quiet period of 100 ms ends a frame.
/// </summary>
public class ReplayTests
{
    private const string LongMessage = "long-message-claude2.1.278-240x50.vt";
    private const string Steering = "steering-claude2.1.278-240x50.vt";

    private static readonly string[] LongMessages =
    [
        "Please reply with exactly the two words OK DONE and nothing else. The rest of this message is filler so that it is longer than one console row of two hundred and forty columns: alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo lima mike november oscar papa quebec romeo sierra tango uniform victor whiskey xray yankee zulu alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo lima mike november oscar papa quebec romeo sierra tango uniform victor whiskey xray yankee zulu alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo lima mike november oscar papa quebec romeo sierra tango uniform victor whiskey xray yankee zulu end of filler.",
        "Second message, also long, reply again with exactly OK DONE. Filler: one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen eighteen nineteen twenty twentyone twentytwo twentythree twentyfour twentyfive twentysix twentyseven twentyeight twentynine thirty thirtyone thirtytwo thirtythree thirtyfour thirtyfive thirtysix thirtyseven thirtyeight thirtynine forty fortyone fortytwo fortythree fortyfour fortyfive fortysix fortyseven fortyeight fortynine fifty end of filler.",
    ];

    private static readonly string[] SteeringMessages =
    [
        "Use the Bash tool to run the command: sleep 20. After it finishes reply with exactly OK DONE.",
        "Second message sent while you were busy: after OK DONE also write the word HELLO on its own line.",
        "Reply with the number of lines in this message and the text of its last line.\nThis is the second line.",
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Wrapped_echo_of_a_long_message_is_hidden_under_its_block(bool quietFrameDuringDraft)
    {
        var texts = Replay(LongMessage, LongMessages, quietFrameDuringDraft).Select(l => l.Text).ToList();
        AssertExchange(texts, 1, LongMessages[0], "claude: OK DONE");
        AssertExchange(texts, 2, LongMessages[1], "claude: OK DONE");
        Assert.DoesNotContain(texts, t => t.StartsWith("you:") || t.StartsWith("$"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Message_sent_while_claude_works_is_marked_where_claude_takes_it_up(bool quietFrameDuringDraft)
    {
        var lines = Replay(Steering, SteeringMessages, quietFrameDuringDraft);
        var texts = lines.Select(l => l.Text).ToList();

        AssertExchange(texts, 1, SteeringMessages[0], "claude: Running the sleep command now.");
        AssertExchange(texts, 2, SteeringMessages[1], "claude: OK DONE");
        Assert.Equal("HELLO", texts[texts.IndexOf("# Output 2 Reply from Claude") + 3]);
        AssertExchange(texts, 3, SteeringMessages[2], "claude: The message has 2 lines. The last line reads:");

        // The queued copy of the second message and its hint never show; the tool result rows stay.
        Assert.DoesNotContain(texts, t => t.StartsWith("you:") || t.StartsWith("$") || t.Contains("ctrl+x ctrl+s") || t.Contains("ctrl+b to run"));
        Assert.Contains("(No output)", texts);
        Assert.Contains("tool: Bash (sleep 20)", texts);
        Assert.True(texts.IndexOf("tool: Bash (sleep 20)") < texts.IndexOf("# Input 2"), string.Join("\n", texts));
        Assert.Equal(3, lines.Count(l => l.Kind == LineKind.OutputMarker));
        Assert.Equal(3, lines.Count(l => l.Kind == LineKind.InputMarker));
    }

    /// <summary>The exchange block of message n, exactly as FR-4.1 prints it, directly followed by the first reply line.</summary>
    private static void AssertExchange(List<string> texts, int n, string sent, string firstReplyLine)
    {
        var input = texts.IndexOf($"# Input {n}");
        Assert.True(input >= 0, $"Missing input marker {n}:\n" + string.Join("\n", texts));
        var expected = new List<string> { $"# Input {n}" };
        expected.AddRange(sent.Split('\n'));
        expected.AddRange(["", $"# Output {n} Reply from Claude", "", firstReplyLine]);
        Assert.Equal(expected, texts.Skip(input).Take(expected.Count).ToList());
    }

    private static List<Line> Replay(string fixture, string[] sent, bool quietFrameDuringDraft)
    {
        var directory = FixtureDirectory();
        var bytes = File.ReadAllBytes(Path.Combine(directory, fixture));
        var chunks = File.ReadAllLines(Path.Combine(directory, fixture + ".chunks.txt"))
            .Select(l => l.TrimStart('﻿'))
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split(' '))
            .Select(p => (Offset: int.Parse(p[0]), Length: int.Parse(p[1]), Ms: int.Parse(p[2])))
            .ToList();

        var model = new SessionModel(240, 50);
        var next = 0;
        var previousMs = 0;
        var maxQueued = 0;
        model.Changed += () => maxQueued = Math.Max(maxQueued, model.QueuedMessages);
        foreach (var chunk in chunks)
        {
            var data = bytes.AsSpan(chunk.Offset, chunk.Length);
            if (chunk.Ms - previousMs >= 100)
            {
                model.EndFrame();
            }

            var firstLine = next < sent.Length ? sent[next].Split('\n')[0] : null;
            var probe = firstLine is null ? null : firstLine[..Math.Min(30, firstLine.Length)];
            if (probe is not null && data.Length > 20 && Encoding.UTF8.GetString(data).Contains(probe, StringComparison.Ordinal))
            {
                // The app inserts the markers before it writes the text; the draft echo arrives a little later.
                model.Send(sent[next++]);
                model.Feed(data);
                if (quietFrameDuringDraft)
                {
                    model.EndFrame();
                }
            }
            else
            {
                model.Feed(data);
            }

            previousMs = chunk.Ms;
        }

        model.EndFrame();
        Assert.Equal(sent.Length, next);
        Assert.Equal(fixture == Steering ? 1 : 0, maxQueued);
        Assert.Equal(0, model.QueuedMessages);
        return model.Lines.Where(l => !l.Hidden).ToList();
    }

    private static string FixtureDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "fixtures");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("tests/fixtures was not found above " + AppContext.BaseDirectory);
    }
}

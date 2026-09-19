using System.Text;
using AxClaude.Core.Transcript;

namespace AxClaude.Tests;

/// <summary>
/// The per-frame pass behind the tick (FR-7.8) and Speak replies as they arrive (FR-7.4): a tick for every frame
/// that adds a line from Claude, and one utterance per frame made of the reply lines that became final, in the
/// chosen mode, and of the tool calls when asked for; nothing twice, nothing from tool output, thinking or a
/// replayed conversation.
/// </summary>
public class ArrivalsTests
{
    private const string Esc = "\x1b";

    [Fact]
    public void A_block_printed_in_one_frame_is_one_utterance_and_important_takes_its_first_line()
    {
        var all = new SessionModel(80, 14);
        var important = new SessionModel(80, 14);
        var heard = Listen(all, ReplySpeechMode.All);
        var headlines = Listen(important, ReplySpeechMode.FirstLines);

        foreach (var model in new[] { all, important })
        {
            Feed(model, $"{Esc}[?25lauto mode on (shift+tab to cycle)\r\n${Esc}[?25h");
            model.Send("blue?");
            Feed(model, $"{Esc}[?25l{Esc}[1;1Hyou: blue?{Esc}[K\r\nThinking…{Esc}[K\r\nauto mode on (shift+tab to cycle)  ·  esc to interrupt{Esc}[K\r\n${Esc}[K{Esc}[?25h");
            Feed(model, $"{Esc}[?25l{Esc}[2;1Hclaude: What Blue Is{Esc}[K\r\n{Esc}[K\r\nBlue is a primary color.{Esc}[K\r\n{Esc}[K\r\nWhy People Like It{Esc}[K\r\n- It is calm.{Esc}[K\r\n- It is trusted{Esc}[K\r\nBaked for 1s · done 2:47 PM{Esc}[K\r\nauto mode on (shift+tab to cycle){Esc}[K\r\n${Esc}[K{Esc}[?25h");
        }

        Assert.Equal(["claude: What Blue Is. Blue is a primary color. Why People Like It. - It is calm. - It is trusted"], heard.Speech);
        Assert.Equal(["claude: What Blue Is"], headlines.Speech);

        // Nothing is spoken twice, and a spinner frame adds nothing.
        Feed(all, $"{Esc}[?25l{Esc}[9;1HPondering…{Esc}[K\r\nauto mode on (shift+tab to cycle)  ·  esc to interrupt{Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Assert.Single(heard.Speech);
    }

    [Fact]
    public void The_tick_marks_every_frame_that_adds_a_line_from_claude_whatever_the_speech_mode()
    {
        var model = new SessionModel(80, 10);
        var heard = Listen(model, ReplySpeechMode.None);
        Feed(model, $"{Esc}[?25lauto mode on (shift+tab to cycle)\r\n${Esc}[?25h");
        Assert.Equal([false], heard.Ticks);

        model.Send("go");
        Assert.Equal([false, false], heard.Ticks);

        // The echo is hidden, the reply and a tool row arrive, a spinner tick changes nothing, the summary arrives.
        Feed(model, $"{Esc}[?25l{Esc}[1;1Hyou: go{Esc}[K\r\nclaude: Looking.{Esc}[K\r\ntool: Read (a.txt){Esc}[K\r\nRunning…{Esc}[K\r\nauto mode on (shift+tab to cycle)  ·  esc to interrupt{Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Feed(model, $"{Esc}[?25l{Esc}[4;1HPondering…{Esc}[K\r\nauto mode on (shift+tab to cycle)  ·  esc to interrupt{Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Feed(model, $"{Esc}[?25l{Esc}[4;1HBaked for 1s · done 2:47 PM{Esc}[K\r\nauto mode on (shift+tab to cycle){Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Assert.Equal([false, false, true, false, true], heard.Ticks);
        // The tool tick for the frame that brought the tool row, whatever is spoken.
        Assert.Equal([false, false, true, false, false], heard.ToolCalls);
        Assert.Empty(heard.Speech);
    }

    [Fact]
    public void The_last_line_waits_until_another_line_follows_or_claude_is_idle()
    {
        var model = new SessionModel(80, 10);
        var heard = Listen(model, ReplySpeechMode.All);
        Feed(model, $"{Esc}[?25lauto mode on (shift+tab to cycle)\r\n${Esc}[?25h");
        model.Send("go");

        // The first message of the turn, with the spinner still on: its last line may still grow.
        Feed(model, $"{Esc}[?25l{Esc}[1;1Hclaude: Looking at the file.{Esc}[K\r\nRunning…{Esc}[K\r\nauto mode on (shift+tab to cycle)  ·  esc to interrupt{Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Assert.Empty(heard.Speech);

        // A tool row after it makes it final; the tool row itself is never spoken.
        Feed(model, $"{Esc}[?25l{Esc}[2;1Htool: Read (a.txt){Esc}[K\r\nRunning…{Esc}[K\r\nauto mode on (shift+tab to cycle)  ·  esc to interrupt{Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Assert.Equal(["claude: Looking at the file."], heard.Speech);

        // The closing message becomes final when Claude is idle.
        Feed(model, $"{Esc}[?25l{Esc}[3;1Hclaude: The file is empty.{Esc}[K\r\nBaked for 1s · done 2:47 PM{Esc}[K\r\nauto mode on (shift+tab to cycle){Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Assert.Equal(["claude: Looking at the file.", "claude: The file is empty."], heard.Speech);
    }

    [Fact]
    public void A_tool_call_is_spoken_as_it_arrives_when_asked_for_and_its_output_never()
    {
        var model = new SessionModel(80, 10);
        var heard = Listen(model, ReplySpeechMode.FirstLines, toolCalls: true);
        var calls = Listen(model, ReplySpeechMode.None, toolCalls: true);
        var replies = Listen(model, ReplySpeechMode.All);
        Feed(model, $"{Esc}[?25lauto mode on (shift+tab to cycle)\r\n${Esc}[?25h");
        model.Send("go");

        // The opening line and the first call arrive together: one utterance, the tool row final at once although the tool still runs under it.
        Feed(model, $"{Esc}[?25l{Esc}[1;1Hclaude: Looking at the file.{Esc}[K\r\ntool: Read (a.txt){Esc}[K\r\nRunning…{Esc}[K\r\nauto mode on (shift+tab to cycle)  ·  esc to interrupt{Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Assert.Equal(["claude: Looking at the file. tool: Read (a.txt)"], heard.Speech);
        Assert.Equal(["tool: Read (a.txt)"], calls.Speech);
        Assert.True(replies.ToolCalls[^1]);

        // Its output rows are not spoken; the next call is, on its own.
        Feed(model, $"{Esc}[?25l{Esc}[3;1H  ⎿  Read 3 lines (ctrl+o to expand){Esc}[K\r\ntool: Bash (dir){Esc}[K\r\nRunning…{Esc}[K\r\nauto mode on (shift+tab to cycle)  ·  esc to interrupt{Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Assert.Equal(["claude: Looking at the file. tool: Read (a.txt)", "tool: Bash (dir)"], heard.Speech);
        Assert.True(replies.ToolCalls[^1]);

        // The closing message when Claude is idle; without the option the tool rows were never spoken, but they ticked.
        Feed(model, $"{Esc}[?25l{Esc}[5;1H(No output){Esc}[K\r\nclaude: The file has 3 lines.{Esc}[K\r\nBaked for 1s · done 2:47 PM{Esc}[K\r\nauto mode on (shift+tab to cycle){Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Assert.Equal(["claude: Looking at the file. tool: Read (a.txt)", "tool: Bash (dir)", "claude: The file has 3 lines."], heard.Speech);
        Assert.Equal(["tool: Read (a.txt)", "tool: Bash (dir)"], calls.Speech);
        Assert.Equal(["claude: Looking at the file.", "claude: The file has 3 lines."], replies.Speech);
        Assert.False(replies.ToolCalls[^1]);
        Assert.Equal(2, replies.ToolCalls.Count(t => t));
    }

    [Fact]
    public void Skip_leaves_the_replayed_conversation_unspoken_but_not_the_rows_claude_redraws_afterwards()
    {
        var model = new SessionModel(80, 12);
        var arrivals = new Arrivals();
        Feed(model, $"{Esc}[?25lyou: earlier question\r\nclaude: earlier answer\r\nauto mode on (shift+tab to cycle)\r\n${Esc}[?25h");

        // Ready: the window skips what is there, then marks the replay; none of it is spoken.
        arrivals.Skip(model.Lines);
        model.MarkReplayedExchanges();
        Assert.Null(arrivals.Update(model.Lines, model.Working, ReplySpeechMode.All, false).Speech);

        // The first live reply is drawn over the rows that held the mode line and the prompt: spoken all the same.
        model.Send("next");
        Feed(model, $"{Esc}[?25l{Esc}[3;1Hyou: next{Esc}[K\r\nclaude: live answer{Esc}[K\r\nauto mode on (shift+tab to cycle){Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Assert.Null(arrivals.Update(model.Lines, model.Working, ReplySpeechMode.None, false).Speech);
        Assert.Equal("claude: live answer", arrivals.Update(model.Lines, model.Working, ReplySpeechMode.All, false).Speech);
        Assert.Null(arrivals.Update(model.Lines, model.Working, ReplySpeechMode.All, false).Speech);
    }

    [Theory]
    [InlineData("conversation-claude2.1.277-240x50.vt", 240, 50)]
    [InlineData("conversation-claude2.1.277-120x40.vt", 120, 40)]
    public void Every_reply_line_of_a_recorded_conversation_is_spoken_once(string fixture, int columns, int rows)
    {
        string[] sent =
        [
            "What is 2+2? Answer in one short line.",
            "Write a short markdown answer with two level-2 headings and a three item bullet list about the color blue.",
            "Create a file named hello.txt in this folder containing the single word hi.",
        ];
        var model = new SessionModel(columns, rows);
        var heard = Listen(model, ReplySpeechMode.All);
        var headlines = Listen(model, ReplySpeechMode.FirstLines);
        var calls = Listen(model, ReplySpeechMode.None, toolCalls: true);
        Replay(model, fixture, sent);

        // Every reply line after a live marker was heard, in one piece per frame, and nothing else was.
        var replyLines = new List<string>();
        var inReply = false;
        var marker = false;
        foreach (var line in model.Lines.Where(l => !l.Hidden))
        {
            marker |= line.Kind == LineKind.OutputMarker;
            if (line.IsMarker || !marker)
            {
                continue;
            }

            inReply = line.Kind == LineKind.ClaudeReply || (inReply && line.Kind == LineKind.Plain);
            if (inReply && line.Text.Length > 0 && !line.Text.StartsWith("Resume this session", StringComparison.Ordinal) && !line.Text.StartsWith("claude --resume", StringComparison.Ordinal))
            {
                replyLines.Add(line.Text);
            }
        }

        var spokenText = string.Join(" ", heard.Speech);
        Assert.All(replyLines, text => Assert.Contains(text.Trim(), spokenText));
        Assert.Equal(replyLines.Count(t => t.StartsWith("claude:", StringComparison.Ordinal)), headlines.Speech.Count);
        Assert.All(headlines.Speech, text => Assert.StartsWith("claude:", text));
        Assert.DoesNotContain(heard.Speech, text => text.Contains("ctrl+o to expand") || text.Contains("you:"));

        // The markdown answer, drawn in one frame, is one utterance with a pause after the heading.
        var markdown = heard.Speech.Single(text => text.StartsWith("claude: W", StringComparison.Ordinal) && text.Contains("- "));
        Assert.Matches(@"^claude: W[^.]+\. ", markdown);
        Assert.Equal(1, markdown.Split("claude:").Length - 1);

        // Lines from Claude ticked, and more frames ticked than spoke: tool rows tick too.
        Assert.True(heard.Ticks.Count(t => t) > heard.Speech.Count);

        // The file was written and read back under one collapsed tool row, "tool: Reading 1 file… (ctrl+o to expand)",
        // which Claude rewrote to "Read 1 file (ctrl+o to expand)" when done: spoken once, as it started, without the hint,
        // and one tool tick, in the frame that brought the row.
        Assert.Equal(["tool: Reading 1 file…"], calls.Speech);
        Assert.Equal(1, heard.ToolCalls.Count(t => t));
    }

    [Fact]
    public void A_recorded_tool_call_joins_the_opening_line_and_the_status_under_it_is_never_spoken()
    {
        var model = new SessionModel(240, 50);
        var heard = Listen(model, ReplySpeechMode.FirstLines, toolCalls: true);
        var calls = Listen(model, ReplySpeechMode.None, toolCalls: true);
        Replay(model, "steering-claude2.1.278-240x50.vt",
        [
            "Use the Bash tool to run the command: sleep 20. After it finishes reply with exactly OK DONE.",
            "Second message sent while you were busy: after OK DONE also write the word HELLO on its own line.",
            "Reply with the number of lines in this message and the text of its last line.\nThis is the second line.",
        ]);

        // The tool row arrives in the frame after the opening line, which it makes final: one utterance for both.
        Assert.Equal(["tool: Bash (sleep 20)"], calls.Speech);
        Assert.Equal(1, calls.ToolCalls.Count(t => t));
        Assert.Equal("claude: Running the sleep command now. tool: Bash (sleep 20)", heard.Speech[0]);
        Assert.DoesNotContain(heard.Speech, text => text.Contains("Running") && !text.Contains("claude:"));
    }

    private sealed class Heard
    {
        public List<string> Speech { get; } = [];
        public List<bool> Ticks { get; } = [];
        public List<bool> ToolCalls { get; } = [];
    }

    private static Heard Listen(SessionModel model, ReplySpeechMode mode, bool toolCalls = false)
    {
        var arrivals = new Arrivals();
        var heard = new Heard();
        model.Changed += () =>
        {
            var arrival = arrivals.Update(model.Lines, model.Working, mode, toolCalls);
            heard.Ticks.Add(arrival.NewLine);
            heard.ToolCalls.Add(arrival.ToolCall);
            if (arrival.Speech is { } text)
            {
                heard.Speech.Add(text);
            }
        };
        return heard;
    }

    /// <summary>Feeds the recording in small chunks; each message is sent, as the app does, before its draft appears in the stream.</summary>
    private static void Replay(SessionModel model, string fixture, string[] sent)
    {
        var bytes = File.ReadAllBytes(Path.Combine(FixtureDirectory(), fixture));
        var sendAt = new List<int>();
        var search = 0;
        foreach (var text in sent)
        {
            var probe = Encoding.UTF8.GetBytes(text[..Math.Min(30, text.Length)]);
            var at = bytes.AsSpan(search).IndexOf(probe);
            Assert.True(at >= 0, "The sent text was not found in the recording: " + text);
            sendAt.Add(search + at);
            search += at + probe.Length;
        }

        const int chunk = 64;
        var next = 0;
        for (var offset = 0; offset < bytes.Length; offset += chunk)
        {
            var data = bytes.AsSpan(offset, Math.Min(chunk, bytes.Length - offset));
            if (next < sent.Length && sendAt[next] < offset + data.Length)
            {
                var cut = sendAt[next] - offset;
                model.Feed(data[..cut]);
                model.Send(sent[next++]);
                model.Feed(data[cut..]);
            }
            else
            {
                model.Feed(data);
            }
        }

        model.EndFrame();
        Assert.Equal(sent.Length, next);
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

    private static void Feed(SessionModel model, string text) => model.Feed(Encoding.UTF8.GetBytes(text));
}

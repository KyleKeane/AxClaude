using System.Text;
using AxClaude.Core.Transcript;

namespace AxClaude.Tests;

public class SessionModelTests
{
    private const string Esc = "\x1b";

    [Fact]
    public void Send_prints_the_exchange_block_and_hides_the_echo()
    {
        var model = new SessionModel(80, 10);
        Feed(model, $"{Esc}[?25lauto mode on (shift+tab to cycle)\r\n${Esc}[?25h");
        Assert.Empty(Visible(model));
        Assert.Equal("auto mode on", model.Mode);
        Assert.False(model.Working);

        Assert.True(model.Send("hi"));
        Assert.Equal(["# Input 1", "hi", "", "# Output 1 Reply from Claude", ""], Visible(model));

        Feed(model, $"{Esc}[?25l{Esc}[1;1Hyou: hi{Esc}[K\r\nThinking…{Esc}[K\r\nauto mode on (shift+tab to cycle)  ·  esc to interrupt{Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Assert.Equal(["# Input 1", "hi", "", "# Output 1 Reply from Claude", ""], Visible(model));
        Assert.True(model.Working);
        Assert.Equal("Thinking…", model.Spinner);

        Feed(model, $"{Esc}[?25l{Esc}[2;1Hclaude: hello{Esc}[K\r\nBaked for 1s · done 2:47 PM{Esc}[K\r\nauto mode on (shift+tab to cycle){Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Assert.Equal(["# Input 1", "hi", "", "# Output 1 Reply from Claude", "", "claude: hello", "Baked for 1s · done 2:47 PM"], Visible(model));
        Assert.False(model.Working);
        Assert.Equal(LineKind.ClaudeReply, model.Lines.Single(l => l.Text == "claude: hello").Kind);
        Assert.Equal(LineKind.TurnSummary, model.Lines.Single(l => l.Text.StartsWith("Baked")).Kind);
    }

    [Fact]
    public void Replayed_exchanges_get_marked_blocks_and_the_numbering_continues_after_them()
    {
        var model = new SessionModel(80, 12);
        Feed(model, $"{Esc}[?25lyou: Reply with exactly the word OK\r\nclaude: OK\r\nBrewed for 2s · done 8:44 AM\r\nyou: /exit\r\nBye!\r\nyou: And again\r\nclaude: OK\r\nauto mode on (shift+tab to cycle)\r\n${Esc}[?25h");
        Assert.Equal(["you: Reply with exactly the word OK", "claude: OK", "Brewed for 2s · done 8:44 AM", "you: /exit", "Bye!", "you: And again", "claude: OK"], Visible(model));

        // Two earlier exchanges get the blocks; the slash command does not.
        model.MarkReplayedExchanges();
        Assert.Equal(
            [
                "# Input 1 from previous session", "you: Reply with exactly the word OK", "", "# Output 1 from previous session Reply from Claude", "", "claude: OK", "Brewed for 2s · done 8:44 AM",
                "you: /exit", "Bye!",
                "# Input 2 from previous session", "you: And again", "", "# Output 2 from previous session Reply from Claude", "", "claude: OK",
            ],
            Visible(model));
        Assert.All(model.Lines.Where(l => l.Text.StartsWith("# ")), l => Assert.Equal(1, l.HeadingLevel));

        // A second pass changes nothing, the past output marker is no response start, and the live numbering starts at 1.
        var started = 0;
        model.ResponseStarted += () => started++;
        model.MarkReplayedExchanges();
        model.EndFrame();
        Assert.Equal(15, Visible(model).Count);
        Assert.Equal(0, started);
        Assert.True(model.Send("next"));
        Assert.Equal("# Input 1", Visible(model)[15]);
    }

    [Fact]
    public void A_replay_printed_while_hiding_stays_hidden_and_live_rows_show_again()
    {
        var model = new SessionModel(80, 12);
        model.AddSystemLine("Restarting Claude");
        model.HideReplay = true;
        Feed(model, $"{Esc}[?25lyou: earlier question\r\nclaude: earlier answer\r\nauto mode on (shift+tab to cycle)\r\n${Esc}[?25h");
        Assert.Equal(["System: Restarting Claude"], Visible(model));

        // Ready: the replay stays hidden, and the rows Claude rewrites afterwards show their live text.
        model.HideReplay = false;
        model.MarkReplayedExchanges();
        Assert.True(model.Send("next"));
        Feed(model, $"{Esc}[?25l{Esc}[3;1Hyou: next{Esc}[K\r\nclaude: live answer{Esc}[K\r\nauto mode on (shift+tab to cycle){Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Assert.Equal(["System: Restarting Claude", "# Input 1", "next", "", "# Output 1 Reply from Claude", "", "claude: live answer"], Visible(model));

        // A question before ready shows everything after all.
        var second = new SessionModel(80, 12);
        second.HideReplay = true;
        Feed(second, $"{Esc}[?25lyou: earlier question\r\nEnter y/n:\r\n{Esc}[?25h");
        Assert.Empty(Visible(second));
        second.ShowHiddenLines();
        Assert.False(second.HideReplay);
        Assert.Equal(["you: earlier question", "Enter y/n:"], Visible(second));
    }

    [Fact]
    public void A_you_row_that_repeats_no_message_is_not_charged_to_a_pending_send()
    {
        var model = new SessionModel(80, 12);
        Feed(model, $"{Esc}[?25lauto mode on (shift+tab to cycle)\r\n${Esc}[?25h");
        Feed(model, $"{Esc}[?25l{Esc}[1;1Hclaude: Working on it.{Esc}[K\r\nRunning…{Esc}[K\r\nauto mode on (shift+tab to cycle)  ·  esc to interrupt{Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Assert.True(model.Working);
        Assert.True(model.Send("real question"));
        Assert.Equal(["claude: Working on it."], Visible(model));

        // A tool result quotes a you: row: it stays a row of the result and the held block stays held.
        Feed(model, $"{Esc}[?25l{Esc}[2;1Htool: Bash (dump){Esc}[K\r\nyou: Reply with exactly the word OK{Esc}[K\r\nRunning…{Esc}[K\r\nauto mode on (shift+tab to cycle)  ·  esc to interrupt{Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Assert.Equal(["claude: Working on it.", "tool: Bash (dump)", "you: Reply with exactly the word OK"], Visible(model));

        // The real echo places the block in front of itself and is hidden.
        Feed(model, $"{Esc}[?25l{Esc}[4;1Hyou: real question{Esc}[K\r\nclaude: The answer.{Esc}[K\r\nauto mode on (shift+tab to cycle){Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Assert.Equal(
            ["claude: Working on it.", "tool: Bash (dump)", "you: Reply with exactly the word OK", "# Input 1", "real question", "", "# Output 1 Reply from Claude", "", "claude: The answer."],
            Visible(model));
    }

    [Fact]
    public void A_quoted_screen_reader_line_does_not_stop_the_replay_marking()
    {
        var model = new SessionModel(80, 14);
        Feed(model, $"{Esc}[?25l[Screen Reader Mode: on via flag]\r\nyou: first\r\nclaude: one\r\ntool: Bash (dump)\r\n[Screen Reader Mode: on via flag]\r\nyou: second\r\nclaude: two\r\nauto mode on (shift+tab to cycle)\r\n${Esc}[?25h");
        model.MarkReplayedExchanges();
        Assert.Equal(["# Input 1 from previous session", "# Input 2 from previous session"], model.Lines.Where(l => l.Kind == LineKind.InputMarker).Select(l => l.Text).ToList());
    }

    [Fact]
    public void Rows_that_scroll_away_inside_a_frame_are_still_classified()
    {
        var model = new SessionModel(80, 6);
        var burst = new StringBuilder($"{Esc}[?25lyou: early question\r\n");
        for (var i = 0; i < 20; i++)
        {
            burst.Append($"line {i}\r\n");
        }

        burst.Append($"auto mode on (shift+tab to cycle)\r\n${Esc}[?25h");
        Feed(model, burst.ToString());
        var early = model.Lines.Single(l => l.Text == "you: early question");
        Assert.True(early.Committed);
        Assert.Equal(LineKind.UserEcho, early.Kind);
    }

    [Fact]
    public void A_prompt_is_pending_only_when_the_cursor_sits_on_it()
    {
        // A tool result quoting a question, with Claude idle at its own prompt: no question.
        var model = new SessionModel(80, 12);
        Feed(model, $"{Esc}[?25ltool: Bash (dump)\r\nEnter y/n:\r\nauto mode on (shift+tab to cycle)\r\n${Esc}[?25h");
        Assert.False(model.PromptPending);
        Assert.True(model.Send("hello"));

        // The effort menu: the cursor waits at the end of its prompt row, and the answer is no message.
        var menu = new SessionModel(80, 12);
        Feed(menu, $"{Esc}[?25lEffort\r\n1. low\r\n2. (selected) high\r\nSelect with numbers [1-2]. Then Enter to submit or Escape to cancel:{Esc}[?25h");
        Assert.True(menu.PromptPending);
        Assert.False(menu.Send("2"));
    }

    [Fact]
    public void Claude_saying_no_conversation_to_continue_is_recognised()
    {
        var model = new SessionModel(80, 10);
        Assert.False(model.SaidNoConversationToContinue());
        Feed(model, $"{Esc}[?25l[Screen Reader Mode: on via flag]\r\nNo conversation found to continue\r\n{Esc}[?25h");
        Assert.True(model.SaidNoConversationToContinue());
    }

    [Fact]
    public void A_message_sent_while_claude_works_stays_out_of_the_conversation_until_its_echo()
    {
        var model = new SessionModel(80, 12);
        Feed(model, $"{Esc}[?25l${Esc}[?25h");
        model.Send("first");
        Feed(model, $"{Esc}[?25l{Esc}[1;1Hyou: first{Esc}[K\r\nclaude: Working on it.{Esc}[K\r\nRunning…{Esc}[K\r\nauto mode on (shift+tab to cycle)  ·  esc to interrupt{Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Assert.True(model.Working);
        Assert.Equal(["# Input 1", "first", "", "# Output 1 Reply from Claude", "", "claude: Working on it."], Visible(model));

        // Sent while Claude works: no markers yet, and Claude draws the queued copy inside its working block.
        model.Send("second");
        Assert.Equal(["# Input 1", "first", "", "# Output 1 Reply from Claude", "", "claude: Working on it."], Visible(model));
        Feed(model, $"{Esc}[?25l{Esc}[3;1Hyou: second{Esc}[K\r\nctrl+x ctrl+s to send now{Esc}[K\r\nRunning…{Esc}[K\r\nauto mode on (shift+tab to cycle)  ·  esc to interrupt{Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Assert.Equal(1, model.QueuedMessages);
        Assert.Equal(["# Input 1", "first", "", "# Output 1 Reply from Claude", "", "claude: Working on it."], Visible(model));

        // Claude takes it up: the echo becomes content, the block goes in front of it and the echo is hidden.
        Feed(model, $"{Esc}[?25l{Esc}[3;1HDone.{Esc}[K\r\nyou: second{Esc}[K\r\nclaude: Second answer.{Esc}[K\r\nWorked for 3s · done 2:47 PM{Esc}[K\r\nauto mode on (shift+tab to cycle){Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Assert.Equal(0, model.QueuedMessages);
        Assert.False(model.Working);
        Assert.Equal(
            ["# Input 1", "first", "", "# Output 1 Reply from Claude", "", "claude: Working on it.", "Done.", "# Input 2", "second", "", "# Output 2 Reply from Claude", "", "claude: Second answer.", "Worked for 3s · done 2:47 PM"],
            Visible(model));
    }

    [Fact]
    public void Markers_of_a_queued_message_without_an_echo_go_in_once_claude_is_idle()
    {
        var model = new SessionModel(80, 10);
        Feed(model, $"{Esc}[?25lclaude: Thinking hard.\r\nRunning…\r\nauto mode on (shift+tab to cycle)  ·  esc to interrupt\r\n${Esc}[?25h");
        Assert.True(model.Working);
        model.Send("interrupting question");
        Assert.Equal(["claude: Thinking hard."], Visible(model));

        Feed(model, $"{Esc}[?25l{Esc}[2;1HWorked for 3s · done 2:47 PM{Esc}[K\r\nauto mode on (shift+tab to cycle){Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Assert.False(model.Working);
        Assert.Equal(["claude: Thinking hard.", "Worked for 3s · done 2:47 PM", "# Input 1", "interrupting question", "", "# Output 1 Reply from Claude", ""], Visible(model));

        // The echo arrives after all: it is hidden and the block stays where it is.
        Feed(model, $"{Esc}[?25l{Esc}[3;1Hyou: interrupting question{Esc}[K\r\nclaude: Late answer.{Esc}[K\r\nauto mode on (shift+tab to cycle){Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Assert.Equal(["claude: Thinking hard.", "Worked for 3s · done 2:47 PM", "# Input 1", "interrupting question", "", "# Output 1 Reply from Claude", "", "claude: Late answer."], Visible(model));
        Assert.Single(model.Lines, l => l.Kind == LineKind.InputMarker);
    }

    [Fact]
    public void Time_stamps_go_into_both_markers_when_switched_on()
    {
        var model = new SessionModel(80, 10) { TimeStamps = true, Clock = () => new DateTime(2026, 9, 19, 14, 5, 0) };
        Feed(model, $"{Esc}[?25l${Esc}[?25h");
        model.Send("hi");
        Assert.Equal(["# Input 1 (14:05)", "hi", "", "# Output 1 Reply from Claude (14:05)", ""], Visible(model));
        Assert.All(model.Lines.Where(l => l.Kind is LineKind.InputMarker or LineKind.OutputMarker), l => Assert.Equal(1, l.HeadingLevel));
        Assert.All(model.Lines.Where(l => l.Kind == LineKind.UserMessage), l => Assert.Equal(0, l.HeadingLevel));
    }

    [Fact]
    public void Wrapped_reply_rows_join_but_tool_rows_and_labelled_rows_do_not()
    {
        var model = new SessionModel(40, 12);
        var wide = "claude: 123456789 123456789 123456789 12"; // 40 columns
        Feed(model, $"{Esc}[?25l{wide}\r\nrest of the sentence.\r\nclaude: second reply row filled up to\r\nUppercase start stays.\r\ntool: Bash (ls)\r\nsome output row that is exactly wide 40\r\nlowercase output row\r\nyou: a wrapped message that fills forty\r\ncontinues here\r\n${Esc}[?25h");

        var lines = model.Lines.Where(l => !l.Hidden).ToList();
        Assert.True(lines.Single(l => l.Text == "rest of the sentence.").JoinedToPrevious);
        Assert.False(lines.Single(l => l.Text.StartsWith("claude: second")).JoinedToPrevious);
        Assert.False(lines.Single(l => l.Text == "Uppercase start stays.").JoinedToPrevious);
        Assert.False(lines.Single(l => l.Text == "lowercase output row").JoinedToPrevious);
        Assert.True(lines.Single(l => l.Text == "continues here").JoinedToPrevious);
        Assert.Equal(0, lines.Single(l => l.Text == "continues here").HeadingLevel);

        var off = new SessionModel(40, 12) { JoinWrappedLines = false };
        Feed(off, $"{Esc}[?25l{wide}\r\nrest of the sentence.\r\n${Esc}[?25h");
        Assert.DoesNotContain(off.Lines, l => l.JoinedToPrevious);
    }

    [Fact]
    public void A_bookmark_goes_in_front_of_the_line_and_the_same_key_takes_it_away()
    {
        var model = new SessionModel(80, 10);
        Feed(model, $"{Esc}[?25lclaude: First.\r\nSecond.\r\nThird.\r\n${Esc}[?25h");
        var second = model.Lines.Single(l => l.Text == "Second.");

        var result = model.ToggleBookmark(second);
        Assert.True(result?.Added);
        var bookmark = result!.Value.Bookmark;
        Assert.Equal("Bookmark 1", bookmark.Text);
        Assert.Equal(LineKind.Bookmark, bookmark.Kind);
        Assert.True(bookmark.IsMarker);
        Assert.Equal(["claude: First.", "Bookmark 1", "Second.", "Third."], Visible(model));

        // The next frame leaves it alone: never classified, never hidden, not a heading.
        Feed(model, $"{Esc}[?25l{Esc}[3;1HThird, rewritten.{Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Assert.Equal(["claude: First.", "Bookmark 1", "Second.", "Third, rewritten."], Visible(model));
        Assert.Equal(0, bookmark.HeadingLevel);

        // m on the bookmarked line, or on the bookmark itself, removes it; the numbers keep counting up.
        Assert.Equal((bookmark, false), model.ToggleBookmark(second)!.Value);
        Assert.Equal(["claude: First.", "Second.", "Third, rewritten."], Visible(model));
        var again = model.ToggleBookmark(second)!.Value.Bookmark;
        Assert.Equal("Bookmark 2", again.Text);
        Assert.Equal((again, false), model.ToggleBookmark(again)!.Value);
        Assert.DoesNotContain(model.Lines, l => l.Kind == LineKind.Bookmark);

        // A line that has left the transcript cannot be bookmarked.
        Assert.Null(model.ToggleBookmark(bookmark));
    }

    [Fact]
    public void A_bookmark_on_a_wrapped_line_goes_in_front_of_its_first_row_and_disturbs_nothing_around_it()
    {
        var model = new SessionModel(40, 12);
        var wide = "claude: 123456789 123456789 123456789 12"; // 40 columns
        Feed(model, $"{Esc}[?25l{wide}\r\nrest of the sentence.\r\n\r\nA Heading\r\n\r\nBody text.\r\n${Esc}[?25h");
        var rest = model.Lines.Single(l => l.Text == "rest of the sentence.");
        Assert.True(rest.JoinedToPrevious);
        Assert.Equal(2, model.Lines.Single(l => l.Text == "A Heading").HeadingLevel);

        // m on the continuation row bookmarks the whole line: the bookmark goes in front of its first row.
        model.ToggleBookmark(rest);
        Assert.Equal(["Bookmark 1", wide, "rest of the sentence.", "", "A Heading", "", "Body text."], Visible(model));

        // m on the blank line after the heading puts the bookmark between them. Through the next frame the heading
        // stays a heading, the wrapped row stays joined, and m on the first row finds the bookmark again.
        var visible = model.Lines.Where(l => !l.Hidden).ToList();
        model.ToggleBookmark(visible[visible.FindIndex(l => l.Text == "A Heading") + 1]);
        Feed(model, $"{Esc}[?25l{Esc}[6;1HBody text, more.{Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Assert.Equal(["Bookmark 1", wide, "rest of the sentence.", "", "A Heading", "Bookmark 2", "", "Body text, more."], Visible(model));
        Assert.True(rest.JoinedToPrevious);
        Assert.Equal(2, model.Lines.Single(l => l.Text == "A Heading").HeadingLevel);
        Assert.All(model.Lines.Where(l => l.Kind == LineKind.Bookmark), l => Assert.Equal(0, l.HeadingLevel));

        Assert.False(model.ToggleBookmark(model.Lines.Single(l => l.Text == wide))?.Added);
        Assert.Equal([wide, "rest of the sentence.", "", "A Heading", "Bookmark 2", "", "Body text, more."], Visible(model));
    }

    [Fact]
    public void Markers_are_level_one_headings_and_claudes_headings_are_deeper()
    {
        var model = new SessionModel(80, 10);
        Feed(model, $"{Esc}[?25l${Esc}[?25h");
        model.Send("hi");
        Feed(model, $"{Esc}[?25l{Esc}[1;1Hyou: hi{Esc}[K\r\nclaude: What Blue Is{Esc}[K\r\n{Esc}[K\r\nBlue is a colour.{Esc}[K\r\n${Esc}[K{Esc}[?25h");

        Assert.Equal(1, model.Lines.Single(l => l.Kind == LineKind.InputMarker).HeadingLevel);
        Assert.Equal(1, model.Lines.Single(l => l.Kind == LineKind.OutputMarker).HeadingLevel);
        Assert.Equal(2, model.Lines.Single(l => l.Text == "claude: What Blue Is").HeadingLevel);
        Assert.Equal(0, model.Lines.Single(l => l.Text == "Blue is a colour.").HeadingLevel);
    }

    [Fact]
    public void Echo_that_differs_from_the_sent_text_stays_visible()
    {
        var model = new SessionModel(80, 10);
        Feed(model, $"{Esc}[?25l${Esc}[?25h");
        model.Send("first line\nsecond line");
        Feed(model, $"{Esc}[?25l{Esc}[1;1Hyou: [Pasted text #1 +2 lines]{Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Assert.Equal(["# Input 1", "first line", "second line", "", "# Output 1 Reply from Claude", "", "you: [Pasted text #1 +2 lines]"], Visible(model));
    }

    [Fact]
    public void Echo_with_blank_lines_and_wrapped_rows_is_hidden_as_a_whole()
    {
        // A quoted line (FR-2.9) puts a blank line inside the message; Claude echoes it as a blank row.
        var model = new SessionModel(80, 12);
        Feed(model, $"{Esc}[?25l${Esc}[?25h");
        model.Send("Look at this.\n\n_ start of copied line from conversation history _\nLine 3 of 54:\nExample test.");
        Feed(model, $"{Esc}[?25l{Esc}[1;1Hyou: Look at this.{Esc}[K\r\n{Esc}[K\r\n_ start of copied line from conversation history _{Esc}[K\r\nLine 3 of 54:{Esc}[K\r\nExample test.{Esc}[K\r\nclaude: I see it.{Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Assert.Equal(
            ["# Input 1", "Look at this.", "", "_ start of copied line from conversation history _", "Line 3 of 54:", "Example test.", "", "# Output 1 Reply from Claude", "", "claude: I see it."],
            Visible(model));

        // A message longer than the console width is echoed over two rows.
        var narrow = new SessionModel(40, 8);
        Feed(narrow, $"{Esc}[?25l${Esc}[?25h");
        narrow.Send("Please reply with exactly the two words OK DONE.");
        Feed(narrow, $"{Esc}[?25l{Esc}[1;1Hyou: Please reply with exactly the two{Esc}[K\r\nwords OK DONE.{Esc}[K\r\nclaude: OK DONE{Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Assert.Equal(["# Input 1", "Please reply with exactly the two words OK DONE.", "", "# Output 1 Reply from Claude", "", "claude: OK DONE"], Visible(narrow));
    }

    [Fact]
    public void Slash_commands_and_prompt_answers_get_no_markers()
    {
        var model = new SessionModel(80, 10);
        Assert.False(model.Send("/exit"));
        Feed(model, $"{Esc}[?25lPermission Required: Accessing workspace:\r\ny. Yes\r\nn. No\r\nEnter y/n:{Esc}[4;11H{Esc}[?25h");
        Assert.True(model.PromptPending);
        Assert.False(model.Send("y"));
        Assert.Equal(LineKind.PromptOption, model.Lines.Single(l => l.Text == "y. Yes").Kind);
    }

    [Fact]
    public void Mode_cycle_updates_the_mode_and_speaks_claudes_announcement_row()
    {
        // From a recording of two Shift+Tab presses: the default mode line has no cycle hint, and Claude parks the
        // cursor on a bracketed announcement row under the prompt, then erases it.
        var model = new SessionModel(80, 20);
        Feed(model, $"{Esc}[?25lclaude: PONG\r\nChurned for 2s · done 9:58 PM\r\nauto mode on (shift+tab to cycle)\r\neffort: xhigh · /effort\r\n${Esc}[?25h");
        Assert.Equal("auto mode on", model.Mode);
        var announced = new List<string>();
        model.Announcement += announced.Add;

        Feed(model, $"{Esc}[?25l{Esc}[3;1Hmanual mode on{Esc}[K\r\neffort: xhigh · /effort{Esc}[K\r\n${Esc}[K\r\n[manual mode on]{Esc}[?25h");
        Assert.Equal("manual mode on", model.Mode);
        Assert.Equal(["manual mode on"], announced);
        Assert.Equal(["claude: PONG", "Churned for 2s · done 9:58 PM"], Visible(model));

        Feed(model, $"{Esc}[?25l\r{Esc}[K{Esc}[5;2H{Esc}[?25h");
        Assert.Equal(["claude: PONG", "Churned for 2s · done 9:58 PM"], Visible(model));

        Feed(model, $"{Esc}[?25l{Esc}[3;1Haccept edits on (shift+tab to cycle){Esc}[K\r\neffort: xhigh · /effort{Esc}[K\r\n${Esc}[K\r\n[accept edits on]{Esc}[?25h");
        Assert.Equal("accept edits on", model.Mode);
        Assert.Equal(["manual mode on", "accept edits on"], announced);
        Assert.Equal(["claude: PONG", "Churned for 2s · done 9:58 PM"], Visible(model));
        Assert.False(model.Working);
    }

    [Fact]
    public void Ctrl_O_transcript_view_is_chrome_and_announced()
    {
        // From a recording: Ctrl+O replaces the mode line and the prompt with a status row and parks the cursor on it.
        var model = new SessionModel(120, 20);
        Feed(model, $"{Esc}[?25lC:\\Temp\\project\r\nauto mode on (shift+tab to cycle)\r\n${Esc}[?25h");
        var announced = new List<string>();
        model.Announcement += announced.Add;

        Feed(model, $"{Esc}[?25l{Esc}[2;1HShowing detailed transcript · ctrl+o to toggle · ctrl+e to show all verbose{Esc}[K\r\n{Esc}[K{Esc}[2;76H{Esc}[?25h");
        Assert.True(model.TranscriptViewOpen);
        Assert.Equal(["C:\\Temp\\project"], Visible(model));
        Assert.Single(announced);
        Assert.Equal(SessionModel.TranscriptViewOpened, announced[0]);

        Feed(model, $"{Esc}[?25l{Esc}[2;1Hauto mode on (shift+tab to cycle){Esc}[K\r\n${Esc}[K{Esc}[?25h");
        Assert.False(model.TranscriptViewOpen);
        Assert.Equal(2, announced.Count);
        Assert.Equal(SessionModel.TranscriptViewClosed, announced[1]);
        Assert.Equal(["C:\\Temp\\project"], Visible(model));

        // An ordinary frame while the prompt is showing announces nothing more.
        Feed(model, $"{Esc}[?25l{Esc}[3;1H{Esc}[K${Esc}[?25h");
        Assert.Equal(2, announced.Count);
    }

    [Fact]
    public void Rows_below_the_cursor_are_hidden()
    {
        var model = new SessionModel(80, 10);
        Feed(model, $"{Esc}[?25lEnter y/n:\r\nEnter to confirm · Esc to cancel{Esc}[1;11H{Esc}[?25h");
        Assert.Equal(["Enter y/n:"], Visible(model));
    }

    [Fact]
    public void Rewriting_a_row_updates_the_same_line()
    {
        var model = new SessionModel(80, 10);
        Feed(model, $"hello{Esc}[?25h");
        var line = Assert.Single(model.Lines);
        Feed(model, $"{Esc}[1;1Hworld{Esc}[K{Esc}[?25h");
        Assert.Same(line, Assert.Single(model.Lines));
        Assert.Equal("world", line.Text);
    }

    [Fact]
    public void Scrolling_commits_the_top_row_and_keeps_order()
    {
        var model = new SessionModel(80, 3);
        Feed(model, "a\r\nb\r\nc\r\nd");
        model.EndFrame();
        Assert.Equal(["a", "b", "c", "d"], Visible(model));
        Assert.True(model.Lines[0].Committed);
        Assert.False(model.Lines[1].Committed);
    }

    [Fact]
    public void Interior_blank_rows_are_kept_and_trailing_ones_are_not()
    {
        var model = new SessionModel(80, 10);
        Feed(model, $"{Esc}[?25lclaude: Title\r\n\r\nBody text.\r\n{Esc}[K\r\n{Esc}[K\r\n${Esc}[?25h");
        Assert.Equal(["claude: Title", "", "Body text."], Visible(model));
        Assert.Equal(2, model.Lines.Single(l => l.Text == "claude: Title").HeadingLevel);
    }

    [Fact]
    public void Wide_characters_take_two_cells()
    {
        var model = new SessionModel(10, 2);
        Feed(model, "日本x");
        model.EndFrame();
        Assert.Equal(["日本x"], Visible(model));
    }

    [Fact]
    public void Reset_screen_freezes_existing_lines()
    {
        var model = new SessionModel(80, 5);
        Feed(model, "old\r\n$");
        model.EndFrame();
        model.ResetScreen();
        model.AddSystemLine("restart");
        Feed(model, "new\r\n$");
        model.EndFrame();
        Assert.Equal(["old", "System: restart", "new"], Visible(model));
    }

    private static void Feed(SessionModel model, string text) => model.Feed(Encoding.UTF8.GetBytes(text));

    private static List<string> Visible(SessionModel model) => model.Lines.Where(l => !l.Hidden).Select(l => l.Text).ToList();
}

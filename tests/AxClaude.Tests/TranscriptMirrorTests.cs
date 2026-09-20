using System.Text;
using AxClaude.Core.Transcript;
using static AxClaude.Tests.TestHelpers;

namespace AxClaude.Tests;

/// <summary>
/// The mirror turns changes of the visible line list into edit-control edits and keeps a caret on the content it
/// was on: the same line and column when the line is still shown, the next shown line otherwise.
/// </summary>
public class TranscriptMirrorTests
{
    [Fact]
    public void Appended_lines_are_one_edit_at_the_end_and_leave_the_caret_on_its_line()
    {
        var mirror = new TranscriptMirror();
        var first = L("first");
        var blank = L("");
        var text = Apply(new StringBuilder(), mirror.Update([first, blank]));
        Assert.Equal("first\r\n", text.ToString());

        var caret = mirror.StartAt(1);
        var update = mirror.Update([first, blank, L("more"), L("and more")]);
        Apply(text, update);

        Assert.Single(update.Edits);
        Assert.Equal("first\r\n\r\nmore\r\nand more", text.ToString());
        Assert.Equal(caret, update.MapPosition(caret));
        Assert.Equal("first".Length, update.MapPosition("first".Length));
    }

    [Fact]
    public void A_rewritten_line_keeps_the_caret_at_its_column()
    {
        var mirror = new TranscriptMirror();
        var reply = L("claude: Hel");
        var text = Apply(new StringBuilder(), mirror.Update([L("You: hi"), reply]));
        var start = mirror.StartAt(1);

        reply.Text = "claude: Hello world, this row grew";
        var grown = mirror.Update([mirror.LineAt(0), reply]);
        Apply(text, grown);
        Assert.Equal("You: hi\r\nclaude: Hello world, this row grew", text.ToString());
        Assert.Equal(start, grown.MapPosition(start));
        Assert.Equal(start + 3, grown.MapPosition(start + 3));
        Assert.Equal(start + 11, grown.MapPosition(start + 11));

        reply.Text = "cl";
        var shrunk = mirror.Update([mirror.LineAt(0), reply]);
        Apply(text, shrunk);
        Assert.Equal("You: hi\r\ncl", text.ToString());
        Assert.Equal(start + 2, shrunk.MapPosition(start + 11));
    }

    [Fact]
    public void Lines_inserted_above_move_the_caret_with_its_line()
    {
        var mirror = new TranscriptMirror();
        var first = L("first");
        var second = L("second");
        var text = Apply(new StringBuilder(), mirror.Update([first, second]));
        var caret = mirror.StartAt(1) + 2;

        var update = mirror.Update([first, L("x"), L("yy"), second]);
        Apply(text, update);
        Assert.Equal("first\r\nx\r\nyy\r\nsecond", text.ToString());
        Assert.Equal(mirror.StartAt(3) + 2, update.MapPosition(caret));
    }

    [Fact]
    public void Lines_removed_above_move_the_caret_back_and_a_caret_inside_them_goes_to_the_first_remaining_line()
    {
        var mirror = new TranscriptMirror();
        var lines = new[] { L("one"), L("two"), L("three"), L("four") };
        var text = Apply(new StringBuilder(), mirror.Update(lines));
        var inThree = mirror.StartAt(2) + 1;
        var inTwo = mirror.StartAt(1) + 1;

        var update = mirror.Update(lines[2..]);
        Apply(text, update);
        Assert.Equal("three\r\nfour", text.ToString());
        Assert.Equal(1, update.MapPosition(inThree));
        Assert.Equal(0, update.MapPosition(inTwo));
    }

    [Fact]
    public void A_line_that_becomes_hidden_hands_the_caret_to_the_next_shown_line()
    {
        var mirror = new TranscriptMirror();
        var lines = new[] { L("first"), L("you: echo"), L("third") };
        var text = Apply(new StringBuilder(), mirror.Update(lines));
        var caret = mirror.StartAt(1) + 2;

        lines[1].EchoHidden = true;
        var update = mirror.Update(lines);
        Apply(text, update);
        Assert.Equal("first\r\nthird", text.ToString());
        Assert.Equal(mirror.StartAt(1), update.MapPosition(caret));
    }

    [Fact]
    public void A_caret_on_replaced_lines_goes_to_the_start_of_their_replacement()
    {
        var mirror = new TranscriptMirror();
        var first = L("first");
        var text = Apply(new StringBuilder(), mirror.Update([first, L("old"), L("older")]));
        var caret = mirror.StartAt(2) + 1;

        var update = mirror.Update([first, L("new")]);
        Apply(text, update);
        Assert.Equal("first\r\nnew", text.ToString());
        Assert.Equal(mirror.StartAt(1), update.MapPosition(caret));

        var emptied = mirror.Update([]);
        Apply(text, emptied);
        Assert.Equal("", text.ToString());
        Assert.Equal(0, emptied.MapPosition(3));
    }

    [Fact]
    public void A_joined_line_follows_its_predecessor_after_a_space_and_counts_as_the_same_display_line()
    {
        var mirror = new TranscriptMirror();
        var head = L("claude: The first row, filled to the width and");
        var rest = Joined("the rest of it.");
        var spaced = Joined(" more, starting with a space.");
        var next = L("Second paragraph.");
        var text = Apply(new StringBuilder(), mirror.Update([head, rest, spaced, next]));

        Assert.Equal("claude: The first row, filled to the width and the rest of it. more, starting with a space.\r\nSecond paragraph.", text.ToString());
        Assert.Equal(4, mirror.LineCount);
        Assert.Equal(2, mirror.DisplayLineCount);
        Assert.Equal(0, mirror.DisplayLineOf(2));
        Assert.Equal(1, mirror.DisplayLineOf(3));
        Assert.Equal(2, mirror.LineIndexAt(mirror.StartAt(2)));
        Assert.Equal(mirror.Length, text.Length);

        // Enter quotes the whole display line whichever fragment the caret is on (FR-2.9).
        Assert.Equal("claude: The first row, filled to the width and the rest of it. more, starting with a space.", mirror.DisplayTextAt(1));
        Assert.Equal(mirror.DisplayTextAt(0), mirror.DisplayTextAt(2));
        Assert.Equal("Second paragraph.", mirror.DisplayTextAt(3));
    }

    [Fact]
    public void A_line_that_becomes_a_continuation_swaps_its_line_break_for_a_space()
    {
        var mirror = new TranscriptMirror();
        var head = L("claude: A long row");
        var tail = L("that turns out to be its rest");
        var after = L("after");
        var text = Apply(new StringBuilder(), mirror.Update([head, tail, after]));
        var caret = mirror.StartAt(1) + 4;

        tail.JoinedToPrevious = true;
        var joined = mirror.Update([head, tail, after]);
        Apply(text, joined);
        Assert.Equal("claude: A long row that turns out to be its rest\r\nafter", text.ToString());
        Assert.Equal(mirror.StartAt(1) + 4, joined.MapPosition(caret));
        Assert.Equal(mirror.Length, text.Length);

        tail.JoinedToPrevious = false;
        Apply(text, mirror.Update([head, tail, after]));
        Assert.Equal("claude: A long row\r\nthat turns out to be its rest\r\nafter", text.ToString());
        Assert.Equal(mirror.Length, text.Length);
    }

    [Fact]
    public void Lines_are_inserted_and_removed_correctly_around_joined_lines()
    {
        var mirror = new TranscriptMirror();
        var head = L("head row");
        var rest = Joined("rest");
        var text = Apply(new StringBuilder(), mirror.Update([head, rest]));
        Assert.Equal("head row rest", text.ToString());

        // Remove the first line: the continuation becomes the first line and loses its separator.
        Apply(text, mirror.Update([rest]));
        Assert.Equal("rest", text.ToString());
        Assert.Equal(1, mirror.DisplayLineCount);

        // Put a line in front again, then append another continuation and a normal line.
        Apply(text, mirror.Update([head, rest]));
        Assert.Equal("head row rest", text.ToString());
        var more = Joined("more");
        var plain = L("plain");
        Apply(text, mirror.Update([head, rest, more, plain]));
        Assert.Equal("head row rest more\r\nplain", text.ToString());

        // Remove a continuation from the middle and the last plain line.
        Apply(text, mirror.Update([head, more]));
        Assert.Equal("head row more", text.ToString());
        Assert.Equal(mirror.Length, text.Length);
        Apply(text, mirror.Update([]));
        Assert.Equal("", text.ToString());
    }

    [Fact]
    public void Find_walks_the_lines_in_both_directions_and_wraps_around()
    {
        var mirror = new TranscriptMirror();
        mirror.Update([L("You: where is the cat"), L("Response 1:"), L("claude: The cat sat on the mat."), L("The dog did not."), L("A CAT again.")]);

        Assert.Equal(2, mirror.Find("cat", 0, backward: false, out var column, out var wrapped));
        Assert.Equal("claude: The ".Length, column);
        Assert.False(wrapped);

        Assert.Equal(4, mirror.Find("cat", 2, backward: false, out column, out wrapped));
        Assert.Equal(2, column);
        Assert.False(wrapped);

        Assert.Equal(0, mirror.Find("cat", 4, backward: false, out _, out wrapped));
        Assert.True(wrapped);

        Assert.Equal(4, mirror.Find("cat", 0, backward: true, out _, out wrapped));
        Assert.True(wrapped);

        Assert.Equal(2, mirror.Find("cat", 4, backward: true, out _, out wrapped));
        Assert.False(wrapped);

        // The starting line itself is searched last, so a lone match is found from itself.
        Assert.Equal(3, mirror.Find("dog", 3, backward: false, out _, out wrapped));
        Assert.True(wrapped);

        Assert.Equal(-1, mirror.Find("bird", 0, backward: false, out _, out _));
        Assert.Equal(-1, mirror.Find("", 0, backward: false, out _, out _));
        Assert.Equal(-1, new TranscriptMirror().Find("cat", 0, backward: false, out _, out _));
    }

    [Theory]
    [MemberData(nameof(FixtureTests.Fixtures), MemberType = typeof(FixtureTests))]
    public void Edits_reproduce_the_visible_text_and_keep_the_caret_on_its_line_for_every_frame(string name)
    {
        var bytes = Fixture(name);
        var (columns, rows) = FixtureSize(name);
        var model = new SessionModel(columns, rows);
        var mirror = new TranscriptMirror();
        var text = new StringBuilder();
        var caret = 0;
        var frames = 0;

        model.Changed += () =>
        {
            frames++;
            Line? before = null;
            var column = 0;
            if (mirror.LineCount > 0)
            {
                var index = mirror.LineIndexAt(caret);
                before = mirror.LineAt(index);
                column = caret - mirror.StartAt(index);
            }

            var update = mirror.Update(model.Lines);
            Apply(text, update);
            Assert.Equal(TranscriptMirror.Render(model.Lines), text.ToString());
            Assert.Equal(mirror.Length, text.Length);

            caret = update.MapPosition(caret);
            if (before is not null && !before.Hidden && model.Lines.Contains(before))
            {
                var index = mirror.LineIndexAt(caret);
                Assert.Same(before, mirror.LineAt(index));
                Assert.Equal(Math.Min(column, before.Text.Length), caret - mirror.StartAt(index));
            }

            // Park the caret a few characters into the newest line, where the row Claude is printing is rewritten
            // frame after frame, which is where a caret used to be thrown back to the start of the line.
            if (mirror.LineCount > 0)
            {
                var last = mirror.LineCount - 1;
                caret = mirror.StartAt(last) + Math.Min(3, mirror.TextAt(last).Length);
            }
        };

        var random = new Random(name.Length);
        var offset = 0;
        while (offset < bytes.Length)
        {
            var length = Math.Min(random.Next(1, 300), bytes.Length - offset);
            model.Feed(bytes.AsSpan(offset, length));
            offset += length;
        }

        model.EndFrame();
        Assert.True(frames > 1, $"Only {frames} frames");
    }
}

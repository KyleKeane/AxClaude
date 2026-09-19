using System.Text;
using AxClaude.Core.Transcript;

namespace AxClaude.Tests;

/// <summary>
/// Reading breaks (FR-3.10): once the reader has heard the line Claude is writing up to its end, the text that
/// arrives after that mark is rendered on a line of its own, so that Down Arrow reads only what is new. The breaks
/// are rendering only and go away when the view clears them.
/// </summary>
public class ReadingBreakTests
{
    private static Line L(string text) => new(0, LineKind.Plain, text);

    private static Line Joined(string text) => new(0, LineKind.Plain, text) { JoinedToPrevious = true };

    private static StringBuilder Apply(StringBuilder text, MirrorUpdate update)
    {
        foreach (var edit in update.Edits)
        {
            text.Remove(edit.Start, edit.OldLength).Insert(edit.Start, edit.Text);
        }

        return text;
    }

    [Fact]
    public void Text_appended_after_the_mark_goes_on_a_line_of_its_own()
    {
        var mirror = new TranscriptMirror();
        var marker = L("# Output 1");
        var reply = L("claude: The quick brown");
        var text = Apply(new StringBuilder(), mirror.Update([marker, reply]));

        mirror.BreakAtEnd(0);
        Assert.True(mirror.HasBreaks);
        Assert.False(mirror.Update([marker, reply]).Changed);

        reply.Text = "claude: The quick brown fox jumps";
        var update = mirror.Update([marker, reply]);
        Apply(text, update);
        Assert.Equal("# Output 1\r\nclaude: The quick brown \r\nfox jumps", text.ToString());
        Assert.Equal(text.Length, mirror.Length);

        // The caret stays on the heard part, and the reader's line is still one display line.
        var start = mirror.StartAt(1);
        Assert.Equal(start + 8, update.MapPosition(start + 8));
        Assert.Equal("claude: The quick brown fox jumps", mirror.DisplayTextAt(1));
        Assert.Equal(2, mirror.DisplayLineCount);
        Assert.Equal(1, mirror.BreakDisplayLine);

        // More text without a new mark extends the new line.
        reply.Text = "claude: The quick brown fox jumps over";
        Apply(text, mirror.Update([marker, reply]));
        Assert.Equal("# Output 1\r\nclaude: The quick brown \r\nfox jumps over", text.ToString());

        mirror.ClearBreaks();
        Apply(text, mirror.Update([marker, reply]));
        Assert.Equal("# Output 1\r\nclaude: The quick brown fox jumps over", text.ToString());
        Assert.False(mirror.HasBreaks);
        Assert.Equal(-1, mirror.BreakDisplayLine);
    }

    [Fact]
    public void A_mark_inside_a_word_moves_in_front_of_the_word_but_not_in_front_of_the_caret()
    {
        var mirror = new TranscriptMirror();
        var reply = L("claude: The qu");
        var text = Apply(new StringBuilder(), mirror.Update([reply]));
        mirror.BreakAtEnd(0);
        reply.Text = "claude: The quantum leap";
        Apply(text, mirror.Update([reply]));
        Assert.Equal("claude: The \r\nquantum leap", text.ToString());
        Assert.Equal(text.Length, mirror.Length);

        var other = new TranscriptMirror();
        var second = L("claude: The qu");
        var otherText = Apply(new StringBuilder(), other.Update([second]));
        other.BreakAtEnd(13);
        second.Text = "claude: The quantum leap";
        Apply(otherText, other.Update([second]));
        Assert.Equal("claude: The qu\r\nantum leap", otherText.ToString());
    }

    [Fact]
    public void A_row_joined_after_the_mark_starts_a_new_line_until_the_breaks_are_cleared()
    {
        var mirror = new TranscriptMirror();
        var head = L("row one");
        var text = Apply(new StringBuilder(), mirror.Update([head]));
        mirror.BreakAtEnd(0);

        var rest = Joined("row two");
        Apply(text, mirror.Update([head, rest]));
        Assert.Equal("row one\r\nrow two", text.ToString());
        Assert.Equal(text.Length, mirror.Length);
        Assert.Equal(1, mirror.DisplayLineCount);
        Assert.Equal("row one row two", mirror.DisplayTextAt(1));

        mirror.ClearBreaks();
        Apply(text, mirror.Update([head, rest]));
        Assert.Equal("row one row two", text.ToString());
        Assert.Equal(text.Length, mirror.Length);
    }

    [Fact]
    public void Several_marks_make_several_lines_and_columns_map_through_them()
    {
        var mirror = new TranscriptMirror();
        var reply = L("a b");
        var text = Apply(new StringBuilder(), mirror.Update([reply]));
        mirror.BreakAtEnd(0);
        reply.Text = "a b c d";
        Apply(text, mirror.Update([reply]));
        Assert.Equal("a b \r\nc d", text.ToString());

        // The reader moved to the new line (position 6) and heard it: mark again.
        mirror.BreakAtEnd(6);
        reply.Text = "a b c d e f";
        Apply(text, mirror.Update([reply]));
        Assert.Equal("a b \r\nc d \r\ne f", text.ToString());
        Assert.Equal(text.Length, mirror.Length);
        Assert.Equal(6, mirror.RenderedColumn(0, 4));
        Assert.Equal(12, mirror.RenderedColumn(0, 8));
        Assert.Equal("a b c d e f", mirror.DisplayTextAt(0));

        // Marking the same end twice adds nothing.
        mirror.BreakAtEnd(12);
        mirror.BreakAtEnd(12);
        reply.Text = "a b c d e f g";
        Apply(text, mirror.Update([reply]));
        Assert.Equal("a b \r\nc d \r\ne f \r\ng", text.ToString());
    }

    [Fact]
    public void A_line_that_shrinks_below_the_mark_loses_the_break()
    {
        var mirror = new TranscriptMirror();
        var reply = L("claude: Hello there");
        var text = Apply(new StringBuilder(), mirror.Update([reply]));
        mirror.BreakAtEnd(0);
        reply.Text = "claude: Hi";
        Apply(text, mirror.Update([reply]));
        Assert.Equal("claude: Hi", text.ToString());
        Assert.False(mirror.HasBreaks);

        reply.Text = "claude: Hi there again";
        Apply(text, mirror.Update([reply]));
        Assert.Equal("claude: Hi there again", text.ToString());
    }

    [Theory]
    [MemberData(nameof(FixtureTests.Fixtures), MemberType = typeof(FixtureTests))]
    public void Marking_every_frame_keeps_the_rendering_consistent_and_clearing_restores_the_plain_text(string name)
    {
        var bytes = File.ReadAllBytes(Path.Combine(FixtureDirectory(), name));
        var size = name.Contains("120x40") ? (120, 40) : (240, 50);
        var model = new SessionModel(size.Item1, size.Item2);
        var mirror = new TranscriptMirror();
        var text = new StringBuilder();

        model.Changed += () =>
        {
            // A reader who is always on the newest line: every frame marks its end as heard.
            mirror.BreakAtEnd(0);
            Apply(text, mirror.Update(model.Lines));
            Assert.Equal(mirror.Length, text.Length);
            for (var i = 0; i < mirror.LineCount; i++)
            {
                var rendered = text.ToString(mirror.StartAt(i), mirror.RenderedColumn(i, mirror.TextAt(i).Length));
                Assert.Equal(mirror.TextAt(i), rendered.Replace("\r\n", string.Empty));
            }
        };

        var random = new Random(name.Length);
        var offset = 0;
        while (offset < bytes.Length)
        {
            var count = Math.Min(bytes.Length - offset, random.Next(1, 300));
            model.Feed(bytes.AsSpan(offset, count));
            offset += count;
        }

        model.EndFrame();
        mirror.ClearBreaks();
        Apply(text, mirror.Update(model.Lines));
        Assert.Equal(TranscriptMirror.Render(model.Lines), text.ToString());
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

        throw new DirectoryNotFoundException("tests/fixtures");
    }
}

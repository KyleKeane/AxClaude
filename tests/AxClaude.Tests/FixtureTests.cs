using System.Text;
using System.Text.RegularExpressions;
using AxClaude.Core.Transcript;
using static AxClaude.Tests.TestHelpers;

namespace AxClaude.Tests;

/// <summary>Parses the recorded ConPTY sessions under tests/fixtures and compares the transcript with the golden files.</summary>
public class FixtureTests
{
    public static IEnumerable<object[]> Fixtures() =>
        Directory.GetFiles(FixtureDirectory(), "*.vt").OrderBy(f => f).Select(f => new object[] { Path.GetFileName(f) });

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Fixture_matches_expected_transcript(string name)
    {
        var model = ParseFixture(name);
        // The golden file is the transcript as the reader sees it: wrapped reply rows joined on one line (FR-3.2a).
        var actual = TranscriptMirror.Render(model.Lines).Replace("\r\n", "\n") + "\n";
        var expectedPath = Path.Combine(FixtureDirectory(), Path.ChangeExtension(name, ".expected.txt"));

        if (Environment.GetEnvironmentVariable("AXCLAUDE_UPDATE_EXPECTED") == "1")
        {
            File.WriteAllText(expectedPath, actual, new UTF8Encoding(false));
            return;
        }

        Assert.True(File.Exists(expectedPath), $"Missing golden file {expectedPath}. Run the tests once with AXCLAUDE_UPDATE_EXPECTED=1 and review the result.");
        var expected = File.ReadAllText(expectedPath).Replace("\r\n", "\n");
        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Fixture_parses_the_same_regardless_of_chunking(string name)
    {
        var bytes = Fixture(name);
        var whole = Visible(ParseFixture(name));

        var random = new Random(name.Length);
        var (columns, rows) = FixtureSize(name);
        var chunked = new SessionModel(columns, rows);
        var offset = 0;
        while (offset < bytes.Length)
        {
            var length = Math.Min(random.Next(1, 300), bytes.Length - offset);
            chunked.Feed(bytes.AsSpan(offset, length));
            offset += length;
        }

        chunked.EndFrame();
        Assert.Equal(whole, Visible(chunked));
        Assert.Equal(TranscriptMirror.Render(ParseFixture(name).Lines), TranscriptMirror.Render(chunked.Lines));
    }

    [Fact]
    public void Wrapped_reply_rows_are_joined_and_tool_rows_are_not()
    {
        const string name = "conversation-claude2.1.277-120x40.vt";
        var model = ParseFixture(name);
        var lines = model.Lines.Where(l => !l.Hidden).ToList();

        // "Blue sits between green and violet ... one" fills the 120 columns; " of the three primary colors of light." is its rest.
        var rest = lines.Single(l => l.Text.StartsWith(" of the three primary colors"));
        Assert.True(rest.JoinedToPrevious);
        Assert.Equal(0, rest.HeadingLevel);
        Assert.True(lines[lines.IndexOf(rest) - 1].Text.Length >= 100);

        // A wrapped "claude:" row continues over two more rows.
        var pasted = lines.Single(l => l.Text.StartsWith("claude: You pasted a two-line block"));
        var index = lines.IndexOf(pasted);
        Assert.True(lines[index + 1].JoinedToPrevious);
        Assert.True(lines[index + 2].JoinedToPrevious);
        Assert.Equal("a request.", lines[index + 2].Text);

        // Labelled rows, blank rows, headings and the rows under a tool line stay on their own.
        Assert.DoesNotContain(lines, l => l.JoinedToPrevious && (l.Kind != LineKind.Plain || l.Text.Length == 0));
        Assert.DoesNotContain(lines, l => l.JoinedToPrevious && l.HeadingLevel > 0);
        var text = TranscriptMirror.Render(model.Lines);
        Assert.Contains("nanometers. It is one of the three primary colors of light.\r\n", text);
    }

    [Fact]
    public void Conversation_fixture_keeps_replies_and_hides_chrome()
    {
        const string name = "conversation-claude2.1.277-240x50.vt";
        var model = ParseFixture(name);
        var visible = model.Lines.Where(l => !l.Hidden).ToList();
        var texts = visible.Select(l => l.Text).ToList();

        Assert.Single(texts, "claude: 2+2 = 4");
        Assert.DoesNotContain(texts, t => t.Contains("(shift+tab to cycle)"));
        Assert.DoesNotContain(texts, t => t.StartsWith("Tip: "));
        Assert.DoesNotContain(texts, t => Regex.IsMatch(t, @"^\S+…$"));
        Assert.Contains(texts, t => t.StartsWith("you: What is 2+2?"));
        Assert.Contains(texts, t => t.StartsWith("claude: Created "));
        Assert.Contains(texts, t => t.StartsWith("claude --resume "));

        var heading = visible.Single(l => l.Text == "Why People Like It");
        Assert.Equal(2, heading.HeadingLevel);
        Assert.Contains(visible, l => l.Kind == LineKind.TurnSummary);
        Assert.Contains(visible, l => l.Kind == LineKind.Tool && l.Text.EndsWith("(ctrl+o to expand)"));
        Assert.Equal(LineKind.UserEcho, visible.First(l => l.Text.StartsWith("you:")).Kind);
        Assert.False(model.PromptPending);
        Assert.False(model.Working);
        Assert.Equal("Arithmetic calculation", model.SessionName);
    }

}

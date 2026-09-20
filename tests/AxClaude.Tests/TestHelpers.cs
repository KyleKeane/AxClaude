using System.Text;
using System.Text.RegularExpressions;
using AxClaude.Core.Transcript;

namespace AxClaude.Tests;

/// <summary>What the test classes share: the fixture files, feeding a model, and the lines and edits of the mirror tests.</summary>
internal static partial class TestHelpers
{
    /// <summary>The tests/fixtures folder, found above the test binary.</summary>
    public static string FixtureDirectory()
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

    /// <summary>The bytes of a recording in tests/fixtures.</summary>
    public static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(FixtureDirectory(), name));

    /// <summary>The console size a fixture was recorded at, from its name (<c>-240x50.vt</c>).</summary>
    public static (int Columns, int Rows) FixtureSize(string name)
    {
        var match = SizeRegex().Match(name);
        return match.Success ? (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value)) : (120, 40);
    }

    /// <summary>A model of the fixture's size fed the whole recording, with the last frame ended.</summary>
    public static SessionModel ParseFixture(string name)
    {
        var (columns, rows) = FixtureSize(name);
        var model = new SessionModel(columns, rows);
        model.Feed(Fixture(name));
        model.EndFrame();
        return model;
    }

    public static void Feed(SessionModel model, string text) => model.Feed(Encoding.UTF8.GetBytes(text));

    /// <summary>The texts of the lines the reader sees.</summary>
    public static List<string> Visible(SessionModel model) => model.Lines.Where(l => !l.Hidden).Select(l => l.Text).ToList();

    /// <summary>A plain line for the mirror tests.</summary>
    public static Line L(string text) => new(0, LineKind.Plain, text);

    /// <summary>A line that continues the row before it (FR-3.2a).</summary>
    public static Line Joined(string text) => new(0, LineKind.Plain, text) { JoinedToPrevious = true };

    /// <summary>Applies a mirror update to a text the way the view applies it to the edit control.</summary>
    public static StringBuilder Apply(StringBuilder text, MirrorUpdate update)
    {
        foreach (var edit in update.Edits)
        {
            text.Remove(edit.Start, edit.OldLength).Insert(edit.Start, edit.Text);
        }

        return text;
    }

    [GeneratedRegex(@"-(\d+)x(\d+)\.vt$")]
    private static partial Regex SizeRegex();
}

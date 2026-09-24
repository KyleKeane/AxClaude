using System.Text.RegularExpressions;

namespace AxClaude.Core.Transcript;

/// <summary>A key a screen of Claude's names on its hint row: what to send, the key's name, and what it does there.</summary>
public sealed record ScreenKey(string Send, string Name, string Action);

/// <summary>
/// A screen of Claude's that waits for keys named on a hint row rather than on a prompt row (docs/claude-screens.md):
/// <c>/status</c>, <c>/tasks</c>, <c>/help</c>, <c>/goal</c> and the like. Its title, its lines, and the keys of its
/// hint row.
/// </summary>
public sealed record ClaudeScreen(string Title, IReadOnlyList<string> Lines, IReadOnlyList<ScreenKey> Keys)
{
    /// <summary>The screen row the screen starts on (its title or tab row), where the app marks it in the conversation.</summary>
    public int FirstRow { get; init; }

    /// <summary>
    /// What tells one screen from the next: the title and the keys. The lines are left out, since some screens change
    /// by themselves (a task's running time counts up every second); after a key of the user's the same screen is
    /// shown again all the same (MainForm).
    /// </summary>
    public string Signature => Title + "\n" + string.Join(" ", Keys.Select(key => key.Send));
}

/// <summary>
/// Reads a waiting screen off the rows around Claude's cursor. The cursor sits on a hint row that names Escape
/// (<c>↑/↓ to select · Enter to view · Esc to close</c>, <c>Esc to dismiss</c>), with the screen's lines above it up to
/// a labelled row or two blank rows; or it sits on a tab row (<c>Settings  Status   Config   Usage   Stats</c>) with the
/// screen's lines below it down to such a hint row. At Claude's own prompt the cursor is on the <c>$</c> row, so
/// ordinary work never looks like a screen.
/// </summary>
public static partial class ScreenReader
{
    private const int MaxRows = 60;

    // "Esc to close", "Esc/Enter/Space to close", "↑/↓ to select", "x to stop": a key, or keys joined by "/", then "to".
    [GeneratedRegex(@"^(?<keys>(?:Esc|Enter|Space|Tab|[←→↑↓]|[a-z])(?:/(?:Esc|Enter|Space|Tab|[←→↑↓]|[a-z]))*) to (?<action>.+)$")]
    private static partial Regex HintRegex();

    // Tab names separated by two spaces or more: three of them at least.
    [GeneratedRegex(@"^\S+(?: \S+)?(?:\s{2,}\S+(?: \S+)?){2,}$")]
    private static partial Regex TabRowRegex();

    /// <summary>The screen waiting around the cursor row, or null when the cursor is not on a hint or tab row.</summary>
    public static ClaudeScreen? Read(IReadOnlyList<string> rows, int cursorRow)
    {
        if (cursorRow < 0 || cursorRow >= rows.Count)
        {
            return null;
        }

        var cursorText = rows[cursorRow].Trim();
        if (Keys(cursorText) is { } keys)
        {
            var (lines, first) = LinesAbove(rows, cursorRow);
            return lines.Count == 0 ? null : new ClaudeScreen(lines[0], lines.Skip(1).ToList(), keys) { FirstRow = first };
        }

        if (TabRowRegex().IsMatch(cursorText))
        {
            var lines = new List<string>();
            for (var r = cursorRow + 1; r < rows.Count && r - cursorRow <= MaxRows; r++)
            {
                var text = rows[r].Trim();
                if (Keys(text) is { } tabKeys)
                {
                    return new ClaudeScreen("Tabs: " + string.Join(", ", Tabs(cursorText)), lines, tabKeys) { FirstRow = cursorRow };
                }

                if (text.Length > 0)
                {
                    lines.Add(text);
                }
            }
        }

        return null;
    }

    /// <summary>The keys of a hint row that names Escape, or null when the row is no such hint row.</summary>
    public static IReadOnlyList<ScreenKey>? Keys(string text)
    {
        var keys = new List<ScreenKey>();
        foreach (var part in text.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var match = HintRegex().Match(part);
            if (!match.Success)
            {
                return null;
            }

            var action = match.Groups["action"].Value;
            foreach (var key in match.Groups["keys"].Value.Split('/'))
            {
                keys.Add(new ScreenKey(Send(key), Name(key), action));
            }
        }

        return keys.Any(key => key.Send == "\x1b") ? keys : null;
    }

    private static (List<string> Lines, int First) LinesAbove(IReadOnlyList<string> rows, int hintRow)
    {
        var lines = new List<string>();
        var blanks = 0;
        var first = hintRow;
        for (var r = hintRow - 1; r >= 0 && hintRow - r <= MaxRows; r--)
        {
            var text = rows[r].Trim();
            if (text.Length == 0)
            {
                if (++blanks == 2)
                {
                    break;
                }

                continue;
            }

            if (LineClassifier.IsChrome(text) || LineClassifier.IsBarePrompt(text)
                || LineClassifier.Classify(text, inPromptBlock: false) is not (LineKind.Plain or LineKind.Prompt))
            {
                break;
            }

            blanks = 0;
            lines.Add(text);
            first = r;
        }

        lines.Reverse();
        return (lines, first);
    }

    private static IEnumerable<string> Tabs(string row) =>
        Regex.Split(row, @"\s{2,}").Select(tab => tab.Trim()).Where(tab => tab.Length > 0);

    private static string Send(string key) => key switch
    {
        "Esc" => "\x1b",
        "Enter" => "\r",
        "Space" => " ",
        "Tab" => "\t",
        "←" => "\x1b[D",
        "→" => "\x1b[C",
        "↑" => "\x1b[A",
        "↓" => "\x1b[B",
        _ => key,
    };

    private static string Name(string key) => key switch
    {
        "Esc" => "Escape",
        "←" => "Left",
        "→" => "Right",
        "↑" => "Up",
        "↓" => "Down",
        _ => key,
    };
}

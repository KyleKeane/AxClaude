using System.Text.RegularExpressions;

namespace AxClaude.Core.Transcript;

/// <summary>
/// Reads the question Claude waits on from the screen rows around its cursor. Screen reader mode draws every question
/// the same way (SPEC.md D33): a title row, text rows, the answers (<c>1. …</c>, or <c>y. …</c> and <c>n. …</c>,
/// with <c>(selected)</c> before the current one), the row the cursor sits on (<c>Enter y/n:</c>,
/// <c>Select with numbers [1-5]. …</c>), and key hints under it. The block ends upwards at two blank rows, a
/// labelled row (<c>you:</c>, <c>claude:</c>, <c>tool:</c> …) or a chrome row.
/// </summary>
public static partial class QuestionReader
{
    /// <summary>More rows than any question has (/config lists 44 settings); a runaway block is cut here.</summary>
    private const int MaxRows = 60;

    [GeneratedRegex(@"^(\d+|[yn])\. (?:(\(selected\)) )?(.*)$")]
    private static partial Regex OptionRegex();

    // AskUserQuestion draws its header as " ☐ Colour" (☒ once answered); only the recorded marks, since a plan's own
    // rows can start with other ticks.
    [GeneratedRegex(@"^\s*[☐☒✔]\s*")]
    private static partial Regex CheckBoxRegex();

    // With several questions in one call, a tab row above each: "←   ☐ Colour   ☐ Fruit   ✔ Submit   →".
    [GeneratedRegex(@"^←.*→$")]
    private static partial Regex QuestionTabsRegex();

    /// <summary>
    /// The question whose prompt row (the row the cursor sits on) is <paramref name="promptRow"/>, or null when
    /// that row is not a prompt row. <paramref name="rows"/> are the screen's rows, top to bottom.
    /// </summary>
    public static Question? Read(IReadOnlyList<string> rows, int promptRow)
    {
        // "Enter text for option 4 (Other)" asks for the user's own words, not for one of the answers still above it.
        if (promptRow < 0 || promptRow >= rows.Count || !LineClassifier.IsPromptText(rows[promptRow].Trim())
            || LineClassifier.IsTextPrompt(rows[promptRow].Trim()))
        {
            return null;
        }

        var hints = new List<string>();
        for (var i = promptRow + 1; i < rows.Count && hints.Count < MaxRows; i++)
        {
            var hint = rows[i].Trim();
            if (hint.Length > 0)
            {
                hints.Add(hint);
            }
        }

        // An answer longer than the console is wrapped onto the rows under it ("1. Claude's screens — Put the lines …"
        // over "theme in /theme …"). Rows that are not answers are held, and joined to the answer found above them when
        // the numbers show they belong to it; otherwise the answers end at the lowest answer row.
        var options = new List<QuestionOption>();
        var wrapped = new List<string>();
        var last = LastKey(rows[promptRow]);
        var top = promptRow;
        for (var row = promptRow - 1; row >= 0 && promptRow - row <= MaxRows; row--)
        {
            var line = rows[row].Trim();
            var match = OptionRegex().Match(line);
            if (!match.Success)
            {
                if (line.Length == 0)
                {
                    break;
                }

                wrapped.Insert(0, line);
                continue;
            }

            var key = match.Groups[1].Value;
            if (wrapped.Count > 0 && !(options.Count == 0 ? key == last : Follows(key, options[^1].Key)))
            {
                break;
            }

            var answer = wrapped.Count == 0 ? match.Groups[3].Value : match.Groups[3].Value + " " + string.Join(" ", wrapped);
            options.Add(new QuestionOption(key, answer, match.Groups[2].Success));
            wrapped.Clear();
            top = row;
        }

        // No answers above the prompt row: no question. Claude also clears a question's rows for a frame while it
        // redraws it, which is not a new question either.
        if (options.Count == 0)
        {
            return null;
        }

        options.Reverse();

        // A single blank row can sit inside a question (/resume has one between its search row and the answers);
        // two in a row end it, as do the rows that came before it.
        var text = new List<string>();
        var blanks = 0;
        (int, int)? step = null;
        // The first row: the top answer until a text row above it is found.
        var first = top;
        for (var row = top - 1; row >= 0 && promptRow - row <= MaxRows; row--)
        {
            var line = rows[row].Trim();
            if (line.Length == 0)
            {
                if (++blanks == 2)
                {
                    break;
                }

                continue;
            }

            // A tip Claude puts inside a permission question ("Tip: auto mode handles these prompts for you") is skipped.
            if (line.StartsWith("Tip: ", StringComparison.Ordinal))
            {
                continue;
            }

            if (EndsBlock(line))
            {
                step = QuestionTabsRegex().IsMatch(line) ? Step(line) : null;
                break;
            }

            blanks = 0;
            text.Add(line);
            first = row;
            if (CheckBoxRegex().IsMatch(rows[row]) || line.StartsWith("Permission Required:", StringComparison.Ordinal))
            {
                // A question's own header (" ☐ Colour", "Permission Required: Bash command") is its first row: what
                // is above it is the tool call or the message before, or a row left over from it ("/plan to preview").
                break;
            }
        }

        text.Reverse();

        // The trust dialog puts its title on a prompt row itself ("Permission Required: …"); a question without
        // text rows is titled by its prompt row.
        var title = text.Count > 0 ? text[0] : rows[promptRow].Trim();
        if (text.Count > 0)
        {
            text.RemoveAt(0);
        }

        title = CheckBoxRegex().Replace(title, string.Empty).TrimEnd(':', ' ');
        // "Select with numbers [1-4] (comma- or space-separated for several)": more than one answer may be given.
        var several = rows[promptRow].Contains("for several", StringComparison.Ordinal);
        return new Question(title, text, options, hints) { AllowsSeveral = several, FirstRow = first, Step = step };
    }

    /// <summary>The highest answer a prompt row names ("5" in "Select with numbers [1-5]", "n" in "Enter y/n:"), or null.</summary>
    private static string? LastKey(string promptRow) =>
        LastKeyRegex().Match(promptRow) is { Success: true } m ? m.Groups[1].Value
        : promptRow.TrimStart().StartsWith("Enter y/n", StringComparison.Ordinal) ? "n" : null;

    [GeneratedRegex(@"\[1-(\d+)\]")]
    private static partial Regex LastKeyRegex();

    /// <summary>True when answer <paramref name="key"/> comes just before <paramref name="below"/>: 2 before 3, y before n.</summary>
    private static bool Follows(string key, string below) =>
        (key, below) is ("y", "n") || (int.TryParse(key, out var a) && int.TryParse(below, out var b) && a + 1 == b);

    /// <summary>
    /// The number and count of the question under a tab row ("←   ☒ Colour   ☐ Fruit   ✔ Submit   →"): the tab row
    /// marks answered questions ☒ and not the current one, and Claude asks them in order, so the current one is the
    /// first ☐. Null when all are ticked: the review, under the Submit tab.
    /// </summary>
    private static (int, int)? Step(string tabRow)
    {
        var tabs = tabRow.Where(c => c is '☐' or '☒').ToList();
        var number = tabs.IndexOf('☐') + 1;
        return number > 0 ? (number, tabs.Count) : null;
    }

    /// <summary>
    /// A row that belongs to what came before the question, the tab row of several questions in one call, or the tab
    /// row of /config (<c>Settings  Status   Config …</c>), whose list is then titled by its prompt row. A warning or
    /// an error belongs to the question ("You have not answered all questions" on the review screen).
    /// </summary>
    private static bool EndsBlock(string line)
    {
        if (QuestionTabsRegex().IsMatch(line) || ScreenReader.IsTabRow(line) || LineClassifier.IsChrome(line) || LineClassifier.IsBarePrompt(line) || LineClassifier.IsDraftRow(line))
        {
            return true;
        }

        return LineClassifier.Classify(line, inPromptBlock: false) is not (LineKind.Plain or LineKind.Prompt or LineKind.Warning or LineKind.Error);
    }
}

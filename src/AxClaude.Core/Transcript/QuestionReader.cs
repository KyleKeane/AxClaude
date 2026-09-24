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
    /// <summary>More rows than any question has; a runaway block is cut here.</summary>
    private const int MaxRows = 40;

    [GeneratedRegex(@"^(\d+|[yn])\. (?:(\(selected\)) )?(.*)$")]
    private static partial Regex OptionRegex();

    // AskUserQuestion draws its header as " ☐ Colour" (a check box per question when there are several).
    [GeneratedRegex(@"^\s*[☐☒✔✓■□]\s*")]
    private static partial Regex CheckBoxRegex();

    /// <summary>
    /// The question whose prompt row (the row the cursor sits on) is <paramref name="promptRow"/>, or null when
    /// that row is not a prompt row. <paramref name="rows"/> are the screen's rows, top to bottom.
    /// </summary>
    public static Question? Read(IReadOnlyList<string> rows, int promptRow)
    {
        if (promptRow < 0 || promptRow >= rows.Count || !LineClassifier.IsPromptText(rows[promptRow].Trim()))
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

        var options = new List<QuestionOption>();
        var row = promptRow - 1;
        for (; row >= 0 && promptRow - row <= MaxRows; row--)
        {
            var match = OptionRegex().Match(rows[row].Trim());
            if (!match.Success)
            {
                break;
            }

            options.Add(new QuestionOption(match.Groups[1].Value, match.Groups[3].Value, match.Groups[2].Success));
        }

        options.Reverse();

        // A single blank row can sit inside a question (/resume has one between its search row and the answers);
        // two in a row end it, as do the rows that came before it.
        var text = new List<string>();
        var blanks = 0;
        for (; row >= 0 && promptRow - row <= MaxRows; row--)
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

            if (EndsBlock(line))
            {
                break;
            }

            blanks = 0;
            text.Add(line);
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
        return new Question(title, text, options, hints);
    }

    /// <summary>A row that belongs to what came before the question.</summary>
    private static bool EndsBlock(string line)
    {
        if (LineClassifier.IsChrome(line) || LineClassifier.IsBarePrompt(line) || LineClassifier.IsDraftRow(line))
        {
            return true;
        }

        return LineClassifier.Classify(line, inPromptBlock: false) is not (LineKind.Plain or LineKind.Prompt);
    }
}

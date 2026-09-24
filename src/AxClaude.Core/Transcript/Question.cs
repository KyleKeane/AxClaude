namespace AxClaude.Core.Transcript;

/// <summary>
/// One answer to a question: the key Claude takes for it (<c>1</c>, <c>2</c>, <c>y</c>, <c>n</c>), its text without
/// the key, and whether Claude marks it as the current choice (<c>(selected)</c>).
/// </summary>
public sealed record QuestionOption(string Key, string Text, bool Current = false);

/// <summary>
/// A question Claude waits to have answered (the workspace trust dialog, a permission prompt, a picker such as
/// <c>/model</c>, a question of Claude's own): its title, the lines between the title and the answers, the answers,
/// and the key hints Claude prints under the question (<c>s to use this session only</c>).
/// </summary>
public sealed record Question(string Title, IReadOnlyList<string> Text, IReadOnlyList<QuestionOption> Options, IReadOnlyList<string> Hints)
{
    /// <summary>What tells one question from the next: the title and the answers. The same question redrawn keeps it.</summary>
    public string Signature => Title + "\n" + string.Join("\n", Options.Select(option => option.Key + ". " + option.Text));

    /// <summary>The answer a key stands for, or null. Keys compare without regard to case.</summary>
    public QuestionOption? OptionFor(string key) =>
        Options.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase));
}

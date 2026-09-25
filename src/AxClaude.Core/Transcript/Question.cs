namespace AxClaude.Core.Transcript;

/// <summary>
/// One answer to a question: the key Claude takes for it (<c>1</c>, <c>2</c>, <c>y</c>, <c>n</c>), its text without
/// the key, and whether Claude marks it as the current choice (<c>(selected)</c>).
/// </summary>
public sealed record QuestionOption(string Key, string Text, bool Current = false)
{
    /// <summary>
    /// The name and value of a /config row ("Show tips: true", "Thinking mode: true (Thinking can't be turned off …)"),
    /// or null when the text has no <c>: </c>.
    /// </summary>
    public (string Name, string Value)? Setting =>
        Text.IndexOf(": ", StringComparison.Ordinal) is var colon and > 0 ? (Text[..colon], Text[(colon + 2)..]) : null;

    /// <summary>
    /// The value of a /config switch, a setting that is true or false, which Claude flips when its number is sent; null
    /// for a setting with other values, whose number opens Claude's list of them.
    /// </summary>
    public bool? Switch => Setting?.Value switch
    {
        "true" => true,
        "false" => false,
        { } value when value.StartsWith("true ", StringComparison.Ordinal) => true,
        { } value when value.StartsWith("false ", StringComparison.Ordinal) => false,
        _ => null,
    };
}

/// <summary>
/// A question Claude waits to have answered (the workspace trust dialog, a permission prompt, a picker such as
/// <c>/model</c>, a question of Claude's own): its title, the lines between the title and the answers, the answers,
/// and the key hints Claude prints under the question (<c>s to use this session only</c>).
/// </summary>
public sealed record Question(string Title, IReadOnlyList<string> Text, IReadOnlyList<QuestionOption> Options, IReadOnlyList<string> Hints)
{
    /// <summary>
    /// Several answers may be given at once, their numbers separated by commas ("1,3", typed one keystroke each; the
    /// same text in one write is a paste, which Claude ignores). The answer notice shows check boxes for them.
    /// </summary>
    public bool AllowsSeveral { get; init; }

    /// <summary>The screen row the question starts on (its title), where the app marks it in the conversation.</summary>
    public int FirstRow { get; init; }

    /// <summary>/config's list of settings, waiting on "Enter a number to change [1-44], or Escape to save and close:".</summary>
    public bool IsSettings => Title.StartsWith("Enter a number to change", StringComparison.Ordinal);

    /// <summary>What tells one question from the next: the title and the answers. The same question redrawn keeps it.</summary>
    public string Signature => Title + "\n" + string.Join("\n", Options.Select(option => option.Key + ". " + option.Text));

    /// <summary>Typed text that answers the question: one of its keys, or, when several answers are allowed, keys separated by spaces or commas.</summary>
    public bool Accepts(string text)
    {
        var keys = text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        return keys.Length > 0 && (keys.Length == 1 || AllowsSeveral) && keys.All(key => OptionFor(key) is not null);
    }

    /// <summary>The answer a key stands for, or null. Keys compare without regard to case.</summary>
    public QuestionOption? OptionFor(string key) =>
        Options.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase));
}

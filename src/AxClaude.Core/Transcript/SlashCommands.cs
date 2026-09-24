namespace AxClaude.Core.Transcript;

/// <summary>What a slash command asks of the user after it is sent.</summary>
public enum CommandInteraction
{
    /// <summary>Prints its output and returns to the prompt.</summary>
    Text,

    /// <summary>A list of answers, one to choose (<c>/model</c>).</summary>
    Picker,

    /// <summary>A list with several answers to tick, or with toggles.</summary>
    Multi,

    /// <summary>A settings or management screen with several pages or nested lists (<c>/config</c>, <c>/mcp</c>).</summary>
    Panel,

    /// <summary>A yes-or-no confirmation.</summary>
    Confirm,

    /// <summary>Asks for typed text.</summary>
    Input,

    /// <summary>Opens a browser, an editor or another program.</summary>
    External,

    /// <summary>Changes or ends the session (<c>/clear</c>, <c>/exit</c>, <c>/resume</c>).</summary>
    Session,

    /// <summary>Not known yet.</summary>
    Unknown,
}

/// <summary>A key a command's screen takes besides the answer keys, and what it does: <c>s</c> in <c>/model</c> uses the model for this session only.</summary>
public sealed record CommandKey(string Send, string Label);

/// <summary>
/// A built-in slash command: its name without the slash, its aliases, what it asks of the user, the extra keys of
/// its screen, and how the entry is known: <c>docs</c> (from the documentation only) or the Claude Code version it
/// was recorded with.
/// </summary>
public sealed record SlashCommand(string Name, CommandInteraction Interaction, string Source, IReadOnlyList<string>? Aliases = null, IReadOnlyList<CommandKey>? Keys = null)
{
    public IReadOnlyList<string> Aliases { get; init; } = Aliases ?? [];

    public IReadOnlyList<CommandKey> Keys { get; init; } = Keys ?? [];
}

/// <summary>The built-in slash commands the app knows (SPEC.md D33). Skills and plugins also appear as slash commands; they are not listed and count as unknown.</summary>
public static class SlashCommands
{
    public static readonly IReadOnlyList<SlashCommand> All =
    [
        new("model", CommandInteraction.Picker, "2.1.281", Keys: [new("s", "Use for this session only")]),
    ];

    /// <summary>The command a sent message starts with (<c>/model</c>, <c>/model sonnet</c>), or null for a message or an unknown command.</summary>
    public static SlashCommand? Find(string sent)
    {
        var text = sent.TrimStart();
        if (text.Length < 2 || text[0] != '/')
        {
            return null;
        }

        var end = text.IndexOfAny([' ', '\t', '\n']);
        var name = (end < 0 ? text[1..] : text[1..end]).ToLowerInvariant();
        return All.FirstOrDefault(command => command.Name == name || command.Aliases.Contains(name));
    }
}

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

    /// <summary>
    /// A screen to read that waits for keys named on its hint row, often with tabs (<c>/status</c>, <c>/help</c>,
    /// <c>/tasks</c>): the cursor sits on the hint row or the tab row, not on a prompt row.
    /// </summary>
    Viewer,

    /// <summary>A settings or management screen with tabs, a search field or nested lists (<c>/permissions</c>).</summary>
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

/// <summary>
/// A built-in slash command: its name without the slash, its aliases, what it asks of the user, and how the entry is
/// known: <c>docs</c> (from the documentation only) or the Claude Code version it was recorded with.
/// </summary>
public sealed record SlashCommand(string Name, CommandInteraction Interaction, string Source, IReadOnlyList<string>? Aliases = null)
{
    public IReadOnlyList<string> Aliases { get; init; } = Aliases ?? [];
}

/// <summary>The built-in slash commands the app knows (SPEC.md D33). Skills and plugins also appear as slash commands; they are not listed and count as unknown.</summary>
public static class SlashCommands
{
    // Recorded: opened bare in a scratch folder with Claude Code 2.1.281 and cancelled (docs/claude-screens.md).
    // Docs: from code.claude.com/docs/en/commands only; not opened, mostly because opening them already acts.
    // Mentioned: named in a hint on a recorded screen, in neither the docs' list nor the recordings.
    private const string Recorded = "2.1.281";
    private const string Docs = "docs";
    private const string Mentioned = "mentioned";

    public static readonly IReadOnlyList<SlashCommand> All =
    [
        // Numbered lists: the answer notice.
        new("model", CommandInteraction.Picker, Recorded),
        new("effort", CommandInteraction.Picker, Recorded),
        new("advisor", CommandInteraction.Picker, Recorded),
        new("theme", CommandInteraction.Picker, Recorded),
        new("export", CommandInteraction.Picker, Recorded),
        new("memory", CommandInteraction.Picker, Recorded),
        new("resume", CommandInteraction.Picker, Recorded),
        new("chrome", CommandInteraction.Picker, Recorded),
        new("mcp", CommandInteraction.Picker, Recorded),
        new("hooks", CommandInteraction.Picker, Recorded),
        // The settings: 44 numbered rows under the tab row and "Enter a number to change [1-44], or Escape to save and
        // close"; a number toggles its setting or opens a list of its values, and the settings list comes back.
        new("config", CommandInteraction.Picker, "2.1.282", Aliases: ["settings"]),

        // Screens to read, waiting on the keys of their hint row.
        new("status", CommandInteraction.Viewer, Recorded),
        new("usage", CommandInteraction.Viewer, Recorded),
        new("cost", CommandInteraction.Viewer, Recorded),
        new("help", CommandInteraction.Viewer, Recorded),
        new("diff", CommandInteraction.Viewer, Recorded),
        new("tasks", CommandInteraction.Viewer, Recorded),
        new("goal", CommandInteraction.Viewer, Recorded),
        new("mobile", CommandInteraction.Viewer, Recorded),
        new("rewind", CommandInteraction.Viewer, Recorded),
        new("ide", CommandInteraction.Viewer, Recorded),

        // Screens with tabs, search fields, sliders or a way on to typed text.
        new("permissions", CommandInteraction.Panel, Recorded),
        new("autocompact", CommandInteraction.Panel, Recorded),
        new("feedback", CommandInteraction.Panel, Recorded),
        new("artifacts", CommandInteraction.Panel, Recorded),
        new("auto-mode-setup", CommandInteraction.Panel, Docs),
        new("bug", CommandInteraction.Panel, Docs),

        // Text: printed into the conversation, back at the prompt.
        new("agents", CommandInteraction.Text, Recorded),
        new("output-style", CommandInteraction.Text, Recorded),
        new("import", CommandInteraction.Text, Recorded),
        new("copy", CommandInteraction.Text, Recorded),
        new("btw", CommandInteraction.Text, Recorded),
        new("context", CommandInteraction.Text, Recorded),
        new("list-agents", CommandInteraction.Text, Recorded),
        new("tui", CommandInteraction.Text, Recorded),
        new("color", CommandInteraction.Text, Docs),
        new("heapdump", CommandInteraction.Text, Docs),

        // Typed text after the command.
        new("add-dir", CommandInteraction.Input, Docs),
        new("cd", CommandInteraction.Input, Docs),

        // The session changes or ends; nothing to answer.
        new("clear", CommandInteraction.Session, Docs),
        new("compact", CommandInteraction.Session, Docs),
        new("exit", CommandInteraction.Session, Docs),
        new("logout", CommandInteraction.Session, Docs),
        new("plan", CommandInteraction.Session, Docs),
        new("branch", CommandInteraction.Session, Docs),
        new("fork", CommandInteraction.Session, Docs),
        new("background", CommandInteraction.Session, Docs),
        new("subtask", CommandInteraction.Session, Docs),
        new("teleport", CommandInteraction.Session, Docs),

        // Another program: a browser or an editor.
        new("login", CommandInteraction.External, Docs),
        new("keybindings", CommandInteraction.External, Docs),
        new("desktop", CommandInteraction.External, Docs),
        new("insights", CommandInteraction.External, Docs),
        new("install-github-app", CommandInteraction.External, Docs),
        new("install-slack-app", CommandInteraction.External, Docs),

        // Not known: the docs do not say what the bare command shows.
        new("init", CommandInteraction.Unknown, Docs),
        new("fast", CommandInteraction.Unknown, Docs),
        new("voice", CommandInteraction.Unknown, Docs),
        new("upgrade", CommandInteraction.Unknown, Docs),
        new("passes", CommandInteraction.Unknown, Docs),
        new("remote-control", CommandInteraction.Unknown, Docs, Aliases: ["rc"]),
        new("autofix-pr", CommandInteraction.Unknown, Docs),
        new("rename", CommandInteraction.Unknown, Mentioned),
        new("claim-credit", CommandInteraction.Unknown, Mentioned),
        new("usage-credits", CommandInteraction.Unknown, Mentioned),
        new("powerup", CommandInteraction.Unknown, Mentioned),
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

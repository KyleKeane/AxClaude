namespace AxClaude.Core.Transcript;

public enum LineKind
{
    Plain,
    InputMarker,
    OutputMarker,
    System,
    UserEcho,
    ClaudeReply,
    Thinking,
    Tool,
    ToolError,
    Error,
    Warning,
    Prompt,
    PromptOption,
    TurnSummary,

    /// <summary>A line of the user's message as the app printed it between the markers, or one of the block's blank lines (FR-4.1).</summary>
    UserMessage,

    /// <summary>A bookmark the reader dropped in front of a line with m: <c>Bookmark 3</c>, found again with k (FR-3.9).</summary>
    Bookmark,

    /// <summary>
    /// The app's record of a question or screen Claude waited on, <c>Question: Select model</c>, in front of its first row
    /// and found again with q (D33). Claude redraws an answered question as a one-line result, so this is what stays.
    /// </summary>
    Question,
}

/// <summary>
/// One logical line of the transcript. Screen rows keep a reference to their line while they are on screen,
/// so a row that is rewritten updates its line instead of adding a new one. Marker and system lines are
/// created by the app and have no row.
/// </summary>
public sealed class Line
{
    internal Line(int id, LineKind kind, string text)
    {
        Id = id;
        Kind = kind;
        Text = text;
    }

    public int Id { get; }
    public string Text { get; internal set; }
    public LineKind Kind { get; internal set; }
    public int HeadingLevel { get; internal set; }

    /// <summary>
    /// The row continues a reply row that Claude hard-wrapped at the console width (FR-3.2a): the view shows the two
    /// as one line, joined with a space.
    /// </summary>
    public bool JoinedToPrevious { get; internal set; }

    public bool Committed { get; internal set; }
    public bool Hidden => ChromeHidden || EchoHidden || ReplayHidden || RepaintHidden;
    /// <summary>A line the app inserted (a marker, a printed message line or blank line, a system line, a bookmark): never classified by text, never hidden.</summary>
    public bool IsMarker => Kind is LineKind.InputMarker or LineKind.OutputMarker or LineKind.System or LineKind.UserMessage or LineKind.Bookmark or LineKind.Question;

    internal bool ChromeHidden { get; set; }
    internal string? StickyChromeText { get; set; }
    internal bool EchoHidden { get; set; }
    internal bool EchoHandled { get; set; }
    /// <summary>Printed while a restart replayed a conversation the window already shows (FR-4.8).</summary>
    internal bool ReplayHidden { get; set; }

    /// <summary>
    /// Printed while Claude repainted the whole conversation from the top of the screen: a copy of what the window
    /// already shows, often worded differently. Shown once a later frame writes something else on its row.
    /// </summary>
    internal bool RepaintHidden { get; set; }

    internal Vt.Row? Row { get; set; }

    public override string ToString() => $"{Id}: [{Kind}] {Text}";
}

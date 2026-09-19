using System.Runtime.InteropServices;
using System.Windows.Forms.Automation;
using AxClaude.Core.Transcript;

namespace AxClaude;

/// <summary>
/// The read-only conversation view. Mirrors the visible transcript lines into a native multi-line edit control
/// with minimal edits, keeps the user's caret on the content it was on, and adds NVDA-style single-key navigation.
/// </summary>
internal sealed class TranscriptView : TextBox
{
    private const int WM_SETREDRAW = 0x000B;
    private const int EM_LINESCROLL = 0x00B6;
    private const int EM_SCROLLCARET = 0x00B7;
    private const int EM_GETLINECOUNT = 0x00BA;
    private const int EM_GETFIRSTVISIBLELINE = 0x00CE;
    private const int EM_CHARFROMPOS = 0x00D7;

    /// <summary>
    /// How long output is held back after a key press while the view has focus. The screen reader reads the caret
    /// line in several steps after a key (find the caret, find its line, fetch the text); text that shifts between
    /// those steps makes it read the wrong line. NVDA waits up to 100 ms for the caret to move before it reads.
    /// </summary>
    private const int NavigationHoldMs = 250;

    private sealed record QuickKey(Keys Key, string Name, Func<Line, bool> Matches);

    /// <summary>k and Shift+K: the bookmarks dropped with m (FR-3.9); the Navigate menu reaches them too.</summary>
    private static readonly QuickKey BookmarkKey = new(Keys.K, "bookmark", l => l.Kind == LineKind.Bookmark);

    private static readonly QuickKey[] QuickKeys =
    [
        new(Keys.H, "heading", l => l.HeadingLevel > 0),
        new(Keys.I, "input", l => l.Kind is LineKind.InputMarker or LineKind.UserEcho),
        new(Keys.O, "response", l => l.Kind == LineKind.OutputMarker),
        new(Keys.R, "response", l => l.Kind == LineKind.OutputMarker),
        new(Keys.C, "Claude reply", l => l.Kind == LineKind.ClaudeReply),
        new(Keys.T, "tool line", l => l.Kind is LineKind.Tool or LineKind.ToolError),
        new(Keys.P, "prompt", l => l.Kind == LineKind.Prompt),
        new(Keys.E, "error", l => l.Kind is LineKind.Error or LineKind.Warning or LineKind.ToolError),
        new(Keys.D, "turn summary", l => l.Kind == LineKind.TurnSummary),
        new(Keys.S, "system line", l => l.Kind == LineKind.System),
        new(Keys.B, "blank line", l => l.Text.Length == 0),
        BookmarkKey,
        new(Keys.D1, "heading level 1", l => l.HeadingLevel == 1),
        new(Keys.D2, "heading level 2", l => l.HeadingLevel == 2),
        new(Keys.D3, "heading level 3", l => l.HeadingLevel == 3),
        new(Keys.D4, "heading level 4", l => l.HeadingLevel == 4),
        new(Keys.D5, "heading level 5", l => l.HeadingLevel == 5),
        new(Keys.D6, "heading level 6", l => l.HeadingLevel == 6),
    ];

    private readonly TranscriptMirror _mirror = new();
    private readonly System.Windows.Forms.Timer _hold = new();
    private IReadOnlyList<Line>? _pending;
    private long _lastKeyTick = long.MinValue / 2;

    public TranscriptView()
    {
        Multiline = true;
        ReadOnly = true;
        WordWrap = true;
        ScrollBars = ScrollBars.Vertical;
        AcceptsTab = false;
        HideSelection = false;
        MaxLength = 0;
        BackColor = SystemColors.Window;
        ForeColor = SystemColors.WindowText;
        AccessibleName = "Conversation";
        _hold.Tick += (_, _) =>
        {
            _hold.Stop();
            if (_pending is { } lines)
            {
                _pending = null;
                Sync(lines);
            }
        };
    }

    /// <summary>Escape was pressed: the window moves focus to the message field.</summary>
    public event Action? EscapePressed;

    /// <summary>Enter was pressed on a line: its display number (from 1), the display line count and its full text (FR-2.9).</summary>
    public event Action<int, int, string>? LineChosen;

    /// <summary>m was pressed on a line (or Navigate → Bookmark this line): the window has the model bookmark it, or take the bookmark away (FR-3.9).</summary>
    public event Action<Line>? BookmarkToggled;

    /// <summary>
    /// True while a notice hides the view that the user was reading (D23): the caret then stays on its line as if
    /// the view still had the focus, instead of following the newest output, so that closing the notice returns to
    /// the same place.
    /// </summary>
    [System.ComponentModel.DefaultValue(false)]
    public bool KeepCaret { get; set; }

    /// <summary>Speaks text through a UI Automation notification without moving focus. Returns false when unsupported.</summary>
    public bool Announce(string text, bool interrupt)
    {
        if (!IsHandleCreated)
        {
            return false;
        }

        var processing = interrupt ? AutomationNotificationProcessing.MostRecent : AutomationNotificationProcessing.All;
        return AccessibilityObject.RaiseAutomationNotification(AutomationNotificationKind.ActionCompleted, processing, text);
    }

    /// <summary>Focuses the view and puts the caret on the newest response marker.</summary>
    public void JumpToLatestResponse()
    {
        Focus();
        for (var i = _mirror.LineCount - 1; i >= 0; i--)
        {
            if (_mirror.LineAt(i).Kind == LineKind.OutputMarker)
            {
                MoveTo(i);
                return;
            }
        }

        Announce("No response yet", true);
    }

    /// <summary>m, or Navigate → Bookmark this line: focuses the view and bookmarks the caret's line, or takes its bookmark away (FR-3.9).</summary>
    public void ToggleBookmark()
    {
        Focus();
        if (_mirror.LineCount == 0)
        {
            Announce("No lines yet", true);
            return;
        }

        BookmarkToggled?.Invoke(_mirror.LineAt(_mirror.LineIndexAt(SelectionStart)));
    }

    /// <summary>Navigate → Next or Previous bookmark: focuses the view and jumps as k or Shift+K would.</summary>
    public void JumpToBookmark(bool backward)
    {
        Focus();
        Jump(BookmarkKey, backward);
    }

    /// <summary>
    /// Focuses the view and moves the caret to the next (or previous) line containing the text, wrapping around at
    /// the ends (FR-3.7). The line is announced, prefixed when the search wrapped; "Not found" when nothing matches.
    /// </summary>
    public void Find(string text, bool backward)
    {
        Focus();
        var current = _mirror.LineCount == 0 ? 0 : _mirror.LineIndexAt(SelectionStart);
        var index = _mirror.Find(text, current, backward, out var column, out var wrapped);
        if (index < 0)
        {
            Announce($"Not found: {text}", true);
            return;
        }

        MoveTo(index, column, wrapped ? (backward ? "From the end, " : "From the top, ") : string.Empty);
    }

    /// <summary>
    /// Brings the control in line with the model's visible lines using as few edits as possible. Right after a key
    /// press in the focused view the update waits until the keys stop, so that the screen reader reads a line that
    /// holds still (see <see cref="NavigationHoldMs"/>); the lines are the model's live list, so nothing is lost.
    /// </summary>
    public void Sync(IReadOnlyList<Line> lines)
    {
        var sinceKey = Environment.TickCount64 - _lastKeyTick;
        if (Focused && sinceKey < NavigationHoldMs)
        {
            _pending = lines;
            _hold.Stop();
            _hold.Interval = (int)Math.Max(1, NavigationHoldMs - sinceKey);
            _hold.Start();
            return;
        }

        _pending = null;
        var selectionStart = SelectionStart;
        var selectionEnd = selectionStart + SelectionLength;
        var focused = Focused || KeepCaret;
        var update = _mirror.Update(lines);
        if (!update.Changed)
        {
            return;
        }

        SendMessage(Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        try
        {
            foreach (var edit in update.Edits)
            {
                Select(edit.Start, edit.OldLength);
                SelectedText = edit.Text;
            }

            if (focused)
            {
                // The caret stays on the transcript line and column it was on (FR-3.3), wherever that line moved.
                var start = update.MapPosition(selectionStart);
                var end = update.MapPosition(selectionEnd);
                Select(start, Math.Max(0, end - start));
            }
            else if (_mirror.LineCount > 0)
            {
                Select(_mirror.StartAt(_mirror.LineCount - 1), 0);
            }
        }
        finally
        {
            SendMessage(Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            Invalidate();
        }

        if (!focused)
        {
            // The caret now sits on the newest line; keep the window showing it, as a terminal would.
            ScrollCaretIntoView();
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        // Output that arrived while the view was unfocused moved the caret without the view following (Sync).
        ScrollCaretIntoView();
    }

    /// <summary>
    /// The edit control's own scroll-to-caret. WinForms' <c>ScrollToCaret</c> first fetches the whole text to see
    /// whether it is empty, a copy of the entire conversation on every call.
    /// </summary>
    private void ScrollCaretIntoView() => SendMessage(Handle, EM_SCROLLCARET, IntPtr.Zero, IntPtr.Zero);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.M && e.Modifiers == Keys.None)
        {
            // Before the hold stamp: the caret stays on its line, so the bookmark line may appear at once.
            e.Handled = e.SuppressKeyPress = true;
            ToggleBookmark();
            return;
        }

        _lastKeyTick = Environment.TickCount64;
        if (e.KeyCode == Keys.Escape)
        {
            e.Handled = e.SuppressKeyPress = true;
            EscapePressed?.Invoke();
            return;
        }

        if (!e.Control && !e.Alt)
        {
            if (e.KeyCode == Keys.Return && !e.Shift)
            {
                e.Handled = e.SuppressKeyPress = true;
                ChooseLine();
                return;
            }

            if (e.KeyCode is Keys.PageUp or Keys.PageDown && !e.Shift)
            {
                e.Handled = e.SuppressKeyPress = true;
                Page(e.KeyCode == Keys.PageDown ? 1 : -1);
                return;
            }

            if (e.KeyCode == Keys.L)
            {
                e.Handled = e.SuppressKeyPress = true;
                AnnounceLineNumber();
                return;
            }

            foreach (var key in QuickKeys)
            {
                if (key.Key == e.KeyCode)
                {
                    e.Handled = e.SuppressKeyPress = true;
                    Jump(key, e.Shift);
                    return;
                }
            }
        }

        base.OnKeyDown(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hold.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Jump(QuickKey key, bool backward)
    {
        var step = backward ? -1 : 1;
        if (_mirror.LineCount > 0)
        {
            var current = _mirror.LineIndexAt(SelectionStart);
            for (var i = current + step; i >= 0 && i < _mirror.LineCount; i += step)
            {
                if (key.Matches(_mirror.LineAt(i)))
                {
                    MoveTo(i);
                    return;
                }
            }
        }

        Announce($"No {(backward ? "previous" : "next")} {key.Name}", true);
    }

    /// <summary>
    /// Page Up and Page Down move the caret by one screen of wrapped lines, counted from the caret rather than from
    /// whatever is scrolled into view, and reach the first and last line. The native keys scroll the view and keep the
    /// caret on the same screen row, which strands the caret when its line is off screen and never reaches the ends.
    /// The screen reader reads the new line itself, so nothing is announced here.
    /// </summary>
    private void Page(int direction)
    {
        var lineCount = (int)SendMessage(Handle, EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero);
        var first = (int)SendMessage(Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
        var bottomPoint = (IntPtr)((Math.Max(0, ClientSize.Height - 1) << 16) | 1);
        var atBottom = (int)SendMessage(Handle, EM_CHARFROMPOS, IntPtr.Zero, bottomPoint);
        var last = atBottom == -1 ? first : (atBottom >> 16) & 0xFFFF;
        var page = Math.Max(1, last - first);
        var caretLine = GetLineFromCharIndex(SelectionStart);
        var target = Math.Clamp(caretLine + direction * page, 0, Math.Max(0, lineCount - 1));
        if (target == caretLine)
        {
            return;
        }

        Select(GetFirstCharIndexFromLine(target), 0);
        if (caretLine >= first && caretLine <= last)
        {
            // The caret was on screen: scroll by the same amount so it keeps its screen row.
            SendMessage(Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)(target - caretLine));
        }

        ScrollCaretIntoView();
    }

    /// <summary>The l key: the transcript line the caret is on, spoken only on request. Joined wrapped rows count once.</summary>
    private void AnnounceLineNumber()
    {
        if (_mirror.LineCount == 0)
        {
            Announce("No lines yet", true);
            return;
        }

        var index = _mirror.LineIndexAt(SelectionStart);
        Announce($"Line {_mirror.DisplayLineOf(index) + 1} of {_mirror.DisplayLineCount}", true);
    }

    /// <summary>Enter: hands the caret's line, as the reader sees it (joined rows as one), to the window to quote in the message.</summary>
    private void ChooseLine()
    {
        if (_mirror.LineCount == 0)
        {
            Announce("No lines yet", true);
            return;
        }

        var index = _mirror.LineIndexAt(SelectionStart);
        LineChosen?.Invoke(_mirror.DisplayLineOf(index) + 1, _mirror.DisplayLineCount, _mirror.DisplayTextAt(index));
    }

    /// <summary>Puts the caret on a visible line (at the start, or at a column), scrolls to it and announces its text.</summary>
    private void MoveTo(int index, int column = 0, string prefix = "")
    {
        var line = _mirror.LineAt(index);
        Select(_mirror.StartAt(index) + Math.Min(column, _mirror.TextAt(index).Length), 0);
        ScrollCaretIntoView();
        var text = line.Text.Length == 0 ? "blank" : line.Text;
        if (line.HeadingLevel > 0)
        {
            text += $" heading level {line.HeadingLevel}";
        }

        if (line.Kind == LineKind.Bookmark && index + 1 < _mirror.LineCount)
        {
            // The bookmark stands in front of the line it marks: say that line too, so one k press tells where it is.
            var marked = _mirror.DisplayTextAt(index + 1);
            text += ", " + (marked.Length == 0 ? "blank" : marked);
        }

        if (!Announce(prefix + text, true))
        {
            // No UI Automation notifications: select the line so the screen reader reports the selection.
            Select(_mirror.StartAt(index), _mirror.TextAt(index).Length);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}

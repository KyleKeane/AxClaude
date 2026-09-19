using System.Text;
using System.Text.RegularExpressions;
using AxClaude.Core.Vt;

namespace AxClaude.Core.Transcript;

/// <summary>
/// Owns the transcript: the ordered list of lines built from the screen plus the marker and system lines the app
/// inserts. Feed it the raw console bytes; it raises <see cref="Changed"/> at the end of every frame.
/// Single threaded: call everything from one thread.
/// </summary>
public sealed partial class SessionModel : ILineStore
{
    /// <summary>A sent message and the block the app prints for it (FR-4.1); <c>Block[0]</c> is in the transcript once the block is placed.</summary>
    private sealed record PendingSend(string Text, IReadOnlyList<Line> Block, DateTime Created);

    private sealed record HandledSend(PendingSend Send, Line Echo);

    // A message sent while Claude is busy is echoed only when Claude gets to it, which can take minutes.
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(30);

    /// <summary>Spoken when Ctrl+O replaces the prompt with Claude's detailed view, and when the prompt is back.</summary>
    public const string TranscriptViewOpened = "Claude's detailed view is on. Press Ctrl+O to turn it off.";
    public const string TranscriptViewClosed = "Claude's detailed view is off.";

    private readonly List<Line> _lines = [];
    private readonly List<PendingSend> _pending = [];
    private readonly List<HandledSend> _handled = [];
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private char[] _chars = new char[4096];
    private Screen _screen = null!;
    private VtParser _parser = null!;
    private Line? _anchor;
    private Line? _responseStartedFor;
    private int _nextId;
    private int _bookmarkCount;

    public SessionModel(int columns, int rows)
    {
        Columns = columns;
        Rows = rows;
        NewScreen();
    }

    public int Columns { get; }
    public int Rows { get; }
    public IReadOnlyList<Line> Lines => _lines;
    public int ResponseCount { get; private set; }

    /// <summary>The last content line is a prompt or one of its options, so Claude is waiting for an answer.</summary>
    public bool PromptPending { get; private set; }

    /// <summary>A spinner row or an "esc to interrupt" hint was on screen at the end of the last frame.</summary>
    public bool Working { get; private set; }

    /// <summary>
    /// While set, every line Claude prints is hidden: a restart in the same folder with --continue replays a
    /// conversation the window already shows (FR-4.8). The window clears it once Claude is ready; a question or an
    /// exit before that shows the hidden lines after all (<see cref="ShowHiddenLines"/>).
    /// </summary>
    public bool HideReplay { get; set; }

    public string? Spinner { get; private set; }

    /// <summary>Messages Claude shows as queued inside its working block (sent while it was busy, not yet taken up).</summary>
    public int QueuedMessages { get; private set; }

    /// <summary>Ctrl+O opened Claude's detailed transcript view: the prompt is gone until Ctrl+O is pressed again.</summary>
    public bool TranscriptViewOpen { get; private set; }

    public string? Mode { get; private set; }
    public string? SessionName { get; private set; }

    /// <summary>Above this many lines the oldest tenth of the transcript is dropped (FR-3.6).</summary>
    public int MaxLines { get; set; } = 20000;

    /// <summary>Show reply rows that Claude hard-wrapped at the console width as one line (FR-3.2a).</summary>
    public bool JoinWrappedLines { get; set; } = true;

    /// <summary>Put the time of sending into the markers: <c>You (14:32): …</c> and <c>Response 3 (14:32):</c> (FR-4.6).</summary>
    public bool TimeStamps { get; set; }

    /// <summary>The clock the time stamps use; tests replace it.</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    /// <summary>Raised after every frame and after every marker or system line insertion.</summary>
    public event Action? Changed;

    public event Action? Bell;

    /// <summary>Raised once per response marker when the first reply, thinking or tool line appears after it.</summary>
    public event Action? ResponseStarted;

    /// <summary>Raised with the text of one of Claude's own bracketed announcement rows, such as "accept edits on".</summary>
    public event Action<string>? Announcement;

    public void Feed(ReadOnlySpan<byte> bytes)
    {
        if (_chars.Length < bytes.Length + 4)
        {
            _chars = new char[Math.Max(bytes.Length + 4, _chars.Length * 2)];
        }

        var count = _decoder.GetChars(bytes, _chars, false);
        _parser.Feed(_chars.AsSpan(0, count));
    }

    /// <summary>Recomputes chrome, classification, echo handling and status from the current screen.</summary>
    public void EndFrame()
    {
        _pending.RemoveAll(p => DateTime.UtcNow - p.Created > PendingLifetime);
        PullRowText();
        _anchor = ApplyChrome(out var spinner, out var mode, out var working);
        Spinner = spinner;
        Mode = mode ?? Mode;
        Working = working;
        Classify();
        JoinWrappedRows();
        HandleEchoes();
        PlaceBlocksWithoutEcho();
        PromptPending = LastContentLine()?.Kind is LineKind.Prompt or LineKind.PromptOption;
        CheckResponseStarted();
        Trim();
        Changed?.Invoke();
    }

    /// <summary>
    /// Records that the user sent a message and prints its exchange block: the input marker, the message lines, a
    /// blank line, the output marker and a blank line (FR-4.1). While Claude is idle the block goes in at once at
    /// the end of the conversation, above the chrome block, where the <c>you:</c> echo is about to appear (and is
    /// hidden). While Claude works the message is held outside the conversation: Claude queues it and draws it
    /// inside its working block, and the block is printed only when Claude takes the message up and echoes it
    /// (FR-4.7), so the message and its response arrive together. Nothing is moved afterwards (D22).
    /// Returns false when no block applies (empty text, a slash command, or an answer to a waiting prompt).
    /// </summary>
    public bool Send(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('/') || PromptPending)
        {
            return false;
        }

        ResponseCount++;
        // The exchange block (FR-4.1): both markers are level 1 headings, so the heading keys stop at every exchange;
        // Claude's own headings are level 2 and deeper (LineClassifier.HeadingLevel). Classify skips app lines, so
        // the levels stick and a message line is never mistaken for a label.
        var stamp = TimeStamps ? $" ({Clock():HH:mm})" : string.Empty;
        var block = new List<Line> { new(_nextId++, LineKind.InputMarker, $"# Input {ResponseCount}{stamp}") { HeadingLevel = 1 } };
        foreach (var messageLine in trimmed.Replace("\r\n", "\n").Split('\n'))
        {
            block.Add(new Line(_nextId++, LineKind.UserMessage, messageLine.TrimEnd()));
        }

        block.Add(new Line(_nextId++, LineKind.UserMessage, string.Empty));
        block.Add(new Line(_nextId++, LineKind.OutputMarker, $"# Output {ResponseCount} Reply from Claude{stamp}") { HeadingLevel = 1 });
        block.Add(new Line(_nextId++, LineKind.UserMessage, string.Empty));
        var send = new PendingSend(trimmed, block, DateTime.UtcNow);
        _pending.Add(send);
        if (!Working)
        {
            InsertBlock(send, AnchorIndex());
            Changed?.Invoke();
        }

        return true;
    }

    public void AddSystemLine(string text)
    {
        _lines.Insert(AnchorIndex(), new Line(_nextId++, LineKind.System, "System: " + text));
        Changed?.Invoke();
    }

    /// <summary>
    /// The m key (FR-3.9): drops a bookmark in front of <paramref name="line"/>, or takes it away again when the line
    /// has one, or is one. A bookmark is an app line, <c>Bookmark 3</c>, so it is read in place, found with k and
    /// saved with the conversation. It goes in front of the first row of the line as the reader sees it: a
    /// continuation row (FR-3.2a) joins the visible row before it. Returns the bookmark line and whether it was
    /// added, or null when the line is no longer in the transcript.
    /// </summary>
    public (Line Bookmark, bool Added)? ToggleBookmark(Line line)
    {
        var first = _lines.LastIndexOf(line);
        if (first < 0)
        {
            return null;
        }

        var before = VisibleBefore(first);
        while (_lines[first].JoinedToPrevious && before >= 0)
        {
            first = before;
            before = VisibleBefore(first);
        }

        var existing = line.Kind == LineKind.Bookmark ? line
            : before >= 0 && _lines[before].Kind == LineKind.Bookmark ? _lines[before]
            : null;
        if (existing is not null)
        {
            _lines.Remove(existing);
            Changed?.Invoke();
            return (existing, false);
        }

        var bookmark = new Line(_nextId++, LineKind.Bookmark, $"Bookmark {++_bookmarkCount}");
        _lines.Insert(first, bookmark);
        Changed?.Invoke();
        return (bookmark, true);
    }

    private int VisibleBefore(int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            if (!_lines[i].Hidden)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Freezes every line that is still on screen and starts a fresh screen (used when Claude restarts).</summary>
    /// <summary>
    /// The exchange blocks for the conversation Claude replays at startup with --continue (FR-4.8). Each visible
    /// <c>you:</c> row that is not a slash command gets <c># Input n from previous session</c> in front of it and,
    /// before the reply row that follows, a blank line, <c># Output n from previous session Reply from Claude</c>
    /// and a blank line; the <c>you:</c> rows stay as the message. n counts from 1 in replay order; the live
    /// numbering is separate and starts at 1 for every run of the app. Rows before the last system line belong to
    /// an earlier run in the same window and were marked then. The last past output marker counts as a response
    /// already started, so the replay never announces "Claude is responding". Called once Claude is ready.
    /// </summary>
    public void MarkReplayedExchanges()
    {
        var start = 0;
        for (var i = _lines.Count - 1; i >= 0; i--)
        {
            if (_lines[i].Kind == LineKind.System)
            {
                start = i + 1;
                break;
            }
        }

        var n = 0;
        var changed = false;
        var end = AnchorIndex();
        for (var i = start; i < end; i++)
        {
            var line = _lines[i];
            if (line.Hidden || line.IsMarker || !IsPastMessage(line.Text))
            {
                continue;
            }

            n++;
            if (i > start && _lines[i - 1].Kind == LineKind.InputMarker)
            {
                continue;
            }

            _lines.Insert(i, new Line(_nextId++, LineKind.InputMarker, $"# Input {n} from previous session") { HeadingLevel = 1 });
            i++;
            end++;
            var reply = ReplyStart(i + 1, end);
            if (reply > 0)
            {
                var output = new Line(_nextId++, LineKind.OutputMarker, $"# Output {n} from previous session Reply from Claude") { HeadingLevel = 1 };
                _lines.InsertRange(reply, [new Line(_nextId++, LineKind.UserMessage, string.Empty), output, new Line(_nextId++, LineKind.UserMessage, string.Empty)]);
                _responseStartedFor = output;
                end += 3;
                i = reply + 2;
            }

            changed = true;
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>A replayed message row: <c>you:</c> followed by something other than a slash command.</summary>
    private static bool IsPastMessage(string text)
    {
        if (!text.StartsWith("you:", StringComparison.Ordinal))
        {
            return false;
        }

        var rest = text.AsSpan("you:".Length).TrimStart();
        return rest.Length > 0 && rest[0] != '/';
    }

    /// <summary>The first reply row (claude:, thinking:, tool:) at or after <paramref name="from"/>, or -1 when another message comes first.</summary>
    private int ReplyStart(int from, int end)
    {
        for (var j = from; j < end; j++)
        {
            var line = _lines[j];
            if (line.Hidden || line.IsMarker)
            {
                continue;
            }

            if (line.Text.StartsWith("you:", StringComparison.Ordinal))
            {
                return -1;
            }

            if (line.Text.StartsWith("claude:", StringComparison.Ordinal) || line.Text.StartsWith("thinking:", StringComparison.Ordinal) || line.Text.StartsWith("tool", StringComparison.Ordinal))
            {
                return j;
            }
        }

        return -1;
    }

    /// <summary>Stops hiding and shows every line hidden as a replay: Claude asked something or exited before it was ready (FR-4.8).</summary>
    public void ShowHiddenLines()
    {
        HideReplay = false;
        var changed = false;
        foreach (var line in _lines)
        {
            changed |= line.ReplayHidden;
            line.ReplayHidden = false;
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>True when Claude's last lines say it found no conversation to continue (FR-9.1).</summary>
    public bool SaidNoConversationToContinue()
    {
        for (var i = _lines.Count - 1; i >= 0 && i >= _lines.Count - 20; i--)
        {
            if (_lines[i].Text.StartsWith("No conversation found to continue", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public void ResetScreen()
    {
        PullRowText();
        for (var r = 0; r < Rows; r++)
        {
            var row = _screen.RowAt(r);
            if (row.Line is { } line)
            {
                Commit(line);
                row.Line = null;
            }
        }

        NewScreen();
        Working = false;
        Spinner = null;
        PromptPending = false;
        TranscriptViewOpen = false;
        Changed?.Invoke();
    }

    // ---- ILineStore ----

    // Screen lines sit at the end of the list, so they are searched from the end.

    public Line CreateLine(Line? before)
    {
        var line = new Line(_nextId++, LineKind.Plain, string.Empty) { ReplayHidden = HideReplay };
        var index = before is null ? -1 : _lines.LastIndexOf(before);
        if (index < 0)
        {
            _lines.Add(line);
        }
        else
        {
            _lines.Insert(index, line);
        }

        return line;
    }

    public void Commit(Line line)
    {
        line.Committed = true;
        line.Row = null;
    }

    public void Remove(Line line)
    {
        var index = _lines.LastIndexOf(line);
        if (index >= 0)
        {
            _lines.RemoveAt(index);
        }

        line.Row = null;
    }

    // ---- internals ----

    private void NewScreen()
    {
        _screen = new Screen(Columns, Rows, this);
        _screen.FrameEnd += EndFrame;
        _screen.Bell += () => Bell?.Invoke();
        _screen.TitleChanged += OnTitle;
        _parser = new VtParser(_screen);
        _decoder.Reset();
        _anchor = null;
    }

    private void PullRowText()
    {
        for (var r = 0; r < Rows; r++)
        {
            var row = _screen.RowAt(r);
            if (row.Line is { } line && row.Dirty)
            {
                var text = row.Text();
                row.Dirty = false;
                if (text != line.Text)
                {
                    // The row now shows something else: whatever the echo handling decided about it no longer applies,
                    // and a row that held hidden replay text now holds live text once the replay is over.
                    line.Text = text;
                    line.EchoHidden = false;
                    line.EchoHandled = false;
                    line.ReplayHidden &= HideReplay;
                }
            }
        }
    }

    /// <summary>
    /// Hides interface rows: everything below the cursor row, the prompt row itself, and the contiguous block of
    /// mode, spinner and tip rows directly above it. Returns the topmost line of that block, which is where the
    /// next marker is inserted.
    /// </summary>
    private Line? ApplyChrome(out string? spinner, out string? mode, out bool working)
    {
        var cy = _screen.CursorRow;
        Line? anchor = null;
        spinner = null;
        mode = null;
        working = false;
        var blockOpen = true;
        var inQueued = false;
        var queued = 0;
        var underAnnouncement = false;
        var transcriptView = false;
        List<string>? announcements = null;
        for (var r = Rows - 1; r >= 0; r--)
        {
            var line = _screen.LineAt(r);
            if (line is null)
            {
                continue;
            }

            bool chrome;
            if (r > cy)
            {
                chrome = true;
            }
            else if (!blockOpen)
            {
                // A row hidden as chrome earlier stays hidden until it is rewritten (for example the
                // slash-command list that remains on screen after /exit).
                chrome = line.StickyChromeText is { } sticky && sticky == line.Text;
            }
            else if (r == cy)
            {
                // Claude parks the cursor on an announcement row ("[accept edits on]") for a moment, under the prompt.
                underAnnouncement = LineClassifier.AnnouncementText(line.Text) is not null;
                transcriptView = LineClassifier.IsTranscriptViewRow(line.Text);
                chrome = LineClassifier.IsPromptRow(line.Text) || underAnnouncement || transcriptView;
            }
            else if (inQueued)
            {
                // A message sent while Claude was busy is drawn inside the block until Claude takes it up;
                // its rows move as the block changes, so they are not the echo the markers wait for.
                chrome = true;
                inQueued = !line.Text.StartsWith("you:", StringComparison.Ordinal);
            }
            else
            {
                chrome = line.Text.Length == 0 || LineClassifier.IsChrome(line.Text)
                    || (underAnnouncement && LineClassifier.IsBarePrompt(line.Text));
                underAnnouncement = false;
                inQueued = chrome && LineClassifier.IsQueuedHint(line.Text);
                if (inQueued)
                {
                    queued++;
                }
            }

            if (r <= cy)
            {
                if (chrome)
                {
                    if (blockOpen)
                    {
                        if (line.StickyChromeText != line.Text && LineClassifier.AnnouncementText(line.Text) is { } announcement)
                        {
                            (announcements ??= []).Add(announcement);
                        }

                        line.StickyChromeText = line.Text;
                    }

                    anchor = line;
                    spinner ??= LineClassifier.SpinnerText(line.Text);
                    if (LineClassifier.ModeText(line.Text) is { } m)
                    {
                        mode = m;
                        working |= line.Text.Contains("esc to interrupt", StringComparison.Ordinal);
                    }
                }
                else
                {
                    blockOpen = false;
                }
            }

            line.ChromeHidden = chrome;
        }

        working |= spinner is not null;
        QueuedMessages = queued;
        if (transcriptView && !TranscriptViewOpen)
        {
            (announcements ??= []).Add(TranscriptViewOpened);
        }
        else if (!transcriptView && TranscriptViewOpen)
        {
            (announcements ??= []).Add(TranscriptViewClosed);
        }

        TranscriptViewOpen = transcriptView;

        // The slash-command completion list and the echoed draft can end a frame with the cursor inside them,
        // so they are hidden wherever they sit above the cursor. Continuation rows only count directly under a list row.
        var inList = false;
        for (var r = 0; r <= cy; r++)
        {
            var line = _screen.LineAt(r);
            if (line is null)
            {
                inList = false;
                continue;
            }

            var completion = LineClassifier.IsCompletionRow(line.Text);
            var continuation = inList && LineClassifier.IsCompletionContinuation(line.Text);
            if (completion || continuation || LineClassifier.IsDraftRow(line.Text) || LineClassifier.IsNotepadHint(line.Text))
            {
                line.ChromeHidden = true;
                line.StickyChromeText = line.Text;
            }

            inList = completion || continuation;
        }

        if (announcements is not null)
        {
            foreach (var announcement in announcements)
            {
                Announcement?.Invoke(announcement);
            }
        }

        return anchor;
    }

    private void Classify()
    {
        for (var i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            if (line.Committed || line.IsMarker)
            {
                continue;
            }

            var inPromptBlock = false;
            for (int j = i - 1, seen = 0; j >= 0 && seen < 12; j--)
            {
                var previous = _lines[j];
                if (previous.Hidden || previous.IsMarker)
                {
                    continue;
                }

                seen++;
                if (previous.Kind == LineKind.Prompt)
                {
                    inPromptBlock = true;
                    break;
                }
            }

            line.Kind = LineClassifier.Classify(line.Text, inPromptBlock);

            string? next = null;
            for (var j = i + 1; j < _lines.Count; j++)
            {
                var candidate = _lines[j];
                if (candidate.ChromeHidden || candidate.IsMarker)
                {
                    continue;
                }

                next = candidate.Text;
                break;
            }

            line.HeadingLevel = LineClassifier.HeadingLevel(line.Text, line.Kind, next);
        }
    }

    /// <summary>
    /// Claude hard-wraps long reply rows at the console width (§4.4 item 4). A plain row inside a <c>claude:</c> or
    /// <c>you:</c> block that follows a row within 20 characters of the width, and that starts with a space or a
    /// lowercase letter, is the rest of that row: the view shows the two as one line (FR-3.2a). Rows of a tool block
    /// never join, and a joined row is never a heading. Committed lines keep the flag they were given.
    /// </summary>
    private void JoinWrappedRows()
    {
        Line? previous = null;
        var inReply = false;
        foreach (var line in _lines)
        {
            if (line.Kind == LineKind.Bookmark)
            {
                // A bookmark stands in front of a row without ending the block or breaking the row off (FR-3.9).
                continue;
            }

            if (line.IsMarker)
            {
                previous = null;
                inReply = false;
                continue;
            }

            if (line.Hidden)
            {
                continue;
            }

            if (line.Kind != LineKind.Plain)
            {
                inReply = line.Kind is LineKind.ClaudeReply or LineKind.UserEcho;
                if (!line.Committed)
                {
                    line.JoinedToPrevious = false;
                }
            }
            else if (!line.Committed)
            {
                var text = line.Text;
                line.JoinedToPrevious = JoinWrappedLines && inReply && previous is not null
                    && previous.Text.Length > 0 && previous.Text.Length >= Columns - 20
                    && text.Length > 0 && (text[0] == ' ' || char.IsLower(text[0]));
                if (line.JoinedToPrevious)
                {
                    line.HeadingLevel = 0;
                }
            }

            previous = line;
        }
    }

    private void InsertBlock(PendingSend send, int at) => _lines.InsertRange(at, send.Block);

    /// <summary>
    /// A send whose block is still held back (sent while Claude worked) but whose echo never came: once Claude is
    /// idle and shows nothing queued, the block goes in at the end of the conversation, where it would have gone at once.
    /// </summary>
    private void PlaceBlocksWithoutEcho()
    {
        if (Working || QueuedMessages > 0)
        {
            return;
        }

        foreach (var send in _pending)
        {
            if (_lines.LastIndexOf(send.Block[0]) < 0)
            {
                InsertBlock(send, AnchorIndex());
            }
        }
    }

    /// <summary>
    /// Hides Claude's <c>you:</c> echo of a sent message: the app has printed the message itself (FR-4.1, D22), so
    /// the echo would show it twice. The echo block is found by text (<see cref="EchoBlock"/>), so wrapped rows and
    /// blank lines inside the message belong to it. A send whose block is still held back (sent while Claude
    /// worked) gets its block printed in front of the echo, which is where Claude took the message up (FR-4.7). An
    /// echo that repeats no sent text (a collapsed "[Pasted text]" placeholder) stays visible, so the user sees what
    /// Claude received. The queued copy inside the working block (chrome) is not the echo: it is skipped until the
    /// hint under it disappears and the row becomes content.
    /// </summary>
    private void HandleEchoes()
    {
        // The echo work is a separate method: its lambdas capture locals, and a closure declared inside this loop
        // would be allocated on every iteration over the whole transcript, echo or not.
        for (var i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            if (!line.Committed && !line.ChromeHidden && line.Kind == LineKind.UserEcho && !line.EchoHandled)
            {
                i = HandleEcho(i, line);
            }
        }

        _handled.RemoveAll(h => DateTime.UtcNow - h.Send.Created > PendingLifetime);
    }

    /// <summary>Handles the new echo at <paramref name="index"/>; returns the index of the last row it covers.</summary>
    private int HandleEcho(int index, Line line)
    {
        line.EchoHandled = true;
        List<Line>? block = null;
        // Echoes normally arrive in send order, but a message sent while Claude was busy is echoed later than one
        // sent after it was answered, so prefer the send whose text the echo repeats. A send whose earlier echo
        // rows were rewritten with other text (the echo moved) is attached to the new rows. An echo that repeats
        // nothing is charged to the oldest pending send, so that a held block still gets printed.
        var send = _pending.FirstOrDefault(p => (block = EchoBlock(index, p.Text)) is not null)
            ?? _handled.FirstOrDefault(h => !h.Echo.Text.StartsWith("you:", StringComparison.Ordinal) && (block = EchoBlock(index, h.Send.Text)) is not null)?.Send
            ?? (_pending.Count > 0 ? _pending[0] : null);
        if (send is null)
        {
            return index;
        }

        _pending.Remove(send);
        _handled.RemoveAll(h => ReferenceEquals(h.Send, send));
        _handled.Add(new HandledSend(send, line));
        if (_lines.LastIndexOf(send.Block[0]) < 0)
        {
            InsertBlock(send, _lines.LastIndexOf(line));
        }

        if (block is null)
        {
            return index;
        }

        foreach (var l in block)
        {
            l.EchoHidden = true;
        }

        return _lines.LastIndexOf(block[^1]);
    }

    /// <summary>
    /// The rows of Claude's echo of <paramref name="sentText"/> starting at the <c>you:</c> row <paramref name="start"/>,
    /// or null when the rows do not repeat the text. Rows are taken while the text so far, with all whitespace
    /// removed, is still a prefix of the sent text with its whitespace removed; blank rows inside the message and
    /// rows wrapped at the console width therefore belong to the block, and rows after the message do not.
    /// </summary>
    private List<Line>? EchoBlock(int start, string sentText)
    {
        var target = Squeeze(sentText);
        var block = new List<Line> { _lines[start] };
        var text = Squeeze(_lines[start].Text["you:".Length..]);
        if (!target.StartsWith(text, StringComparison.Ordinal))
        {
            return null;
        }

        for (var j = start + 1; text.Length < target.Length && j < _lines.Count; j++)
        {
            var candidate = _lines[j];
            if (candidate.Committed || candidate.IsMarker || candidate.ChromeHidden)
            {
                break;
            }

            var longer = text + Squeeze(candidate.Text);
            if (!target.StartsWith(longer, StringComparison.Ordinal))
            {
                break;
            }

            text = longer;
            block.Add(candidate);
        }

        return text.Length == target.Length ? block : null;
    }

    private void CheckResponseStarted()
    {
        var index = -1;
        for (var i = _lines.Count - 1; i >= 0; i--)
        {
            if (_lines[i].Kind == LineKind.OutputMarker)
            {
                index = i;
                break;
            }
        }

        if (index < 0 || ReferenceEquals(_lines[index], _responseStartedFor))
        {
            return;
        }

        for (var j = index + 1; j < _lines.Count; j++)
        {
            var line = _lines[j];
            if (line.Hidden || line.IsMarker)
            {
                continue;
            }

            if (line.Kind is LineKind.ClaudeReply or LineKind.Thinking or LineKind.Tool or LineKind.ToolError or LineKind.Error)
            {
                _responseStartedFor = _lines[index];
                ResponseStarted?.Invoke();
                return;
            }
        }
    }

    /// <summary>Drops the oldest tenth of the transcript once it exceeds <see cref="MaxLines"/>; rows still on screen stay.</summary>
    private void Trim()
    {
        if (_lines.Count <= MaxLines)
        {
            return;
        }

        var wanted = _lines.Count / 10;
        var count = 0;
        while (count < wanted && count < _lines.Count && _lines[count].Row is null)
        {
            count++;
        }

        if (count > 0)
        {
            _lines.RemoveRange(0, count);
        }
    }

    private Line? LastContentLine()
    {
        for (var i = _lines.Count - 1; i >= 0; i--)
        {
            var line = _lines[i];
            if (!line.Hidden && !line.IsMarker && line.Text.Length > 0)
            {
                return line;
            }
        }

        return null;
    }

    private int AnchorIndex()
    {
        if (_anchor is { } anchor)
        {
            var index = _lines.LastIndexOf(anchor);
            if (index >= 0)
            {
                return index;
            }
        }

        return _lines.Count;
    }

    private void OnTitle(string title)
    {
        var text = title.Trim();
        if (text.Length == 0)
        {
            return;
        }

        // Session names arrive as "<state glyph> <name>"; other titles (the executable name at startup,
        // "Claude Code" before a name exists) are not names.
        if (!"◐◑◒◓✳✶✻✽✢·".Contains(text[0]))
        {
            return;
        }

        text = text[1..].Trim();
        if (text.Length > 0 && text != "Claude Code")
        {
            SessionName = text;
        }
    }

    /// <summary>The text with every whitespace character removed: how an echo is compared with the sent text, so that wrapped rows and blank lines do not matter.</summary>
    private static string Squeeze(string text) => WhitespaceRegex().Replace(text, string.Empty);

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

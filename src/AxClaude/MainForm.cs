using System.Diagnostics;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using AxClaude.Core;
using AxClaude.Core.Pty;
using AxClaude.Core.Transcript;
using AxClaude.Core.Updates;

namespace AxClaude;

internal sealed class MainForm : Form
{
    private const int MaxHistory = 100;

    private readonly StartupOptions _options;
    private readonly AppSettings _settings;
    private readonly string? _settingsError;
    private readonly SessionModel _model;
    private readonly TranscriptView _transcript = new();
    private readonly TextBox _input = new();
    private readonly OverlayPanel _overlay = new();
    private readonly MenuStrip _menu = new();
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _state = new();
    private readonly ToolStripStatusLabel _folderLabel = new();
    private readonly ToolStripMenuItem _currentFolderItem = new();
    private readonly ToolStripMenuItem _recentFoldersItem = new("&Recent folders");
    private readonly ToolStripMenuItem _recordItem = new(RecordItemText);
    private const string RecordItemText = "&Record raw stream for a bug report...";
    private readonly ToolStripMenuItem _updateItem = new("Check for &updates...");
    private readonly System.Windows.Forms.Timer _quiet = new() { Interval = 100 };
    private readonly System.Windows.Forms.Timer _attention = new() { Interval = 400 };
    private readonly System.Windows.Forms.Timer _exitSettle = new() { Interval = 300 };
    private readonly Queue<(string Text, int DelayAfter)> _writes = new();
    private readonly List<string> _history = [];
    private readonly HashSet<int> _spoken = [];
    private PtyHost? _host;
    private StreamRecorder? _recorder;
    private int _exitCode;
    private string? _folder;
    private bool _writing;
    private bool _bellPending;
    private bool _promptAnnounced;
    private bool _readyAnnounced;
    private int _historyIndex = -1;
    private string _draft = string.Empty;
    private string _findText = string.Empty;
    private Control? _focusBeforeNotice;
    private bool _noticeOpen;
    private bool _closeConfirmed;
    private ReleaseInfo? _update;
    private string? _updateFolder;
    private bool _updateBusy;

    public MainForm(StartupOptions options, AppSettings settings, string? settingsError)
    {
        AccessibleRole = AccessibleRole.Window;
        _options = options;
        _settings = settings;
        _settingsError = settingsError;
        _folder = options.Folder ?? (settings.LastProjectFolder is { } last && Directory.Exists(last) ? last : null);
        _model = new SessionModel(options.Columns ?? settings.PtyColumns, options.Rows ?? settings.PtyRows)
        {
            MaxLines = settings.MaxTranscriptLines,
            JoinWrappedLines = settings.JoinWrappedLines,
            TimeStamps = settings.MarkerTimeStamps,
        };
        _model.Changed += OnModelChanged;
        _model.ResponseStarted += () => Announce("Claude is responding", false);
        _model.Announcement += text => Announce(text, false);
        _model.Bell += () =>
        {
            _bellPending = true;
            _attention.Stop();
            _attention.Start();
        };
        _quiet.Tick += (_, _) =>
        {
            _quiet.Stop();
            _model.EndFrame();
        };
        _attention.Tick += (_, _) =>
        {
            _attention.Stop();
            AnnounceAttention();
        };
        _exitSettle.Tick += (_, _) =>
        {
            _exitSettle.Stop();
            FinishExit();
        };
        BuildUi();
        ApplyTextFont();
        RestoreWindow();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Focus first: a notice shown by what follows (Claude not found) takes the focus from here and gives it back.
        _input.Focus();
        if (_settingsError is not null)
        {
            _model.AddSystemLine("The settings file could not be read, so the defaults are in use: " + _settingsError);
        }

        if (_options.RecordPath is { } record)
        {
            StartRecording(record);
        }

        if (_folder is null)
        {
            ChooseFolder();
        }

        if (_folder is null)
        {
            _model.AddSystemLine("No project folder. Press Ctrl+N to choose one.");
        }
        else
        {
            StartClaude();
        }

        if (_settings.CheckForUpdates)
        {
            CheckForUpdates(manual: false);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !_closeConfirmed && ClaudeBusy)
        {
            // FR-1.7: closing now would cut Claude off in the middle of a turn. Ask first, inside the window (D23).
            // A Windows shutdown or Task Manager closes without asking.
            e.Cancel = true;
            ConfirmClose();
            base.OnFormClosing(e);
            return;
        }

        SaveWindow();
        StopClaude();
        StopRecording(announce: false);
        SaveSettings();
        if (_updateFolder is { } update)
        {
            // FR-1.10: the downloaded version's installer waits for this process to end, installs and starts AxClaude again.
            Updater.LaunchInstaller(update, _folder, continueConversation: _history.Count > 0);
        }

        base.OnFormClosing(e);
    }

    /// <summary>Claude is in the middle of a turn: working, waiting for an answer, or holding a message sent while it worked.</summary>
    private bool ClaudeBusy => _host is not null && (_model.Working || _model.PromptPending || _model.QueuedMessages > 0);

    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_KEYMENU = 0xF100;

    protected override void WndProc(ref Message m)
    {
        // Alt alone and F10 open the menu bar through SC_KEYMENU; while a notice holds the keyboard they do nothing.
        // Alt+Space (the system menu, lParam is the space character) still works.
        if (m.Msg == WM_SYSCOMMAND && ((long)m.WParam & 0xFFF0) == SC_KEYMENU && ((long)m.LParam & 0xFFFF) == 0 && _noticeOpen)
        {
            return;
        }

        base.WndProc(ref m);
    }

    /// <summary>
    /// NVDA finds a status bar by looking at the bottom-left pixel of the foreground window's rectangle. It takes that
    /// rectangle from UI Automation, and for a WinForms window that is the full window rectangle including the invisible
    /// resize border, so the pixel lies outside the visible window and NVDA+End reports no status bar. Reporting the
    /// client area (with the title bar) instead puts that pixel in the status strip. Classic Win32 windows report their
    /// client area through MSAA, which is why the trick is not needed there.
    /// </summary>
    protected override AccessibleObject CreateAccessibilityInstance() => new MainFormAccessibleObject(this);

    private sealed class MainFormAccessibleObject(MainForm owner) : ControlAccessibleObject(owner)
    {
        public override Rectangle Bounds
        {
            get
            {
                if (!owner.IsHandleCreated)
                {
                    return Rectangle.Empty;
                }

                var client = owner.RectangleToScreen(owner.ClientRectangle);
                var top = Math.Min(owner.Bounds.Top, client.Top);
                return Rectangle.FromLTRB(client.Left, top, client.Right, client.Bottom);
            }
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_noticeOpen)
        {
            // A notice holds the keyboard: the window's shortcuts and the menu's wait until it is closed. Not calling
            // the base skips the menu shortcuts; Enter and Escape reach the notice's buttons through AcceptButton and
            // CancelButton, Tab and the editing keys work as usual. Only the text size keys stay live.
            switch (keyData)
            {
                case Keys.Control | Keys.Oemplus:
                case Keys.Control | Keys.Add:
                    ChangeTextSize(1);
                    return true;
                case Keys.Control | Keys.OemMinus:
                case Keys.Control | Keys.Subtract:
                    ChangeTextSize(-1);
                    return true;
            }

            return false;
        }

        switch (keyData)
        {
            case Keys.Control | Keys.Tab:
            case Keys.Control | Keys.Shift | Keys.Tab:
            case Keys.F6:
                if (_transcript.Focused)
                {
                    _input.Focus();
                }
                else
                {
                    _transcript.Focus();
                }

                return true;
            case Keys.Control | Keys.Return:
                SendInput();
                return true;
            case Keys.Control | Keys.F:
                FindDialog();
                return true;
            case Keys.F3:
                FindNext(backward: false);
                return true;
            case Keys.Shift | Keys.F3:
                FindNext(backward: true);
                return true;
            case Keys.Control | Keys.S:
                SaveConversation();
                return true;
            case Keys.Shift | Keys.Escape:
                // The interrupt key (FR-2.3). Ctrl+Escape opens the Start menu and never reaches the app.
                Write("\x1b");
                return true;
            case Keys.Shift | Keys.Tab when _input.Focused:
                // What a Claude Code user expects Shift+Tab to do: cycle the permission mode.
                Write("\x1b[Z");
                return true;
            case Keys.Control | Keys.Oemplus:
            case Keys.Control | Keys.Add:
                ChangeTextSize(1);
                return true;
            case Keys.Control | Keys.OemMinus:
            case Keys.Control | Keys.Subtract:
                ChangeTextSize(-1);
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ---- UI ----

    private void BuildUi()
    {
        Text = "AxClaude";
        Size = new Size(1000, 700);
        MinimumSize = new Size(600, 400);

        var project = new ToolStripMenuItem("&Project");
        _currentFolderItem.Click += (_, _) => CopyFolder();
        project.DropDownItems.Add(_currentFolderItem);
        project.DropDownItems.Add(new ToolStripMenuItem("&Change folder...", null, (_, _) => ChangeFolder()) { ShortcutKeys = Keys.Control | Keys.N });
        _recentFoldersItem.DropDownOpening += (_, _) => FillRecentFolders();
        _recentFoldersItem.DropDownItems.Add(new ToolStripMenuItem("(none)") { Enabled = false });
        project.DropDownItems.Add(_recentFoldersItem);
        project.DropDownItems.Add(new ToolStripMenuItem("&Restart Claude", null, (_, _) => Relaunch("Restarting Claude")) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.R });
        project.DropDownItems.Add(new ToolStripSeparator());
        project.DropDownItems.Add(new ToolStripMenuItem("&Save conversation as...", null, (_, _) => SaveConversation()) { ShortcutKeyDisplayString = "Ctrl+S" });
        project.DropDownItems.Add(new ToolStripSeparator());
        project.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (_, _) => Close()) { ShortcutKeyDisplayString = "Alt+F4" });

        var session = new ToolStripMenuItem("&Session");
        session.DropDownItems.Add(new ToolStripMenuItem("Send &message", null, (_, _) => SendInput()) { ShortcutKeyDisplayString = "Ctrl+Enter" });
        session.DropDownItems.Add(new ToolStripMenuItem("&Interrupt Claude (send Escape)", null, (_, _) => Write("\x1b")) { ShortcutKeyDisplayString = "Shift+Esc" });
        session.DropDownItems.Add(new ToolStripMenuItem("Send Ctrl+&C", null, (_, _) => Write("\x03")) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.C });
        session.DropDownItems.Add(new ToolStripMenuItem("Send Ctrl+&D", null, (_, _) => Write("\x04")));
        session.DropDownItems.Add(new ToolStripMenuItem("Send &Tab", null, (_, _) => Write("\t")));
        session.DropDownItems.Add(new ToolStripMenuItem("Send Shift+Tab (next permission &mode)", null, (_, _) => Write("\x1b[Z")) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.M });
        session.DropDownItems.Add(new ToolStripMenuItem("Send Ctrl+&O (Claude's detailed view on or off)", null, (_, _) => Write("\x0f")) { ShortcutKeys = Keys.Control | Keys.O });
        session.DropDownItems.Add(new ToolStripMenuItem("Send &Up", null, (_, _) => Write("\x1b[A")) { ShortcutKeyDisplayString = "Ctrl+Up in the message field" });
        session.DropDownItems.Add(new ToolStripMenuItem("Send Do&wn", null, (_, _) => Write("\x1b[B")) { ShortcutKeyDisplayString = "Ctrl+Down in the message field" });

        var navigate = new ToolStripMenuItem("&Navigate");
        navigate.DropDownItems.Add(new ToolStripMenuItem("Go to &message field", null, (_, _) => _input.Focus()) { ShortcutKeyDisplayString = "Ctrl+Tab" });
        navigate.DropDownItems.Add(new ToolStripMenuItem("Go to &conversation", null, (_, _) => _transcript.Focus()) { ShortcutKeyDisplayString = "Ctrl+Tab" });
        navigate.DropDownItems.Add(new ToolStripMenuItem("&Latest response", null, (_, _) => _transcript.JumpToLatestResponse()) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.O });
        navigate.DropDownItems.Add(new ToolStripSeparator());
        navigate.DropDownItems.Add(new ToolStripMenuItem("&Find...", null, (_, _) => FindDialog()) { ShortcutKeyDisplayString = "Ctrl+F" });
        navigate.DropDownItems.Add(new ToolStripMenuItem("Find &next", null, (_, _) => FindNext(backward: false)) { ShortcutKeyDisplayString = "F3" });
        navigate.DropDownItems.Add(new ToolStripMenuItem("Find &previous", null, (_, _) => FindNext(backward: true)) { ShortcutKeyDisplayString = "Shift+F3" });
        navigate.DropDownItems.Add(new ToolStripSeparator());
        navigate.DropDownItems.Add(new ToolStripMenuItem("&Bookmark this line", null, (_, _) => _transcript.ToggleBookmark()) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.K, ShortcutKeyDisplayString = "Ctrl+Shift+K, or M in the conversation" });
        navigate.DropDownItems.Add(new ToolStripMenuItem("Next bookmar&k", null, (_, _) => _transcript.JumpToBookmark(backward: false)) { ShortcutKeyDisplayString = "K in the conversation" });
        navigate.DropDownItems.Add(new ToolStripMenuItem("Pre&vious bookmark", null, (_, _) => _transcript.JumpToBookmark(backward: true)) { ShortcutKeyDisplayString = "Shift+K in the conversation" });

        var options = new ToolStripMenuItem("&Options");
        options.DropDownItems.Add(Toggle("&Announce when Claude is done", _settings.AnnounceBell, v => _settings.AnnounceBell = v));
        options.DropDownItems.Add(Toggle("Play a &sound when Claude is done", _settings.SoundOnBell, v => _settings.SoundOnBell = v));
        options.DropDownItems.Add(Toggle("&Flash the taskbar button when Claude is done", _settings.FlashTaskbar, v => _settings.FlashTaskbar = v));
        options.DropDownItems.Add(Toggle("Speak &replies as they arrive", _settings.SpeakReplies, v => _settings.SpeakReplies = v));
        options.DropDownItems.Add(Toggle("Show the &time in the Input and Output lines", _settings.MarkerTimeStamps, v =>
        {
            _settings.MarkerTimeStamps = v;
            _model.TimeStamps = v;
        }));
        options.DropDownItems.Add(Toggle("Check for &updates when AxClaude starts", _settings.CheckForUpdates, v => _settings.CheckForUpdates = v));
        options.DropDownItems.Add(new ToolStripSeparator());
        options.DropDownItems.Add(new ToolStripMenuItem("&Font...", null, (_, _) => ChooseFont()));
        options.DropDownItems.Add(new ToolStripMenuItem("&Larger text", null, (_, _) => ChangeTextSize(1)) { ShortcutKeyDisplayString = "Ctrl+Plus" });
        options.DropDownItems.Add(new ToolStripMenuItem("S&maller text", null, (_, _) => ChangeTextSize(-1)) { ShortcutKeyDisplayString = "Ctrl+Minus" });
        options.DropDownItems.Add(new ToolStripMenuItem("&Use the Windows text size", null, (_, _) => UseSystemFont()));
        options.DropDownItems.Add(new ToolStripSeparator());
        _recordItem.Click += (_, _) => ToggleRecording();
        options.DropDownItems.Add(_recordItem);
        options.DropDownItems.Add(new ToolStripMenuItem("&Open settings file", null, (_, _) => OpenSettingsFile()));

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add(new ToolStripMenuItem("&Keyboard shortcuts", null, (_, _) => ShowText("Keyboard shortcuts", HelpText.Shortcuts)) { ShortcutKeys = Keys.F1 });
        help.DropDownItems.Add(new ToolStripMenuItem("&User guide", null, (_, _) => ShowText("User guide", HelpText.UserGuide())));
        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add(new ToolStripMenuItem("Claude Code &documentation (web)", null, (_, _) => OpenUrl("https://code.claude.com/docs/en/overview")));
        help.DropDownItems.Add(new ToolStripMenuItem("Claude Code slash &commands (web)", null, (_, _) => OpenUrl("https://code.claude.com/docs/en/commands")));
        help.DropDownItems.Add(new ToolStripMenuItem("Claude Code &keys (web)", null, (_, _) => OpenUrl("https://code.claude.com/docs/en/interactive-mode")));
        help.DropDownItems.Add(new ToolStripMenuItem("Claude Code command &line (web)", null, (_, _) => OpenUrl("https://code.claude.com/docs/en/cli-reference")));
        help.DropDownItems.Add(new ToolStripMenuItem("Install or update Claude Code (&web)", null, (_, _) => OpenUrl(ClaudeLauncher.InstallUrl)));
        help.DropDownItems.Add(new ToolStripSeparator());
        _updateItem.Click += (_, _) => CheckForUpdates(manual: true);
        help.DropDownItems.Add(_updateItem);
        help.DropDownItems.Add(new ToolStripMenuItem("Copy diag&nostics", null, (_, _) => CopyDiagnostics()));
        help.DropDownItems.Add(new ToolStripMenuItem("&About", null, (_, _) =>
            ShowText("About AxClaude",
                $"AxClaude {Program.Version}\nA screen reader friendly window for Claude Code.\nMade by Dr. Kyle Keane, www.kylekeane.com. Free under the MIT licence.\n\nClaude Code documentation: https://code.claude.com/docs\nSettings: {AppSettings.DefaultPath}\nLog: {Log.FilePath}")));

        _menu.Items.AddRange([project, session, navigate, options, help]);
        MainMenuStrip = _menu;

        // No AccessibleName on status labels: it would replace the text that NVDA reads with NVDA+End.
        // No Spring either: this WinForms version places a Spring label outside the strip, so it is neither drawn
        // nor exposed to NVDA. StatusLayout keeps both labels inside the strip whatever the texts' lengths.
        _state.Text = "Claude: starting";
        _state.TextAlign = ContentAlignment.MiddleLeft;
        _folderLabel.TextAlign = ContentAlignment.MiddleLeft;
        _status.Items.AddRange([_state, _folderLabel]);
        _status.SizeChanged += (_, _) => StatusLayout.Fit(_status, _state, _folderLabel);

        _input.AccessibleName = "Message to Claude";
        _input.AccessibleDescription = "Ctrl+Enter sends, Enter starts a new line";
        _input.PlaceholderText = "Message to Claude";
        _input.Multiline = true;
        _input.AcceptsReturn = true;
        _input.AcceptsTab = false;
        _input.WordWrap = true;
        _input.ScrollBars = ScrollBars.Vertical;
        _input.Dock = DockStyle.Bottom;
        _input.TabIndex = 0;
        _input.KeyDown += OnInputKeyDown;

        _transcript.Dock = DockStyle.Fill;
        _transcript.TabIndex = 1;
        _transcript.EscapePressed += () => _input.Focus();
        _transcript.LineChosen += QuoteLineInMessage;
        _transcript.BookmarkToggled += ToggleBookmark;
        _overlay.Chosen += OnNoticeChoice;

        // The overlay is hidden until a notice shows; it then fills the space of the hidden conversation and field.
        Controls.Add(_overlay);
        Controls.Add(_transcript);
        Controls.Add(_input);
        Controls.Add(_status);
        Controls.Add(_menu);
        _transcript.BringToFront();

        UpdateFolderUi();
    }

    private ToolStripMenuItem Toggle(string text, bool initial, Action<bool> apply)
    {
        var item = new ToolStripMenuItem(text) { CheckOnClick = true, Checked = initial };
        item.CheckedChanged += (_, _) =>
        {
            apply(item.Checked);
            SaveSettings();
        };
        return item;
    }

    private void FillRecentFolders()
    {
        _recentFoldersItem.DropDownItems.Clear();
        var folders = _settings.RecentFolders.Where(Directory.Exists).ToList();
        if (folders.Count == 0)
        {
            _recentFoldersItem.DropDownItems.Add(new ToolStripMenuItem("(none)") { Enabled = false });
            return;
        }

        foreach (var folder in folders)
        {
            var target = folder;
            _recentFoldersItem.DropDownItems.Add(new ToolStripMenuItem(folder.Replace("&", "&&"), null, (_, _) => SwitchFolder(target)));
        }
    }

    // ---- input ----

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Escape when e.Modifiers == Keys.None:
                // A guard (FR-2.3, D20): Escape is a reflex key for screen reader users, and Claude takes it as an interrupt.
                e.SuppressKeyPress = true;
                Announce("Escape does nothing here. Press Shift+Escape to interrupt Claude", true);
                break;
            case Keys.Up when e.Control:
                e.SuppressKeyPress = true;
                Write("\x1b[A");
                break;
            case Keys.Down when e.Control:
                e.SuppressKeyPress = true;
                Write("\x1b[B");
                break;
            case Keys.Up when e.Modifiers == Keys.None && CaretLine() == 0:
                e.SuppressKeyPress = true;
                RecallHistory(-1);
                break;
            case Keys.Down when e.Modifiers == Keys.None && CaretLine() == _input.GetLineFromCharIndex(_input.TextLength):
                e.SuppressKeyPress = true;
                RecallHistory(1);
                break;
        }
    }

    private int CaretLine() => _input.GetLineFromCharIndex(_input.SelectionStart);

    /// <summary>Up on the first line and Down on the last line walk the messages sent in this session (FR-2.6).</summary>
    private void RecallHistory(int step)
    {
        if (_historyIndex < 0)
        {
            if (step > 0 || _history.Count == 0)
            {
                Announce(step > 0 ? "No later message" : "No earlier message", true);
                return;
            }

            _draft = _input.Text;
            _historyIndex = _history.Count;
        }

        var next = _historyIndex + step;
        if (next < 0)
        {
            Announce("No earlier message", true);
            return;
        }

        if (next >= _history.Count)
        {
            _historyIndex = -1;
            SetInputText(_draft);
            Announce(_draft.Length == 0 ? "Message field is empty" : _draft, true);
            return;
        }

        _historyIndex = next;
        SetInputText(_history[next]);
        Announce(_history[next], true);
    }

    private void SetInputText(string text)
    {
        _input.Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
        _input.Select(_input.TextLength, 0);
    }

    /// <summary>The literal line that heads a quoted conversation line in the message, so Claude can tell it from the user's words (FR-2.9).</summary>
    private const string QuoteMarker = "_ start of copied line from conversation history _";

    /// <summary>
    /// Enter on a conversation line appends the line to the message under the marker, separated from what is there
    /// by a blank line, and leaves the message caret on a fresh line after it for the comment (FR-2.9). Focus and the
    /// conversation caret stay where they are, so several lines can be collected before replying.
    /// </summary>
    private void QuoteLineInMessage(int number, int count, string text)
    {
        var existing = _input.Text.TrimEnd('\r', '\n');
        var quote = $"{QuoteMarker}\r\nLine {number} of {count}: {text}\r\n";
        _input.Text = existing.Length == 0 ? quote : existing + "\r\n\r\n" + quote;
        _input.Select(_input.TextLength, 0);
        _historyIndex = -1;
        Announce($"Line {number} copied to the message", true);
    }

    /// <summary>m, or Navigate → Bookmark this line (FR-3.9): the model adds or removes the bookmark line and the result is spoken.</summary>
    private void ToggleBookmark(Line line)
    {
        if (_model.ToggleBookmark(line) is not ({ } bookmark, var added))
        {
            Announce("The line is no longer in the conversation", true);
            return;
        }

        Announce($"{bookmark.Text} {(added ? "added" : "removed")}", true);
    }

    private void SendInput()
    {
        if (_host is null)
        {
            // The text stays in the field: nothing was sent, and it is wanted again after a restart.
            Announce("Claude is not running. Press Ctrl+Shift+R to start it again", true);
            return;
        }

        var text = _input.Text.Replace("\r\n", "\n").Trim();
        _input.Clear();
        _historyIndex = -1;
        _draft = string.Empty;
        if (text.Length == 0)
        {
            Write("\r");
            return;
        }

        var answer = _model.PromptPending;
        var queued = _model.Working;
        _model.Send(text);
        if (_history.Count == 0 || _history[^1] != text)
        {
            _history.Add(text);
            if (_history.Count > MaxHistory)
            {
                _history.RemoveAt(0);
            }
        }

        // Text and Enter are separate writes: text plus CR in one chunk is treated as a paste (SPEC.md 4.4).
        // Lines of a multi-line message are joined with a lone newline write, Claude's Ctrl+J.
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            _writes.Enqueue((lines[i], i == lines.Length - 1 ? 100 : 30));
            if (i < lines.Length - 1)
            {
                _writes.Enqueue(("\n", 30));
            }
        }

        _writes.Enqueue(("\r", 50));
        if (!_writing)
        {
            PumpWrites();
        }

        Announce(answer ? "Answer sent" : queued ? "Message waiting" : "Message sent", false);
    }

    private void PumpWrites()
    {
        if (_writes.Count == 0)
        {
            _writing = false;
            return;
        }

        _writing = true;
        var (text, delay) = _writes.Dequeue();
        Write(text);
        Later(delay, PumpWrites);
    }

    private static void Later(int milliseconds, Action action)
    {
        var timer = new System.Windows.Forms.Timer { Interval = milliseconds };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            action();
        };
        timer.Start();
    }

    private void Write(string text) => _host?.Write(text);

    // ---- model events ----

    private void OnModelChanged()
    {
        _transcript.Sync(_model.Lines);
        UpdateStatus();

        if (_model.PromptPending)
        {
            if (!_promptAnnounced)
            {
                _attention.Stop();
                _attention.Start();
            }
        }
        else
        {
            _promptAnnounced = false;
        }

        if (!_readyAnnounced && _host is not null && _model.Mode is not null && !_model.Working && !_model.PromptPending)
        {
            _readyAnnounced = true;
            Announce("Claude is ready", false);
        }

        if (_settings.SpeakReplies)
        {
            SpeakNewReplyLines();
        }
    }

    /// <summary>Speaks reply lines once they are final: committed, followed by another line, or Claude is idle (FR-7.4).</summary>
    private void SpeakNewReplyLines()
    {
        var lines = _model.Lines;
        var marker = -1;
        var lastVisible = -1;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (lastVisible < 0 && !lines[i].Hidden)
            {
                lastVisible = i;
            }

            if (lines[i].Kind == LineKind.OutputMarker)
            {
                marker = i;
                break;
            }
        }

        if (marker < 0)
        {
            return;
        }

        var inReply = false;
        for (var i = marker + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Hidden || line.IsMarker)
            {
                continue;
            }

            if (line.Kind == LineKind.ClaudeReply)
            {
                inReply = true;
            }
            else if (line.Kind != LineKind.Plain)
            {
                inReply = false;
            }

            if (!inReply || line.Text.Length == 0)
            {
                continue;
            }

            var final = line.Committed || i < lastVisible || !_model.Working;
            if (final && _spoken.Add(line.Id))
            {
                Announce(line.Text, false);
            }
        }
    }

    private void AnnounceAttention()
    {
        if (_model.PromptPending)
        {
            if (!_promptAnnounced)
            {
                _promptAnnounced = true;
                Announce("Claude needs your answer", false);
                Signal();
            }
        }
        else if (_bellPending)
        {
            if (_settings.AnnounceBell)
            {
                Announce("Claude is done", false);
            }

            Signal();
        }

        _bellPending = false;
    }

    /// <summary>The sound and the taskbar flash that go with an attention announcement.</summary>
    private void Signal()
    {
        if (_settings.SoundOnBell)
        {
            SystemSounds.Asterisk.Play();
        }

        if (_settings.FlashTaskbar && !ContainsFocus && IsHandleCreated)
        {
            var info = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                hwnd = Handle,
                dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
                uCount = 0,
                dwTimeout = 0,
            };
            FlashWindowEx(ref info);
        }
    }

    private void UpdateStatus()
    {
        var state = _host is null ? "stopped"
            : _model.TranscriptViewOpen ? "detailed view on, Ctrl+O turns it off"
            : _model.PromptPending ? "waiting for your answer"
            : _model.Working ? ("working " + (_model.Spinner ?? string.Empty)).Trim()
            : _model.Mode is not null ? "ready"
            : "starting";
        var parts = new List<string> { "Claude: " + state };
        if (_model.QueuedMessages > 0)
        {
            parts.Add(_model.QueuedMessages == 1 ? "1 message waiting" : $"{_model.QueuedMessages} messages waiting");
        }

        if (_model.Mode is { } mode)
        {
            parts.Add(mode);
        }

        if (_model.SessionName is { } name)
        {
            parts.Add(name);
        }

        var text = string.Join(" | ", parts);
        if (_state.Text != text)
        {
            _state.Text = text;
            StatusLayout.Fit(_status, _state, _folderLabel);
        }
    }

    private void UpdateFolderUi()
    {
        Text = _folder is null ? "AxClaude" : $"{FolderName(_folder)} - AxClaude";
        _folderLabel.Text = _folder ?? "No project folder";
        StatusLayout.Fit(_status, _state, _folderLabel);
        _currentFolderItem.Text = "Current folder: " + (_folder ?? "(none)").Replace("&", "&&");
    }

    private static string FolderName(string folder)
    {
        var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return name.Length > 0 ? name : folder;
    }

    /// <summary>Speaks without moving the focus; from the notice while one shows, since the conversation is hidden then.</summary>
    private bool Announce(string text, bool interrupt) =>
        _noticeOpen ? _overlay.Announce(text, interrupt) : _transcript.Announce(text, interrupt);

    // ---- find and save ----

    /// <summary>Ctrl+F: the Find notice (FR-3.7). Find next closes it and searches from the caret; an empty field keeps it open.</summary>
    private void FindDialog()
    {
        ShowNotice("Find", string.Empty,
        [
            new OverlayChoice("Find &next", () =>
            {
                _findText = _overlay.InputText;
                _transcript.Find(_findText, backward: false);
            }, IsDefault: true),
            new OverlayChoice("Cancel", IsCancel: true),
        ],
        new OverlayInput("&Find:", "Text to find", _findText, "Type the text to find"));
    }

    private void FindNext(bool backward)
    {
        if (_findText.Length == 0)
        {
            FindDialog();
            return;
        }

        _transcript.Find(_findText, backward);
    }

    /// <summary>Writes the conversation as the reader sees it (hidden rows left out) to a UTF-8 text file (FR-3.8).</summary>
    private void SaveConversation()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Save the conversation as text",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"{(_folder is null ? "conversation" : FolderName(_folder))}-{DateTime.Now:yyyyMMdd-HHmm}.txt",
            AddExtension = true,
            DefaultExt = "txt",
            InitialDirectory = _folder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, _transcript.Text, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fail("The conversation could not be saved: " + ex.Message);
            return;
        }

        Log.Info("Conversation saved to " + dialog.FileName);
        Announce("Conversation saved", true);
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            Fail("The page could not be opened: " + ex.Message);
        }
    }

    // ---- fonts and window ----

    private static Font SystemTextFont() => SystemFonts.MessageBoxFont ?? DefaultFont;

    private Font TextFont()
    {
        var system = SystemTextFont();
        if (_settings.FontFamily is null && _settings.FontSize <= 0 && !_settings.FontBold)
        {
            return system;
        }

        var size = _settings.FontSize > 0 ? _settings.FontSize : system.SizeInPoints;
        var style = _settings.FontBold ? FontStyle.Bold : FontStyle.Regular;
        try
        {
            return new Font(_settings.FontFamily ?? system.FontFamily.Name, size, style);
        }
        catch (ArgumentException)
        {
            return new Font(system.FontFamily, size, style);
        }
    }

    private void ApplyTextFont()
    {
        var font = TextFont();
        _transcript.Font = font;
        _input.Font = font;
        _overlay.SetTextFont(font);
        _input.Height = TextRenderer.MeasureText("Wg", font).Height * 3 + 8;
    }

    private void ChangeTextSize(int delta)
    {
        var size = Math.Clamp((int)Math.Round(TextFont().SizeInPoints) + delta, 6, 72);
        _settings.FontSize = size;
        ApplyTextFont();
        SaveSettings();
        Announce($"Text size {size}", true);
    }

    private void ChooseFont()
    {
        using var dialog = new FontDialog
        {
            Font = TextFont(),
            ShowEffects = false,
            FontMustExist = true,
            MinSize = 6,
            MaxSize = 72,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _settings.FontFamily = dialog.Font.FontFamily.Name;
        _settings.FontSize = dialog.Font.SizeInPoints;
        _settings.FontBold = dialog.Font.Bold;
        ApplyTextFont();
        SaveSettings();
        Announce($"Font {dialog.Font.FontFamily.Name} {Math.Round(dialog.Font.SizeInPoints)} point", true);
    }

    private void UseSystemFont()
    {
        _settings.FontFamily = null;
        _settings.FontSize = 0;
        _settings.FontBold = false;
        ApplyTextFont();
        SaveSettings();
        Announce("Windows text size", true);
    }

    private void RestoreWindow()
    {
        if (_settings.Window is not { } placement)
        {
            return;
        }

        var bounds = new Rectangle(placement.X, placement.Y, placement.Width, placement.Height);
        if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(bounds)))
        {
            return;
        }

        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        if (placement.Maximized)
        {
            WindowState = FormWindowState.Maximized;
        }
    }

    private void SaveWindow()
    {
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.Window = new WindowPlacement
        {
            X = bounds.X,
            Y = bounds.Y,
            Width = bounds.Width,
            Height = bounds.Height,
            Maximized = WindowState == FormWindowState.Maximized,
        };
    }

    private void SaveSettings()
    {
        try
        {
            _settings.Save(AppSettings.DefaultPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Settings could not be saved", ex);
        }
    }

    private void OpenSettingsFile()
    {
        SaveSettings();
        try
        {
            Process.Start(new ProcessStartInfo(AppSettings.DefaultPath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            Fail("The settings file could not be opened: " + ex.Message);
        }
    }

    // ---- diagnostics ----

    private void ToggleRecording()
    {
        if (_recorder is not null)
        {
            StopRecording(announce: true);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Record the raw console stream",
            Filter = "Console recording (*.vt)|*.vt|All files (*.*)|*.*",
            FileName = $"axclaude-{DateTime.Now:yyyyMMdd-HHmm}.vt",
            AddExtension = true,
            DefaultExt = "vt",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            StartRecording(dialog.FileName);
        }
    }

    private void StartRecording(string path)
    {
        try
        {
            _recorder = new StreamRecorder(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fail("The recording could not be started: " + ex.Message);
            return;
        }

        _recordItem.Text = "Stop &recording";
        Log.Info("Recording raw stream to " + path);
        _model.AddSystemLine("Recording to " + path);
        Announce("Recording started", false);
    }

    private void StopRecording(bool announce)
    {
        var recorder = _recorder;
        if (recorder is null)
        {
            return;
        }

        _recorder = null;
        recorder.Dispose();
        _recordItem.Text = RecordItemText;
        Log.Info($"Recording stopped: {recorder.Length} bytes in {recorder.Path}");
        if (announce)
        {
            _model.AddSystemLine($"Recording stopped: {recorder.Length} bytes in {recorder.Path}");
            Announce("Recording stopped", false);
        }
    }

    /// <summary>The recording file could not be written (reported from the reader thread): stop recording and say so.</summary>
    private void RecordingFailed(string reason)
    {
        if (_recorder is null)
        {
            return;
        }

        StopRecording(announce: false);
        Log.Error("Recording failed: " + reason);
        _model.AddSystemLine("Recording stopped, the file could not be written: " + reason);
        Announce("Recording stopped", false);
    }

    private void CopyDiagnostics()
    {
        var claude = ClaudeLauncher.Find(_options.ClaudePath ?? _settings.ClaudePath);
        var text = string.Join("\r\n",
        [
            $"AxClaude {Program.Version} on .NET {Environment.Version}, {Environment.OSVersion}",
            $"Claude: {claude ?? "(not found)"}",
            $"Project folder: {_folder ?? "(none)"}",
            $"Console size: {_model.Columns}x{_model.Rows}",
            $"Settings: {AppSettings.DefaultPath}",
            $"Log: {Log.FilePath}",
            $"Claude state: {_state.Text}",
            string.Empty,
            Log.Tail(200),
        ]);
        if (CopyToClipboard(text))
        {
            Announce("Diagnostics copied", true);
        }
    }

    /// <summary>Another program can hold the clipboard for a moment; that is announced rather than reported as a crash.</summary>
    private bool CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (ExternalException ex)
        {
            Log.Error("The clipboard could not be written", ex);
            Announce("Could not copy. Try again", true);
            return false;
        }
    }

    // ---- project folder ----

    private void CopyFolder()
    {
        if (_folder is null)
        {
            Announce("No project folder", true);
            return;
        }

        if (CopyToClipboard(_folder))
        {
            Announce("Copied", true);
        }
    }

    private bool ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the project folder for Claude",
            UseDescriptionForTitle = true,
            InitialDirectory = _folder ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ShowNewFolderButton = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(dialog.SelectedPath))
        {
            return false;
        }

        _folder = dialog.SelectedPath;
        UpdateFolderUi();
        return true;
    }

    private void ChangeFolder()
    {
        if (!ChooseFolder())
        {
            return;
        }

        Announce($"Project folder changed to {FolderName(_folder!)}", false);
        Relaunch($"Project folder changed to {_folder}");
    }

    private void SwitchFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            Fail("The folder no longer exists: " + folder);
            return;
        }

        _folder = folder;
        UpdateFolderUi();
        Announce($"Project folder changed to {FolderName(folder)}", false);
        Relaunch($"Project folder changed to {folder}");
    }

    // ---- Claude process ----

    private void StartClaude()
    {
        if (!Directory.Exists(_folder))
        {
            Fail($"The project folder no longer exists: {_folder}. Press Ctrl+N to choose another.");
            return;
        }

        var configured = _options.ClaudePath ?? _settings.ClaudePath;
        var claude = ClaudeLauncher.Find(configured);
        if (claude is null)
        {
            // FR-1.9: say where the app looked, offer the install command, or let the user point at claude.exe.
            Log.Error("Claude Code was not found");
            _model.AddSystemLine("Claude Code was not found. Install it, then press Ctrl+Shift+R.");
            UpdateStatus();
            ShowClaudeNotFound(ClaudeLauncher.Candidates(configured));
            return;
        }

        var arguments = new List<string>();
        if (!_options.NoAx)
        {
            arguments.Add("--ax-screen-reader");
        }

        arguments.AddRange(_options.ClaudeArgs);

        PtyHost host;
        try
        {
            host = PtyHost.Start(new PtyOptions(
                ClaudeLauncher.BuildCommandLine(claude, arguments),
                _folder!,
                _model.Columns,
                _model.Rows,
                ClaudeLauncher.BuildEnvironment()));
        }
        catch (Exception ex)
        {
            Log.Error("Claude could not be started", ex);
            var reason = ex is EntryPointNotFoundException
                ? "this version of Windows has no pseudo console. Windows 10 version 1809 or later is needed"
                : ex.Message;
            Fail("Claude could not be started: " + reason);
            return;
        }

        Log.Info($"Started {claude} (pid {host.ProcessId}) in {_folder}, console {_model.Columns}x{_model.Rows}");
        _host = host;
        _readyAnnounced = false;
        _promptAnnounced = false;
        _settings.RememberFolder(_folder!);
        SaveSettings();
        // Chunks that arrive while the UI thread is busy are handed over together: one post per burst, not one per
        // chunk, so a large tool output does not queue up hundreds of window messages ahead of the user's keys.
        var inbox = new List<byte[]>();
        var posted = false;
        host.DataReceived += data =>
        {
            try
            {
                _recorder?.Write(data);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A full disk or a locked file must not end the read loop, which would freeze the transcript.
                Post(() => RecordingFailed(ex.Message));
            }

            lock (inbox)
            {
                inbox.Add(data);
                if (posted)
                {
                    return;
                }

                posted = true;
            }

            if (!Post(FeedInbox))
            {
                lock (inbox)
                {
                    posted = false;
                }
            }
        };

        void FeedInbox()
        {
            byte[][] batch;
            lock (inbox)
            {
                batch = [.. inbox];
                inbox.Clear();
                posted = false;
            }

            if (!ReferenceEquals(host, _host))
            {
                return;
            }

            foreach (var chunk in batch)
            {
                _model.Feed(chunk);
            }

            _quiet.Stop();
            _quiet.Start();
        }

        host.Exited += code => Post(() =>
        {
            if (ReferenceEquals(host, _host))
            {
                // Let output that is still in the pipe arrive before the exit is reported.
                _exitCode = code;
                _exitSettle.Stop();
                _exitSettle.Start();
            }
        });
        UpdateStatus();
    }

    private void FinishExit()
    {
        _quiet.Stop();
        _model.EndFrame();
        StopClaude();
        Log.Info($"Claude exited with code {_exitCode}");
        _model.AddSystemLine($"Claude stopped (exit code {_exitCode}). Press Ctrl+Shift+R to start it again.");
        Announce("Claude stopped", false);
    }

    private void StopClaude()
    {
        var host = _host;
        _host = null;
        _quiet.Stop();
        _attention.Stop();
        _exitSettle.Stop();
        _writes.Clear();
        _writing = false;
        host?.Dispose();
        UpdateStatus();
    }

    private void Relaunch(string systemLine)
    {
        StopClaude();
        _model.ResetScreen();
        _model.AddSystemLine(systemLine);
        if (_folder is not null)
        {
            StartClaude();
        }
    }

    private void Fail(string message)
    {
        Log.Error(message);
        _model.AddSystemLine(message);
        UpdateStatus();
        ShowText("Error", message);
    }

    /// <summary>The crash handler's report (FR-13.4), shown inside the window like every other notice.</summary>
    public void ShowError(string title, string message) => ShowText(title, message);

    // ---- notices: the app's own dialogs, drawn inside the window (D23) ----

    /// <summary>
    /// Shows a notice in place of the conversation and the message field. The focus moves into the notice (the
    /// field, or the top of the text), the menu and the window's shortcuts are blocked, and Enter and Escape go to
    /// the default and cancel buttons. A notice shown while another is open replaces it.
    /// </summary>
    private void ShowNotice(string title, string text, IReadOnlyList<OverlayChoice> choices, OverlayInput? input = null)
    {
        if (!_noticeOpen)
        {
            _focusBeforeNotice = ReferenceEquals(ActiveControl, _transcript) ? _transcript : _input;
            // A reader keeps their line while the notice hides the conversation (FR-3.3).
            _transcript.KeepCaret = ReferenceEquals(_focusBeforeNotice, _transcript);
        }

        _noticeOpen = true;
        _overlay.Populate(title, text, choices, input);
        _overlay.Visible = true;
        _overlay.BringToFront();
        AcceptButton = _overlay.DefaultButton;
        CancelButton = _overlay.CancelButton;
        _overlay.FocusStart();
        _transcript.Visible = false;
        _input.Visible = false;
        _menu.Enabled = false;
        PerformLayout();
    }

    /// <summary>A read-only text with a Close button: help texts, About and errors.</summary>
    private void ShowText(string title, string text) => ShowNotice(title, text, [OverlayChoice.Close]);

    private void OnNoticeChoice(OverlayChoice choice)
    {
        if (choice.StaysOpen)
        {
            choice.Action?.Invoke();
            return;
        }

        DismissNotice(choice.Action);
    }

    /// <summary>
    /// Brings the conversation and the message field back, runs the action, and returns the focus to the control
    /// that had it before the notice unless the action moved it itself (Find lands in the conversation).
    /// </summary>
    private void DismissNotice(Action? then = null)
    {
        if (!_noticeOpen)
        {
            return;
        }

        _noticeOpen = false;
        _transcript.KeepCaret = false;
        _transcript.Visible = true;
        _input.Visible = true;
        _menu.Enabled = true;
        AcceptButton = null;
        CancelButton = null;
        PerformLayout();
        then?.Invoke();
        if (IsDisposed)
        {
            // The action closed the window.
            return;
        }

        if (ActiveControl is null || _overlay.Contains(ActiveControl))
        {
            (_focusBeforeNotice ?? _input).Select();
        }

        _overlay.Visible = false;
    }

    /// <summary>FR-1.7: a close while Claude is in the middle of a turn is confirmed first. Enter closes, Escape keeps the window.</summary>
    private void ConfirmClose()
    {
        var state = _model.PromptPending ? "Claude is waiting for your answer" : "Claude is still working";
        if (_model.QueuedMessages > 0)
        {
            state += _model.QueuedMessages == 1
                ? " and a message you sent is still waiting for it"
                : $" and {_model.QueuedMessages} messages you sent are still waiting for it";
        }

        var consequence = _updateFolder is null
            ? "If you close now, Claude stops in the middle of its work."
            : "If you close now, Claude stops in the middle of its work and the update is installed.";
        ShowNotice("Close AxClaude?",
            $"{state}. {consequence}\n" +
            "What is done so far is saved. Start AxClaude with -- --continue to carry on later.\n" +
            "Enter closes anyway. Escape keeps AxClaude open.",
        [
            new OverlayChoice("&Close anyway", () =>
            {
                _closeConfirmed = true;
                Close();
            }, IsDefault: true),
            new OverlayChoice("&Keep working", CancelUpdate, IsCancel: true),
        ]);
    }

    /// <summary>
    /// FR-1.10: asks GitHub for the latest release. A newer one is announced, noted in a system line and offered under
    /// Help; a manual check (Help → Check for updates) reports the result either way, including a failure.
    /// </summary>
    private async void CheckForUpdates(bool manual)
    {
        if (manual && _update is { } known)
        {
            ShowUpdateNotice(known);
            return;
        }

        if (_updateBusy)
        {
            return;
        }

        _updateBusy = true;
        _updateItem.Enabled = false;
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var release = await Updater.CheckAsync(cancellation.Token);
            if (IsDisposed)
            {
                return;
            }

            if (release is not null && UpdateCheck.IsNewer(release, Program.Version))
            {
                var version = release.Version.ToString(3);
                _update = release;
                _updateItem.Text = $"&Update to AxClaude {version}...";
                Log.Info($"Update available: {release.Tag}, zip {release.ZipUrl ?? "missing"}");
                if (manual)
                {
                    ShowUpdateNotice(release);
                }
                else
                {
                    _model.AddSystemLine($"AxClaude {version} is available. Help menu, Update to AxClaude {version}.");
                    Announce($"AxClaude {version} is available. See the Help menu", false);
                }
            }
            else if (manual)
            {
                ShowText("Check for updates",
                    $"You have the newest version, AxClaude {Program.Version}.\n" +
                    (release is null ? "No release is published yet.\n" : string.Empty) +
                    $"Releases: {UpdateCheck.ReleasesPage}");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            Log.Error("The update check failed", ex);
            if (manual && !IsDisposed)
            {
                ShowNotice("Check for updates",
                    $"GitHub could not be reached: {ex.Message}\nThe releases page: {UpdateCheck.ReleasesPage}",
                [
                    new OverlayChoice("&Open releases page", () => OpenUrl(UpdateCheck.ReleasesPage), StaysOpen: true),
                    OverlayChoice.Close,
                ]);
            }
        }
        finally
        {
            _updateBusy = false;
            if (!IsDisposed)
            {
                _updateItem.Enabled = true;
            }
        }
    }

    /// <summary>The release notes with Update now, Open release page and Later (FR-1.10).</summary>
    private void ShowUpdateNotice(ReleaseInfo release)
    {
        var version = release.Version.ToString(3);
        var size = release.ZipSize > 0 ? $" The download is {release.ZipSize / (1024.0 * 1024.0):0} MB." : string.Empty;
        var text =
            $"You have AxClaude {Program.Version}. AxClaude {version} is available.\n\n" +
            (release.Notes.Length > 0 ? release.Notes + "\n\n" : string.Empty) +
            "Update now downloads the new version, closes AxClaude, installs it and starts it again on the same folder. " +
            $"The conversation is picked up again if one is open.{size}\n" +
            "Later keeps this version; the Help menu offers the update again.";
        ShowNotice($"Update to AxClaude {version}", text,
        [
            new OverlayChoice("&Update now", () => InstallUpdate(release), IsDefault: true),
            new OverlayChoice("&Open release page", () => OpenUrl(release.PageUrl), StaysOpen: true),
            new OverlayChoice("&Later", IsCancel: true),
        ]);
    }

    /// <summary>
    /// Update now: downloads and extracts the release, then closes the window. The close question of FR-1.7 still
    /// applies while Claude works; on the way out, OnFormClosing starts the new version's installer.
    /// </summary>
    private async void InstallUpdate(ReleaseInfo release)
    {
        if (_updateBusy)
        {
            return;
        }

        _updateBusy = true;
        _updateItem.Enabled = false;
        var version = release.Version.ToString(3);
        Announce($"Downloading AxClaude {version}", true);
        _model.AddSystemLine($"Downloading AxClaude {version}...");
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var folder = await Updater.DownloadAsync(release, cancellation.Token);
            if (IsDisposed)
            {
                return;
            }

            _updateFolder = folder;
            Log.Info($"Update {release.Tag} downloaded to {folder}");
            _model.AddSystemLine($"AxClaude {version} downloaded. AxClaude closes now, installs it and starts again.");
            Announce($"AxClaude {version} downloaded. AxClaude closes now and starts again when the update is installed", true);
            Close();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Log.Error("The update download failed", ex);
            if (!IsDisposed)
            {
                _model.AddSystemLine($"The download of AxClaude {version} failed: {ex.Message}");
                ShowNotice($"Update to AxClaude {version}",
                    $"The download failed: {ex.Message}\nYou can download the zip from the release page and run install.ps1 yourself.",
                [
                    new OverlayChoice("&Open release page", () => OpenUrl(release.PageUrl), StaysOpen: true),
                    OverlayChoice.Close,
                ]);
            }
        }
        finally
        {
            _updateBusy = false;
            if (!IsDisposed)
            {
                _updateItem.Enabled = true;
            }
        }
    }

    /// <summary>Keep working after Update now: the downloaded update is not installed now; the Help menu offers it again and the download is kept.</summary>
    private void CancelUpdate()
    {
        if (_updateFolder is null)
        {
            return;
        }

        _updateFolder = null;
        Announce("The update was not installed. Help menu, Update AxClaude, when you are ready", true);
    }

    /// <summary>FR-1.9: where the app looked, the install command with a button that copies it, the install page, and Locate claude.exe.</summary>
    private void ShowClaudeNotFound(IReadOnlyList<string> candidates)
    {
        ShowNotice("Claude Code was not found", HelpText.ClaudeNotFound(candidates),
        [
            new OverlayChoice("Copy install &command", () =>
            {
                if (CopyToClipboard(ClaudeLauncher.InstallCommand))
                {
                    Announce("Copied. Paste it into PowerShell and press Enter", true);
                }
            }, StaysOpen: true),
            new OverlayChoice("&Open install instructions", () => OpenUrl(ClaudeLauncher.InstallUrl), StaysOpen: true),
            new OverlayChoice("&Locate claude.exe...", LocateClaude, StaysOpen: true),
            OverlayChoice.Close,
        ]);
    }

    /// <summary>The standard file picker; a choice closes the notice, is saved as claudePath and started at once. Cancel keeps the notice.</summary>
    private void LocateClaude()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Locate claude.exe",
            Filter = "Claude Code (claude.exe;claude.cmd)|claude.exe;claude.cmd|Programs (*.exe;*.cmd)|*.exe;*.cmd|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        DismissNotice();
        _settings.ClaudePath = dialog.FileName;
        SaveSettings();
        StartClaude();
    }

    /// <summary>Runs the action on the UI thread; false when the window is gone.</summary>
    private bool Post(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return false;
        }

        try
        {
            BeginInvoke(action);
            return true;
        }
        catch (InvalidOperationException)
        {
            // The window is closing.
            return false;
        }
    }

    // ---- taskbar flash ----

    private const uint FLASHW_ALL = 3;
    private const uint FLASHW_TIMERNOFG = 12;

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);
}

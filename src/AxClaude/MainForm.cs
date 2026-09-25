using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AxClaude.Core;
using AxClaude.Core.Pty;
using AxClaude.Core.Transcript;
using AxClaude.Core.Updates;

namespace AxClaude;

internal sealed class MainForm : Form
{
    private const string RecordItemText = "&Record raw stream for a bug report...";

    /// <summary>Spoken for Ctrl+O, which the app does not pass on (D14).</summary>
    private const string CtrlOGuard = "Ctrl+O is off here: Claude's detailed view redraws the conversation in screen reader mode. For tool output, start the session with --verbose.";

    private readonly StartupOptions _options;
    private readonly AppSettings _settings;
    private readonly string? _settingsError;
    private readonly SessionModel _model;
    private readonly TranscriptView _transcript = new();
    private readonly TextBox _input = new();
    private readonly ControlArea _area = new();
    private readonly OverlayPanel _overlay = new();
    private readonly MenuStrip _menu = new();
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _state = new();
    private readonly ToolStripStatusLabel _folderLabel = new();
    private readonly ToolStripMenuItem _currentFolderItem = new() { ShortcutKeys = Keys.Control | Keys.W };
    private readonly ToolStripMenuItem _recentFoldersItem = new("&Recent folders");
    private readonly ToolStripMenuItem _recordItem = new(RecordItemText);
    private readonly ToolStripMenuItem _installedItem = new($"&Installed: AxClaude {Program.Version}");
    private readonly ToolStripMenuItem _latestItem = new();
    private readonly ToolStripMenuItem _updateItem = new("Check for &updates...");
    private readonly System.Windows.Forms.Timer _quiet = new() { Interval = 100 };
    private readonly System.Windows.Forms.Timer _attention = new() { Interval = 400 };
    private readonly System.Windows.Forms.Timer _exitSettle = new() { Interval = 300 };
    private readonly Queue<(string Text, int DelayAfter)> _writes = new();
    /// <summary>The per-frame pass behind the tick and the spoken replies (FR-7.8, FR-7.4).</summary>
    private readonly Arrivals _arrivals = new();
    private readonly List<(ReplySpeechMode Mode, ToolStripMenuItem Item)> _replySpeechItems = [];

    /// <summary>The Speak replies choices in menu order (FR-7.4); the text without its mnemonic is what the choice announces.</summary>
    private static readonly (ReplySpeechMode Mode, string Text)[] ReplySpeechChoices =
    [
        (ReplySpeechMode.None, "&None"),
        (ReplySpeechMode.FirstLines, "&First lines only"),
        (ReplySpeechMode.All, "&All lines"),
    ];
    private PtyHost? _host;
    private StreamRecorder? _recorder;
    private int _exitCode;
    private string? _folder;
    private bool _writing;
    private bool _bellPending;
    private bool _promptAnnounced;

    /// <summary>The signature of the question or screen the control area shows (D33); null while the message field is in its place.</summary>
    private string? _shownWait;

    /// <summary>The signature of the question or screen that was shown in the control area or announced: it is not shown again while Claude waits on it.</summary>
    private string? _handledWait;

    /// <summary>When a key was last sent from a screen of Claude's: the screen that follows is the user's doing and comes without the chime.</summary>
    private long _screenKeyAt;

    /// <summary>When an answer was last sent: the question that follows is the user's doing and opens without the chime.</summary>
    private long _answerAt;

    /// <summary>The /config setting last chosen, where the list of settings starts when Claude's list comes back.</summary>
    private string? _settingKey;

    /// <summary>The question the control area showed last, which a follow-up for the user's own words repeats.</summary>
    private Question? _askedQuestion;

    /// <summary>The slash command sent last, which the question that follows belongs to (QuestionRouter); null after a message.</summary>
    private SlashCommand? _lastCommand;

    /// <summary>A message kept out of a waiting question by the guard (FR-2.10, D33), sent after all when Enter is pressed again with the same text.</summary>
    private string? _keptMessage;
    private bool _readyAnnounced;
    private bool _messageSent;
    /// <summary>Claude's arguments after --ax-screen-reader, from the command line or the New session notice (FR-8.6); null is the default, --continue (FR-9.1). Restart keeps them.</summary>
    private List<string>? _claudeArgs;
    /// <summary>Claude found no conversation to continue in this folder: the default is no arguments until the folder changes (FR-9.1).</summary>
    private bool _nothingToContinue;
    /// <summary>The folder Claude was last started in: a restart there with --continue replays what the window shows (FR-4.8).</summary>
    private string? _launchedFolder;
    private string _findText = string.Empty;
    private Control? _focusBeforeNotice;
    private bool _noticeOpen;
    private bool _closeConfirmed;
    private ReleaseInfo? _update;
    private ReleaseInfo? _latest;
    private string _latestState = "not checked yet";
    private string? _updateFolder;
    private bool _updateBusy;

    public MainForm(StartupOptions options, AppSettings settings, string? settingsError)
    {
        AccessibleRole = AccessibleRole.Window;
        _options = options;
        _claudeArgs = options.ClaudeArgs is { } given ? [.. given] : null;
        _settings = settings;
        _settingsError = settingsError;
        _folder = options.Folder ?? (settings.LastProjectFolder is { } last && Directory.Exists(last) ? last : null);
        _model = new SessionModel(options.Columns ?? settings.PtyColumns, options.Rows ?? settings.PtyRows)
        {
            MaxLines = settings.MaxTranscriptLines,
            JoinWrappedLines = settings.JoinWrappedLines,
            TimeStamps = settings.MarkerTimeStamps,
            DetectScreens = settings.QuestionNotices,
        };
        _model.Changed += OnModelChanged;
        _model.ResponseStarted += () =>
        {
            Announce("Claude is responding", false);
            if (_settings.SoundOnBell)
            {
                Sounds.Responding();
            }
        };
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

        if (_folder is null && PickFolder(null) is { } chosen)
        {
            SetFolder(chosen);
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
        if (e.CloseReason == CloseReason.UserClosing && !_closeConfirmed && (ClaudeBusy || ClaudeBackground is not null))
        {
            // FR-1.7: closing now would cut Claude off in the middle of a turn, or stop the work it runs in the
            // background after the turn. Ask first, inside the window (D23). A Windows shutdown or Task Manager
            // closes without asking.
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
            Updater.LaunchInstaller(update, _folder, continueConversation: _messageSent);
        }

        base.OnFormClosing(e);
    }

    /// <summary>Claude is in the middle of a turn: working, waiting for an answer, or holding a message sent while it worked.</summary>
    private bool ClaudeBusy => _host is not null && (_model.Working || _model.PromptPending || _model.QueuedMessages > 0);

    /// <summary>The work Claude runs in the background, which outlives the turn ("1 shell"); null when none runs or Claude is stopped.</summary>
    private string? ClaudeBackground => _host is null ? null : _model.Background;

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
                    BottomControl.Focus();
                }
                else
                {
                    _transcript.Focus();
                }

                return true;
            case Keys.Control | Keys.D1:
                GoTo(BottomControl);
                return true;
            case Keys.Control | Keys.D2:
                GoTo(_transcript);
                return true;
            case Keys.Control | Keys.O:
                // D14: Claude's detailed view redraws the conversation in screen reader mode, which doubles the
                // transcript and speaks old replies again. The key is sent only to close the view should it be open.
                if (_model.TranscriptViewOpen)
                {
                    Write("\x0f");
                }
                else
                {
                    Announce(CtrlOGuard, true);
                }

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
                Interrupt();
                return true;
            case Keys.Shift | Keys.Tab when BottomControl.Focused:
                // What a Claude Code user expects Shift+Tab to do: cycle the permission mode. The same in the control
                // area, so the key never changes its meaning at the bottom of the window.
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
        _currentFolderItem.Click += (_, _) => OpenFolder();
        project.DropDownItems.Add(_currentFolderItem);
        project.DropDownItems.Add(new ToolStripMenuItem("&Change folder...", null, (_, _) => ChangeFolder()));
        project.DropDownItems.Add(new ToolStripMenuItem("&New session...", null, (_, _) => NewSession()) { ShortcutKeys = Keys.Control | Keys.N });
        _recentFoldersItem.DropDownOpening += (_, _) => FillRecentFolders();
        _recentFoldersItem.DropDownItems.Add(new ToolStripMenuItem("(none)") { Enabled = false });
        project.DropDownItems.Add(_recentFoldersItem);
        project.DropDownItems.Add(new ToolStripMenuItem("&Force Claude to restart", null, (_, _) => RestartClaude()) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.R });
        project.DropDownItems.Add(new ToolStripSeparator());
        project.DropDownItems.Add(new ToolStripMenuItem("&Save conversation as...", null, (_, _) => SaveConversation()) { ShortcutKeyDisplayString = "Ctrl+S" });
        project.DropDownItems.Add(new ToolStripSeparator());
        project.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (_, _) => Close()) { ShortcutKeyDisplayString = "Alt+F4" });

        var session = new ToolStripMenuItem("&Session");
        session.DropDownItems.Add(new ToolStripMenuItem("Send &message", null, (_, _) => SendInput()) { ShortcutKeyDisplayString = "Enter in the message field" });
        session.DropDownItems.Add(new ToolStripMenuItem("Send the waiting message &now", null, (_, _) => SendNow()) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.S });
        session.DropDownItems.Add(new ToolStripMenuItem("&Interrupt Claude (send Escape)", null, (_, _) => Interrupt()) { ShortcutKeyDisplayString = "Shift+Esc" });
        session.DropDownItems.Add(new ToolStripMenuItem("Send Ctrl+&C", null, (_, _) => Write("\x03")) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.C });
        session.DropDownItems.Add(new ToolStripMenuItem("Send Ctrl+&D", null, (_, _) => Write("\x04")));
        session.DropDownItems.Add(new ToolStripMenuItem("Send &Tab", null, (_, _) => Write("\t")));
        session.DropDownItems.Add(new ToolStripMenuItem("Send Shift+Tab (next permission &mode)", null, (_, _) => Write("\x1b[Z")) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.M });
        session.DropDownItems.Add(new ToolStripMenuItem("Send &Up", null, (_, _) => Write("\x1b[A")) { ShortcutKeyDisplayString = "Ctrl+Up in the message field" });
        session.DropDownItems.Add(new ToolStripMenuItem("Send Do&wn", null, (_, _) => Write("\x1b[B")) { ShortcutKeyDisplayString = "Ctrl+Down in the message field" });

        var navigate = new ToolStripMenuItem("&Navigate");
        navigate.DropDownItems.Add(new ToolStripMenuItem("Go to &message field", null, (_, _) => GoTo(BottomControl)) { ShortcutKeyDisplayString = "Ctrl+1" });
        navigate.DropDownItems.Add(new ToolStripMenuItem("Go to &conversation", null, (_, _) => GoTo(_transcript)) { ShortcutKeyDisplayString = "Ctrl+2" });
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
        options.DropDownItems.Add(Toggle("Play &sounds: ready, sent, replying, done, question", _settings.SoundOnBell, v => _settings.SoundOnBell = v));
        options.DropDownItems.Add(Toggle("Tick for each &new line and tool call from Claude", _settings.ClickOnNewLine, v => _settings.ClickOnNewLine = v));
        options.DropDownItems.Add(Toggle("&Flash the taskbar button when Claude is done", _settings.FlashTaskbar, v => _settings.FlashTaskbar = v));
        // FR-7.4: one of three, shown checked like a radio group; the choice is announced, since the menu closes on it.
        var speech = new ToolStripMenuItem("Speak &replies as they arrive");
        foreach (var (mode, text) in ReplySpeechChoices)
        {
            var item = new ToolStripMenuItem(text) { Checked = _settings.ReplySpeech == mode };
            item.Click += (_, _) => SetReplySpeech(mode);
            speech.DropDownItems.Add(item);
            _replySpeechItems.Add((mode, item));
        }

        options.DropDownItems.Add(speech);
        // FR-7.4: the tool: row of each call, spoken as it arrives, whatever the reply mode; starts with what arrives next.
        options.DropDownItems.Add(Toggle("Speak each tool &call", _settings.SpeakToolCalls, v =>
        {
            _settings.SpeakToolCalls = v;
            _arrivals.Skip(_model.Lines);
        }));
        options.DropDownItems.Add(Toggle("Show the &time in the Input and Output lines", _settings.MarkerTimeStamps, v =>
        {
            _settings.MarkerTimeStamps = v;
            _model.TimeStamps = v;
        }));
        options.DropDownItems.Add(Toggle("Check for &updates when AxClaude starts", _settings.CheckForUpdates, v => _settings.CheckForUpdates = v));
        // D33: off, what Claude waits on is only announced, as before 1.4.0: no control area, no guard, no waiting screens.
        options.DropDownItems.Add(Toggle("Answer Claude's &questions from a list", _settings.QuestionNotices, v =>
        {
            _settings.QuestionNotices = v;
            _model.DetectScreens = v;
            if (!v)
            {
                CloseArea();
            }
        }));
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
        help.DropDownItems.Add(new ToolStripMenuItem("&Keyboard shortcuts", null, (_, _) => ShowNotice(Notice.Plain("Keyboard shortcuts", HelpText.Shortcuts))) { ShortcutKeys = Keys.F1 });
        help.DropDownItems.Add(new ToolStripMenuItem("&User guide", null, (_, _) => ShowNotice(Notice.Plain("User guide", HelpText.UserGuide()))));
        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add(new ToolStripMenuItem("Claude Code &documentation (web)", null, (_, _) => OpenUrl("https://code.claude.com/docs/en/overview")));
        help.DropDownItems.Add(new ToolStripMenuItem("Claude Code slash &commands (web)", null, (_, _) => OpenUrl("https://code.claude.com/docs/en/commands")));
        help.DropDownItems.Add(new ToolStripMenuItem("Claude Code &keys (web)", null, (_, _) => OpenUrl("https://code.claude.com/docs/en/interactive-mode")));
        help.DropDownItems.Add(new ToolStripMenuItem("Claude Code command &line (web)", null, (_, _) => OpenUrl("https://code.claude.com/docs/en/cli-reference")));
        help.DropDownItems.Add(new ToolStripMenuItem("Install or update Claude Code (&web)", null, (_, _) => OpenUrl(ClaudeLauncher.InstallUrl)));
        help.DropDownItems.Add(new ToolStripSeparator());
        // FR-1.10: the version in use, the latest release GitHub named, and the way to update, always in the menu.
        _installedItem.Click += (_, _) => ShowAbout();
        help.DropDownItems.Add(_installedItem);
        SetLatest(_latestState);
        _latestItem.Click += (_, _) => OpenLatestRelease();
        help.DropDownItems.Add(_latestItem);
        _updateItem.Click += (_, _) => CheckForUpdates(manual: true);
        help.DropDownItems.Add(_updateItem);
        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add(new ToolStripMenuItem("Copy diag&nostics", null, (_, _) => CopyDiagnostics()));
        help.DropDownItems.Add(new ToolStripMenuItem("&About", null, (_, _) => ShowAbout()));

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
        _input.AccessibleDescription = "Enter sends, Shift+Enter starts a new line";
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
        _transcript.EscapePressed += () => BottomControl.Focus();
        _transcript.LineChosen += QuoteLineInMessage;
        _transcript.BookmarkToggled += ToggleBookmark;
        _overlay.Chosen += OnNoticeChoice;

        // The overlay is hidden until a notice shows; it then fills the space of the hidden conversation and field.
        Controls.Add(_overlay);
        Controls.Add(_transcript);
        Controls.Add(_input);
        // What Claude waits on takes the message field's place (D33), one control at a time.
        _area.TabIndex = 0;
        Controls.Add(_area);
        SizeChanged += (_, _) => _area.FitHeight();
        Controls.Add(_status);
        Controls.Add(_menu);
        _transcript.BringToFront();

        UpdateFolderUi();
    }

    /// <summary>Options → Speak replies as they arrive → a mode (FR-7.4): speech starts with what arrives next.</summary>
    private void SetReplySpeech(ReplySpeechMode mode)
    {
        _settings.ReplySpeech = mode;
        foreach (var (candidate, item) in _replySpeechItems)
        {
            item.Checked = candidate == mode;
        }

        _arrivals.Skip(_model.Lines);
        SaveSettings();
        Announce("Speak replies: " + ReplySpeechChoices.First(c => c.Mode == mode).Text.Replace("&", string.Empty), true);
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
            case Keys.Enter when e.Modifiers == Keys.None:
                // Enter sends; Shift+Enter is the field's own new line (FR-2.1).
                e.Handled = e.SuppressKeyPress = true;
                SendInput();
                break;
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
            case Keys.PageUp when e.Modifiers == Keys.None:
            case Keys.PageDown when e.Modifiers == Keys.None:
                // One screen of the field's rows, or its first / last row (FR-2.1): the control's own keys do nothing while the message fits.
                e.Handled = e.SuppressKeyPress = true;
                EditPaging.Page(_input, e.KeyCode == Keys.PageDown ? 1 : -1);
                break;
                // Plain Up and Down only move the caret (D13): text appearing in the field on an arrow key confused the reading.
        }
    }

    /// <summary>The literal lines around a quoted conversation line in the message, so Claude can tell it from the user's words (FR-2.9).</summary>
    private const string QuoteMarker = "_ start of copied line from conversation history _";
    private const string CommentMarker = "_ start of comment on the copied line _";

    /// <summary>
    /// Enter on a conversation line appends the line to the message under the marker and a line naming its number,
    /// separated from what is there by a blank line, then the comment marker, and moves the focus into the field
    /// with the caret on the empty line under it, ready for the comment (FR-2.9). The conversation caret stays
    /// where it is, so Ctrl+2 comes back to the same line.
    /// </summary>
    private void QuoteLineInMessage(int number, int count, string text)
    {
        var existing = _input.Text.TrimEnd('\r', '\n');
        var quote = $"{QuoteMarker}\r\nLine {number} of {count}:\r\n{text}\r\n{CommentMarker}\r\n";
        _input.Text = existing.Length == 0 ? quote : existing + "\r\n\r\n" + quote;
        _input.Select(_input.TextLength, 0);
        Announce($"Line {number} copied to the message", true);
        GoTo(BottomControl);
    }

    /// <summary>
    /// Text that answers what Claude waits on: any text on an "Enter text for" row; else one of the question's keys,
    /// or several when it allows several; or, when the question could not be read, y, n or a number. Never a key of a
    /// screen.
    /// </summary>
    private bool IsAnswer(string text) =>
        _model.AwaitsText
        || _model.PendingScreen is null
        && (_model.PendingQuestion is { } question
            ? question.Accepts(text)
            : text is "y" or "n" or "Y" or "N" || text.All(char.IsAsciiDigit));

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
        if (_settings.QuestionNotices && text.Length > 0 && _model.PromptPending && !IsAnswer(text) && text != _keptMessage)
        {
            // D33: text sent into a question is lost (Claude answers "Please answer y or n." and drops it) or taken
            // as a key of the screen. It stays in the field; the same text sent again goes anyway (a typed answer).
            _keptMessage = text;
            var about = _model.PendingQuestion is { } waiting ? $" to {waiting.Title}"
                : _model.PendingScreen is { } screen ? $" on its screen {screen.Title}" : string.Empty;
            Announce($"Claude is waiting for an answer{about}. Your message was not sent and is still in the field. Press Enter again to send it anyway.", true);
            return;
        }

        _keptMessage = null;
        _input.Clear();
        if (text.Length == 0)
        {
            Write("\r");
            return;
        }

        var answer = _model.PromptPending;
        if (answer && IsAnswer(text))
        {
            // Typed like the control area's answers, one keystroke each ("1,3" for several answers, the words of an
            // answer of the user's own on one line).
            SendAnswer(text.Replace('\n', ' '));
            Announce("Answer sent", false);
            return;
        }

        // A question that follows belongs to the slash command sent last (QuestionRouter); an answer keeps it for the
        // next step of the same screen, a message ends it.
        if (!answer)
        {
            _lastCommand = SlashCommands.Find(text);
        }

        var queued = _model.Working;
        _model.Send(text);
        _messageSent = true;

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
        if (_settings.SoundOnBell)
        {
            Sounds.Sent();
        }
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

    /// <summary>
    /// Session → Send the waiting message now (Ctrl+Shift+S): Claude's Ctrl+X Ctrl+S, which makes it take up a
    /// message it holds without finishing the current step first (FR-2.8). The chord goes as two writes.
    /// </summary>
    private void SendNow()
    {
        if (_model.QueuedMessages == 0)
        {
            Announce("No message waiting", false);
            return;
        }

        _writes.Enqueue(("\x18", 30));
        _writes.Enqueue(("\x13", 30));
        if (!_writing)
        {
            PumpWrites();
        }

        Announce("Sending now", false);
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

    /// <summary>Escape to Claude (FR-2.3), and what is still queued to type goes: an answer typed out a key at a time must not land after it.</summary>
    private void Interrupt()
    {
        _writes.Clear();
        Write("\x1b");
    }

    // ---- model events ----

    private void OnModelChanged()
    {
        _transcript.Sync(_model.Lines);
        UpdateStatus();

        // One pass over the transcript for the tick (FR-7.8) and the spoken reply and tool calls (FR-7.4). Nothing is
        // spoken before Claude is ready in this run: what it prints until then is the replayed conversation, there for
        // reading. The chimes hang on the send, the first reply line and the bell (FR-7.3), never on the working status.
        var arrival = _arrivals.Update(_model.Lines, _model.Working, _readyAnnounced ? _settings.ReplySpeech : ReplySpeechMode.None, _readyAnnounced && _settings.SpeakToolCalls);
        if (_settings.ClickOnNewLine)
        {
            // A frame that brings a tool call gets the tool tick in place of the line's (FR-7.8).
            if (arrival.ToolCall)
            {
                Sounds.Tool();
            }
            else if (arrival.NewLine)
            {
                Sounds.Click();
            }
        }

        if (arrival.Speech is { } speech)
        {
            Announce(speech, false);
        }

        // What Claude waits on is acted on once the screen has settled for the attention timer (AnnounceAttention):
        // showing it in the control area, and putting the message field back when it went away, since Claude redraws
        // a question in passing.
        var waiting = WaitSignature;
        if (_shownWait is { } shown && waiting != shown && !_attention.Enabled)
        {
            _attention.Start();
        }

        if (_model.PromptPending)
        {
            if (_model.HideReplay)
            {
                // A question before Claude is ready must be seen, replay or not.
                _model.ShowHiddenLines();
            }

            // A new question or screen, or the next step of one, waits for the screen to settle like the first.
            if (!_promptAnnounced || (waiting != _handledWait && !_attention.Enabled && _shownWait is null))
            {
                _attention.Stop();
                _attention.Start();
            }
        }
        else
        {
            _promptAnnounced = false;
            _handledWait = null;
        }

        if (!_readyAnnounced && _host is not null && _model.Mode is not null && !_model.Working && !_model.PromptPending)
        {
            _readyAnnounced = true;
            _model.HideReplay = false;
            // Speech starts here: the replayed conversation gets its markers and is not read out.
            _arrivals.Skip(_model.Lines);
            _model.MarkReplayedExchanges();
            Announce("Claude is ready", false);
            if (_settings.SoundOnBell)
            {
                Sounds.Ready();
            }
        }
    }

    private void AnnounceAttention()
    {
        var waiting = WaitSignature;
        if (_model.PromptPending && _settings.QuestionNotices && waiting != _handledWait && ShowWaiting())
        {
            // In the control area, which replaced what it showed before (the next question of a set) in place.
        }
        else if (_model.PromptPending)
        {
            // Claude moved on from what the control area shows to something it cannot show (D33).
            if (_shownWait is { } shown && waiting != shown)
            {
                CloseArea();
            }

            if (!_promptAnnounced || waiting != _handledWait)
            {
                _promptAnnounced = true;
                _handledWait = waiting;
                // Recorded for q.
                _model.MarkQuestion();
                Announce("Claude needs your answer", false);
                Signal(question: true);
            }
        }
        else
        {
            // Nothing waits any more: the message field comes back. Said only when Claude closed it by itself, not
            // right after the user's answer or key.
            if (_area.Current is not null)
            {
                var byUser = Environment.TickCount64 - Math.Max(_answerAt, _screenKeyAt) < 3000;
                CloseArea();
                if (!byUser)
                {
                    Announce("The question closed", false);
                }
            }

            if (_bellPending)
            {
                if (_settings.AnnounceBell)
                {
                    Announce("Claude is done", false);
                }

                Signal(question: false);
            }
        }

        _bellPending = false;
    }

    /// <summary>
    /// Shows what Claude waits on in the control area (D33): a list of answers unless the slash command sent last
    /// leaves its lists to the message field, a screen waiting on its hint row, or the row for the user's own words
    /// after Other. False when it is none of these.
    /// </summary>
    private bool ShowWaiting()
    {
        if (_model.PendingQuestion is { } question)
        {
            if (QuestionRouter.Route(question, _lastCommand).Dialog != QuestionDialog.AnswerNotice)
            {
                return false;
            }

            Mark(question.Signature);
            ShowClaudeQuestion(question);
        }
        else if (_model.PendingScreen is { } screen)
        {
            Mark(screen.Signature);
            ShowClaudeScreen(screen);
        }
        else if (_model.TextPrompt is { } prompt)
        {
            Mark(prompt);
            ShowOwnAnswer(prompt);
        }
        else
        {
            return false;
        }

        // The taskbar flash; the area plays its own chime.
        Signal(question: true, chime: false);
        return true;

        void Mark(string signature)
        {
            _promptAnnounced = true;
            _handledWait = signature;
            // Recorded for q (nothing for the text row after Other, which belongs to the question before).
            _model.MarkQuestion();
        }
    }

    /// <summary>
    /// The chime (two equal notes for a question, three falling notes when Claude is done, FR-7.3) and the taskbar
    /// flash that go with an attention announcement; without <paramref name="chime"/> only the flash (the answer
    /// control area plays its own chime).
    /// </summary>
    private void Signal(bool question, bool chime = true)
    {
        if (_settings.SoundOnBell && chime)
        {
            if (question)
            {
                Sounds.Question();
            }
            else
            {
                Sounds.Done();
            }
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

        if (ClaudeBackground is { } background)
        {
            parts.Add(background + " in the background");
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
        _currentFolderItem.Text = "Open current &folder: " + (_folder ?? "(none)").Replace("&", "&&");
    }

    private static string FolderName(string folder)
    {
        var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return name.Length > 0 ? name : folder;
    }

    /// <summary>
    /// Ctrl+1, Ctrl+2 and the Navigate menu (FR-6.5): the control takes the focus, and NVDA announces the change. When it
    /// has the focus already, its name is spoken instead, so the key also answers "where am I".
    /// </summary>
    private void GoTo(Control control)
    {
        if (control.Focused)
        {
            Announce(control.AccessibleName ?? string.Empty, true);
            return;
        }

        control.Focus();
    }

    /// <summary>Speaks without moving the focus; from the notice while one shows, since the conversation is hidden then.</summary>
    private bool Announce(string text, bool interrupt) =>
        _noticeOpen ? _overlay.Announce(text, interrupt) : _transcript.Announce(text, interrupt);

    // ---- find and save ----

    /// <summary>Ctrl+F: the Find notice (FR-3.7). Find next closes it and searches from the caret; an empty field keeps it open.</summary>
    private void FindDialog()
    {
        ShowNotice(new Notice("Find", string.Empty,
        [
            new OverlayChoice("Find &next", () =>
            {
                _findText = _overlay.InputText;
                _transcript.Find(_findText, backward: false);
            }, IsDefault: true),
            new OverlayChoice("Cancel", IsCancel: true),
        ])
        {
            Input = new OverlayInput("&Find:", "Text to find", _findText, "Type the text to find"),
        });
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
        _area.Font = font;
        _area.MinimumHeight = _input.Height;
        _area.FitHeight();
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
            $"Program: {Environment.ProcessPath ?? "(unknown)"} (process {Environment.ProcessId})",
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

    /// <summary>Project → Current folder, Ctrl+W (FR-8.2): the folder in the default file manager, one of the system's own windows (D23).</summary>
    private void OpenFolder()
    {
        if (_folder is null)
        {
            Announce("No project folder", true);
            return;
        }

        if (!Directory.Exists(_folder))
        {
            Announce("The folder no longer exists", true);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            Fail("The folder could not be opened: " + ex.Message);
        }
    }

    /// <summary>The standard folder picker (one of the few separate windows, D23); null when it was cancelled.</summary>
    private string? PickFolder(string? start)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the project folder for Claude",
            UseDescriptionForTitle = true,
            InitialDirectory = start ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ShowNewFolderButton = true,
        };
        return dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dialog.SelectedPath) ? dialog.SelectedPath : null;
    }

    /// <summary>Makes the folder the project folder: the title, the status bar and the Project menu follow, and the default arguments apply there again (FR-9.1).</summary>
    private void SetFolder(string folder)
    {
        _folder = folder;
        _nothingToContinue = false;
        UpdateFolderUi();
    }

    /// <summary>Project → Change folder… (FR-8.3): the picker, then Claude restarts in the chosen folder.</summary>
    private void ChangeFolder()
    {
        if (PickFolder(_folder) is { } folder)
        {
            SwitchFolder(folder);
        }
    }

    /// <summary>Restarts Claude in another folder (Change folder, Recent folders), with a system line and an announcement.</summary>
    private void SwitchFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            Fail("The folder no longer exists: " + folder);
            return;
        }

        SetFolder(folder);
        Announce($"Project folder changed to {FolderName(folder)}", false);
        // Another folder is another conversation (FR-8.3): the window is cleared, and what Claude prints there, a
        // replay with its blocks when the folder has one, arrives as at the first start of the app.
        Relaunch($"Project folder changed to {folder}", clearConversation: true);
    }

    // ---- New session (FR-8.6) ----

    /// <summary>The recommended ways to start Claude, in the order of the notice; Custom comes after them.</summary>
    private static readonly OverlayPreset[] SessionPresets =
    [
        new("New conversation", []),
        new("Continue the last conversation", ["--continue"]),
        new("Choose a conversation to resume", ["--resume"]),
        new("New conversation in plan mode", ["--permission-mode", "plan"]),
        new("New conversation, edits accepted without asking", ["--permission-mode", "acceptEdits"]),
    ];

    /// <summary>
    /// Project → New session… (Ctrl+N): the folder with a Choose folder button under it, the recommended argument
    /// sets as radio buttons, Custom with a free argument field. Enter stops the current session and starts a new
    /// one in the folder with those arguments; Escape keeps the current session.
    /// </summary>
    private void NewSession()
    {
        var current = ClaudeArgs;
        var selected = Array.FindIndex(SessionPresets, preset => preset.Arguments.SequenceEqual(current, StringComparer.Ordinal));
        var custom = selected < 0 ? ClaudeLauncher.JoinArguments(current) : string.Empty;
        var text = "Choose how Claude starts. Starting stops the current session and starts a new one in the folder.";
        if (ClaudeBusy)
        {
            text = "Claude is still working. A new session stops it in the middle of its work.\n" + text;
        }
        else if (ClaudeBackground is { } background)
        {
            text = $"Claude has work running in the background ({background}). A new session stops it.\n" + text;
        }

        ShowNotice(new Notice("New Claude session", text,
        [
            new OverlayChoice("&Start session", StartNewSession, IsDefault: true),
            new OverlayChoice("Cancel", IsCancel: true),
        ])
        {
            Session = new OverlaySession(_folder, ChooseSessionFolder, SessionPresets, selected < 0 ? SessionPresets.Length : selected, custom),
        });
    }

    private void ChooseSessionFolder()
    {
        if (PickFolder(_overlay.SessionFolder ?? _folder) is { } folder)
        {
            _overlay.SetSessionFolder(folder);
        }
    }

    private void StartNewSession()
    {
        var folder = _overlay.SessionFolder;
        if (folder is null || !Directory.Exists(folder))
        {
            Fail("The folder no longer exists: " + folder);
            return;
        }

        var arguments = _overlay.SelectedPreset?.Arguments ?? ClaudeLauncher.SplitArguments(_overlay.CustomArguments);
        SetFolder(folder);
        _claudeArgs = [.. arguments];
        var with = arguments.Count > 0 ? " with " + ClaudeLauncher.JoinArguments(arguments) : " with no extra arguments";
        Announce($"New session in {FolderName(folder)}", false);
        // A new session is a new conversation (or a resumed one): the window is cleared first, and what Claude prints
        // then, a replay with its blocks included, arrives as at the first start of the app.
        Relaunch($"New session in {folder}{with}", clearConversation: true);
    }

    // ---- Claude process ----

    /// <summary>The arguments Claude gets after --ax-screen-reader: the chosen ones, or the default (FR-9.1).</summary>
    private IReadOnlyList<string> ClaudeArgs => _claudeArgs ?? (_nothingToContinue ? [] : ["--continue"]);

    /// <summary>
    /// Project → Force Claude to restart (Ctrl+Shift+R, FR-1.8): the same folder and arguments; the conversation
    /// stays. A running Claude is stopped only after the Force Claude to restart? notice; a stopped one starts at once.
    /// </summary>
    private void RestartClaude()
    {
        if (_host is null)
        {
            ForceRestart();
            return;
        }

        var state = !ClaudeBusy ? string.Empty
            : _model.PromptPending ? "Claude is waiting for your answer. "
            : "Claude is still working. ";
        if (ClaudeBackground is { } background)
        {
            state += $"Claude has work running in the background ({background}). ";
        }

        ShowNotice(new Notice("Force Claude to restart?",
            state + "Restarting immediately quits Claude Code and terminates any processes it runs in the background, then starts it again in the same folder with the same options. The conversation stays in the window.",
        [
            new OverlayChoice("&Restart now", ForceRestart, IsDefault: true),
            new OverlayChoice("&Keep Claude running", IsCancel: true),
        ]));
    }

    private void ForceRestart()
    {
        Announce("Restarting Claude", false);
        Relaunch("Restarting Claude");
    }

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

        arguments.AddRange(ClaudeArgs);
        _launchedFolder = _folder;

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
        if (_model.HideReplay)
        {
            // Claude ended before it was ready: whatever it printed is the explanation.
            _model.ShowHiddenLines();
        }

        if (_claudeArgs is null && !_nothingToContinue && _model.SaidNoConversationToContinue())
        {
            // The default --continue in a folder without a conversation: Claude says so and exits (FR-9.1).
            _nothingToContinue = true;
            Announce("No conversation to continue. Starting a new one", false);
            Relaunch("No conversation to continue here; starting a new one");
            return;
        }

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
        // Nothing waits on an answer any more: the message field comes back.
        CloseArea();
        UpdateStatus();
    }

    /// <summary>
    /// Stops Claude and starts it again with the current folder and arguments, after the system line. The
    /// conversation stays in the window (Restart Claude) unless <paramref name="clearConversation"/> is set (New
    /// session, a folder change; FR-8.3, FR-8.6), in which case the system line is the first line of the emptied window.
    /// </summary>
    private void Relaunch(string systemLine, bool clearConversation = false)
    {
        StopClaude();
        if (clearConversation)
        {
            _model.Clear();
        }
        else
        {
            _model.ResetScreen();
        }

        _model.AddSystemLine(systemLine);
        // The same folder with --continue replays the conversation the window already shows: hidden until Claude is
        // ready (FR-4.8). A cleared window shows the replay with its blocks, as at startup.
        _model.HideReplay = !clearConversation && _folder is not null && string.Equals(_folder, _launchedFolder, StringComparison.OrdinalIgnoreCase) && ClaudeArgs.Contains("--continue");
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
        ShowNotice(Notice.Plain("Error", message) with { Unprompted = true });
    }

    /// <summary>The crash handler's report (FR-13.4), shown inside the window like every other notice.</summary>
    public void ShowError(string title, string message) => ShowNotice(Notice.Plain(title, message) with { Unprompted = true });

    // ---- notices: the app's own dialogs, drawn inside the window (D23) ----

    /// <summary>
    /// The one place a notice is shown (D23); callers pass only its content. It adds what every notice shares: the key
    /// line made from the buttons at the end of the text, the notice chime and the hold of a notice the user did not
    /// open, the focus in the notice (the field, the chosen answer, or the top of the text), the blocked menu and
    /// window shortcuts, and Enter and Escape on the default and cancel buttons. A notice shown while another is
    /// open replaces it.
    /// </summary>
    private void ShowNotice(Notice notice)
    {
        if (notice.Unprompted && _settings.SoundOnBell)
        {
            Sounds.Notice();
        }

        if (!_noticeOpen)
        {
            // The conversation, or else the bottom of the window (null: whichever control is there when the notice closes).
            _focusBeforeNotice = _transcript.Focused ? _transcript : null;
            // A reader keeps their line while the notice hides the conversation (FR-3.3).
            _transcript.KeepCaret = _focusBeforeNotice is not null;
        }

        _noticeOpen = true;
        _overlay.Populate(notice with { Text = WithKeyLine(notice.Text, notice.Choices) });
        _overlay.Visible = true;
        _overlay.BringToFront();
        AcceptButton = _overlay.DefaultButton;
        CancelButton = _overlay.CancelButton;
        _overlay.FocusStart();
        _transcript.Visible = false;
        _input.Visible = false;
        _area.Visible = false;
        _menu.Enabled = false;
        PerformLayout();
    }

    /// <summary>
    /// The last line of every notice with text, made from its buttons so that no notice words it differently:
    /// "Enter: Close anyway. Escape: Keep working." or "Enter or Escape: Close." for a single button.
    /// </summary>
    private static string WithKeyLine(string text, IReadOnlyList<OverlayChoice> choices)
    {
        var enter = choices.FirstOrDefault(choice => choice.IsDefault);
        var escape = choices.FirstOrDefault(choice => choice.IsCancel);
        // The button's text without its mnemonic marker or a trailing ellipsis.
        static string Label(OverlayChoice choice) => choice.Text.Replace("&", string.Empty).TrimEnd('.');
        var keys = enter is not null && ReferenceEquals(enter, escape) ? $"Enter or Escape: {Label(enter)}."
            : string.Join(" ", new[] { enter is null ? null : $"Enter: {Label(enter)}.", escape is null ? null : $"Escape: {Label(escape)}." }.OfType<string>());
        return text.Length == 0 || keys.Length == 0 ? text : text.TrimEnd('\n') + "\n" + keys;
    }

    // ---- the control area: what Claude waits on, in the message field's place (D33) ----

    /// <summary>The control at the bottom of the window: the message field, or the control area's list or field in its place.</summary>
    private Control BottomControl => _area.Current ?? _input;

    /// <summary>
    /// Shows what Claude waits on in the control area, in place of the message field (D33). <paramref name="show"/>
    /// puts the control there, given whether it takes the focus (it does when the focus was at the bottom of the
    /// window) and whether it holds its answering keys for a moment (when the user did not cause it, since they may be
    /// typing). Such a control also plays the notice chime; when the focus is in the conversation, it stays there and
    /// the question is said after whatever NVDA is reading.
    /// </summary>
    private void ShowInArea(string signature, string name, bool unprompted, Action<bool, bool> show)
    {
        var focus = !_noticeOpen && (_input.ContainsFocus || _area.ContainsFocus);
        if (unprompted && _settings.SoundOnBell)
        {
            Sounds.Notice();
        }

        show(focus, unprompted);
        _input.Visible = false;
        _area.Visible = !_noticeOpen;
        _shownWait = signature;
        if (!focus && unprompted)
        {
            Announce($"Claude asks: {name}. Ctrl+Tab to answer", false);
        }
    }

    /// <summary>The message field comes back in the control area's place, with the focus when the area had it.</summary>
    private void CloseArea()
    {
        if (_area.Current is null)
        {
            return;
        }

        var focus = _area.ContainsFocus;
        _input.Visible = !_noticeOpen;
        if (focus)
        {
            _input.Focus();
        }

        _area.Clear();
        _shownWait = null;
    }

    /// <summary>
    /// A question as a list: its answers as items ("2. Green"), the current one marked and the start, or check boxes
    /// when it takes several, ticked where Claude marks them. Enter hands <paramref name="choose"/> the chosen
    /// answer, or the ticked ones; <paramref name="startKey"/> starts on another answer (the setting just changed).
    /// </summary>
    private void ShowQuestionList(string signature, string name, Question question, string? startKey, bool unprompted,
        Action<IReadOnlyList<QuestionOption>> choose, Action escape)
    {
        var options = question.Options;
        var several = question.AllowsSeveral;
        var items = options.Select(option => $"{option.Key}. {option.Text}" + (option.Current && !several ? " (current)" : string.Empty)).ToList();
        var start = Math.Max(0, options.ToList().FindIndex(option => startKey is null ? option.Current : option.Key == startKey));
        IReadOnlyList<int>? ticked = several ? Enumerable.Range(0, options.Count).Where(i => options[i].Current).ToList() : null;
        ShowInArea(signature, name, unprompted, (focus, hold) => _area.ShowList(name, items, start, ticked, focus, hold, (focused, ticks) =>
        {
            if (several && ticks.Count == 0)
            {
                Announce("Tick at least one answer with Space, then press Enter.", true);
                return;
            }

            choose(several ? ticks.Select(i => options[i]).ToList() : [options[focused]]);
        }, escape));
    }

    /// <summary>What the list of a question is called, read each time the focus comes to it: the title (with its place among several) and the question's first line.</summary>
    private static string QuestionName(Question question)
    {
        var title = question.Step is (int number, int count) ? $"Question {number} of {count}: {question.Title}" : question.Title;
        return question.Text.Count > 0 ? $"{title}. {question.Text[0]}" : title;
    }

    /// <summary>
    /// One of Claude's questions (D33): the answers as a list in the control area. Enter answers, Escape cancels the
    /// question. Other, like any answer, is sent as its key; Claude then asks for the words, and
    /// <see cref="ShowOwnAnswer"/> follows.
    /// </summary>
    private void ShowClaudeQuestion(Question question)
    {
        if (question.IsSettings)
        {
            ShowSettings(question);
            return;
        }

        _askedQuestion = question;
        ShowQuestionList(question.Signature, QuestionName(question), question, null, Environment.TickCount64 - _answerAt > 3000, AnswerQuestion, () =>
        {
            SendEscape();
            Announce("Question cancelled", false);
        });
    }

    /// <summary>
    /// /config's list of settings (docs/claude-screens.md), read off Claude's screen each time it shows. Enter on a
    /// true-or-false setting asks for its value (<see cref="ShowSwitch"/>), since Claude flips it when its number is
    /// sent; on any other setting it sends the number and Claude's list of its values follows. Either way the list comes
    /// back on the setting just chosen. Escape saves and closes.
    /// </summary>
    private void ShowSettings(Question settings, bool back = false) =>
        ShowQuestionList(settings.Signature, "Claude's settings", settings, _settingKey, !back && Environment.TickCount64 - _answerAt > 3000, chosen =>
        {
            var setting = chosen[0];
            _settingKey = setting.Key;
            if (setting.Switch is { } on)
            {
                ShowSwitch(settings, setting, on);
            }
            else
            {
                SendAnswer(setting.Key);
            }
        }, () =>
        {
            _settingKey = null;
            SendEscape();
            Announce("Settings saved", false);
        });

    /// <summary>A true-or-false setting of /config: True and False, the current one first in focus. Only a change sends the setting's number.</summary>
    private void ShowSwitch(Question settings, QuestionOption setting, bool on)
    {
        var name = setting.Setting?.Name ?? setting.Text;
        var values = new Question(name, [], [new QuestionOption("1", "True", on), new QuestionOption("2", "False", !on)], []);
        ShowQuestionList(settings.Signature, name, values, null, false, chosen =>
        {
            if (chosen[0].Current)
            {
                // Unchanged: nothing to send, and the settings come back on this one.
                ShowSettings(settings, back: true);
            }
            else
            {
                SendAnswer(setting.Key);
            }
        }, () => ShowSettings(settings, back: true));
    }

    /// <summary>Sends the chosen answer's key, or the ticked keys joined with commas, and says what comes next.</summary>
    private void AnswerQuestion(IReadOnlyList<QuestionOption> chosen)
    {
        SendAnswer(string.Join(",", chosen.Select(option => option.Key)));
        // Each answer's name without Claude's explanation after the dash.
        Announce("Answer sent: " + string.Join(", ", chosen.Select(option =>
        {
            var dash = option.Text.IndexOf(" — ", StringComparison.Ordinal);
            return $"{option.Key}, {(dash > 0 ? option.Text[..dash] : option.Text)}";
        })) + (chosen.Any(option => option.Text.Equals("Other", StringComparison.OrdinalIgnoreCase))
            // Claude asks for the words of Other next (ShowOwnAnswer).
            ? ". Next, type your own answer"
            : NextStep()), false);
    }

    /// <summary>After an answer to one of several questions, what comes next, so that the gap before it does not sound like the end.</summary>
    private string NextStep() => _askedQuestion?.Step is (int number, int count)
        ? number < count ? ". Next question coming" : ". The review of your answers is next"
        : string.Empty;

    /// <summary>
    /// The follow-up of a question when Claude asks for the user's own words ("Enter text for option 4 (Other), or
    /// Escape for the list:", also plan approval's "No, keep planning"): a field named with the question and the
    /// answer chosen. Enter sends the words, a keystroke each, and Escape goes back to Claude's list.
    /// </summary>
    private void ShowOwnAnswer(string prompt)
    {
        var chosen = LineClassifier.TextPromptAnswer(prompt) ?? "an answer that asks for your own words";
        var name = (_askedQuestion is { } question ? QuestionName(question) + ". " : string.Empty) + $"You chose {chosen}. Your answer";
        ShowInArea(prompt, name, Environment.TickCount64 - _answerAt > 3000, (focus, hold) => _area.ShowField(name, focus, hold, typed =>
        {
            var words = typed.Trim();
            if (words.Length == 0)
            {
                Announce("Type your answer first, or press Escape to go back to the list.", true);
                return;
            }

            SendAnswer(words);
            Announce("Answer sent: " + words + NextStep(), false);
        }, SendEscape));
    }

    /// <summary>What Claude waits on now, as a signature: the question, the screen, or the row for the user's own words; null when it waits on nothing.</summary>
    private string? WaitSignature => _model.PendingQuestion?.Signature ?? _model.PendingScreen?.Signature ?? _model.TextPrompt;

    /// <summary>
    /// A screen of Claude's that waits on its hint row (/status, /tasks, /help …; D33) as a list: its lines, which the
    /// conversation does not show for a tab screen, then an item for every key the hint row names ("View (Enter)",
    /// "Close (Escape)"). Enter on a key's item sends the key, Escape sends Escape; the screen Claude shows next takes
    /// the list's place, without the chime since the user caused it.
    /// </summary>
    private void ShowClaudeScreen(ClaudeScreen screen)
    {
        var items = screen.Lines.ToList();
        var first = items.Count;
        items.AddRange(screen.Keys.Select(key => $"{char.ToUpperInvariant(key.Action[0])}{key.Action[1..]} ({key.Name})"));
        var escape = screen.Keys.First(key => key.Send == "\x1b").Send;
        ShowInArea(screen.Signature, screen.Title, Environment.TickCount64 - _screenKeyAt > 3000, (focus, hold) =>
            _area.ShowList(screen.Title, items, 0, null, focus, hold, (index, _) =>
            {
                if (index < first)
                {
                    Announce("The keys are at the end of the list", true);
                    return;
                }

                SendScreenKey(screen.Keys[index - first].Send);
            }, () => SendScreenKey(escape)));
    }

    /// <summary>
    /// Escape from the control area: the question closes, or goes back a step. What Claude shows next is the user's
    /// doing, and the area holds its keys until then, so that a second Escape does not reach Claude as an interrupt.
    /// </summary>
    private void SendEscape()
    {
        _answerAt = Environment.TickCount64;
        _area.Hold(3000);
        Write("\x1b");
    }

    /// <summary>Sends a screen's key. The screen that shows next opens again even when its title and keys are the same (a moved selection, another tab).</summary>
    private void SendScreenKey(string key)
    {
        _screenKeyAt = Environment.TickCount64;
        _handledWait = null;
        _area.Hold(3000);
        Write(key);
        // A key that changes nothing draws no frame: the timer brings the screen back all the same.
        _attention.Stop();
        _attention.Start();
    }

    /// <summary>
    /// An answer through the send queue: each character as a keystroke of its own, then Enter. The control area holds
    /// its keys meanwhile, so that a second Enter does not send it again.
    /// </summary>
    private void SendAnswer(string key)
    {
        if (_host is null)
        {
            return;
        }

        // One keystroke per character (per rune, so an emoji stays whole): Claude's answer field takes typed keys, and
        // several characters in one write are a paste, which it ignores ("12" in a long list, "1,3" for several
        // answers). Then Enter.
        var keys = key.EnumerateRunes().Select(rune => rune.ToString()).ToList();
        for (var i = 0; i < keys.Count; i++)
        {
            _writes.Enqueue((keys[i], i == keys.Count - 1 ? 100 : 60));
        }

        _writes.Enqueue(("\r", 50));
        _answerAt = Environment.TickCount64;
        _area.Hold(3000);
        if (!_writing)
        {
            PumpWrites();
        }

        if (_settings.SoundOnBell)
        {
            Sounds.Sent();
        }
    }

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
        // The message field, or the control area when Claude asked something while the notice showed.
        _input.Visible = _area.Current is null;
        _area.Visible = _area.Current is not null;
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
            (_focusBeforeNotice ?? BottomControl).Select();
        }

        _overlay.Visible = false;
    }

    /// <summary>
    /// FR-1.7: a close while Claude is in the middle of a turn, or runs work in the background, is confirmed first.
    /// Enter closes, Escape keeps the window.
    /// </summary>
    private void ConfirmClose()
    {
        var background = ClaudeBackground;
        string state;
        if (ClaudeBusy)
        {
            state = _model.PromptPending ? "Claude is waiting for your answer" : "Claude is still working";
            if (_model.QueuedMessages > 0)
            {
                state += _model.QueuedMessages == 1
                    ? " and a message you sent is still waiting for it"
                    : $" and {_model.QueuedMessages} messages you sent are still waiting for it";
            }

            if (background is not null)
            {
                state += $", and it has work running in the background ({background})";
            }
        }
        else
        {
            state = $"Claude has work running in the background ({background})";
        }

        var stops = ClaudeBusy ? "Claude stops in the middle of its work" : "the background work stops";
        var consequence = _updateFolder is null
            ? $"If you close now, {stops}."
            : $"If you close now, {stops} and the update is installed.";
        ShowNotice(new Notice("Close AxClaude?",
            $"{state}. {consequence}\n" +
            "What is done so far is saved. Start AxClaude again to carry on.",
        [
            new OverlayChoice("&Close anyway", () =>
            {
                _closeConfirmed = true;
                Close();
            }, IsDefault: true),
            new OverlayChoice("&Keep working", CancelUpdate, IsCancel: true),
        ]));
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
        SetLatest("checking...");
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var release = await Updater.CheckAsync(cancellation.Token);
            if (IsDisposed)
            {
                return;
            }

            _latest = release;
            if (release is null)
            {
                SetLatest("none published yet");
            }
            else if (UpdateCheck.IsNewer(release, Program.Version))
            {
                var version = release.Version.ToString(3);
                _update = release;
                SetLatest($"AxClaude {version} (newer than this one)");
                _updateItem.Text = $"&Update to AxClaude {version}...";
                Log.Info($"Update available: {release.Tag}, zip {release.ZipUrl ?? "missing"}");
                if (!manual)
                {
                    _model.AddSystemLine($"AxClaude {version} is available. Help menu, Update to AxClaude {version}.");
                }

                if (manual || !_noticeOpen)
                {
                    // At startup the notice opens by itself: Enter updates, Escape keeps this version (FR-1.10).
                    ShowUpdateNotice(release, unprompted: !manual);
                }
                else
                {
                    Announce($"AxClaude {version} is available. See the Help menu", false);
                }

                return;
            }
            else
            {
                var same = release.Version == UpdateCheck.ParseVersion(Program.Version);
                SetLatest($"AxClaude {release.Version.ToString(3)} ({(same ? "this version" : "older than this build")})");
            }

            if (manual)
            {
                ShowNotice(Notice.Plain("Check for updates",
                    $"You have the newest version, AxClaude {Program.Version}.\n" +
                    (release is null ? "No release is published yet.\n" : string.Empty) +
                    $"Releases: {UpdateCheck.ReleasesPage}"));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            Log.Error("The update check failed", ex);
            SetLatest("unknown, GitHub could not be reached");
            if (manual && !IsDisposed)
            {
                ShowNotice(new Notice("Check for updates",
                    $"GitHub could not be reached: {ex.Message}\nThe releases page: {UpdateCheck.ReleasesPage}",
                [
                    new OverlayChoice("&Open releases page", () => OpenUrl(UpdateCheck.ReleasesPage), StaysOpen: true),
                    OverlayChoice.Close,
                ]));
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

    /// <summary>Help → Latest release: what GitHub named, or why nothing is known yet.</summary>
    private void SetLatest(string state)
    {
        _latestState = state;
        _latestItem.Text = "&Latest release: " + state;
    }

    /// <summary>Help → Latest release: opens the release page when one is known, otherwise checks.</summary>
    private void OpenLatestRelease()
    {
        if (_latest is { } latest)
        {
            OpenUrl(latest.PageUrl);
        }
        else
        {
            CheckForUpdates(manual: true);
        }
    }

    private void ShowAbout() =>
        ShowNotice(Notice.Plain("About AxClaude",
            $"AxClaude {Program.Version}\nA screen reader friendly window for Claude Code.\nMade by Dr. Kyle Keane, www.kylekeane.com. Free under the MIT licence.\n\n" +
            $"Latest release on GitHub: {_latestState}\nClaude Code documentation: https://code.claude.com/docs\nSettings: {AppSettings.DefaultPath}\nLog: {Log.FilePath}\n\n" +
            HelpText.Disclaimer));

    /// <summary>The release notes with Update now, Open release page and Later (FR-1.10); <paramref name="unprompted"/> when the check at startup found it.</summary>
    private void ShowUpdateNotice(ReleaseInfo release, bool unprompted = false)
    {
        var version = release.Version.ToString(3);
        var size = release.ZipSize > 0 ? $" The download is {release.ZipSize / (1024.0 * 1024.0):0} MB." : string.Empty;
        var text =
            $"You have AxClaude {Program.Version}. AxClaude {version} is available.\n\n" +
            (release.Notes.Length > 0 ? release.Notes + "\n\n" : string.Empty) +
            "Update now downloads the new version, closes AxClaude, installs it and starts it again on the same folder. " +
            $"The conversation is picked up again if one is open.{size}\n" +
            "Later keeps this version; the Help menu offers the update again.";
        ShowNotice(new Notice($"Update to AxClaude {version}", text,
        [
            new OverlayChoice("&Update now", () => InstallUpdate(release), IsDefault: true),
            new OverlayChoice("&Open release page", () => OpenUrl(release.PageUrl), StaysOpen: true),
            new OverlayChoice("&Later", IsCancel: true),
        ])
        {
            Unprompted = unprompted,
        });
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
                ShowNotice(new Notice($"Update to AxClaude {version}",
                    $"The download failed: {ex.Message}\nYou can download the zip from the release page and run install.ps1 yourself.",
                [
                    new OverlayChoice("&Open release page", () => OpenUrl(release.PageUrl), StaysOpen: true),
                    OverlayChoice.Close,
                ]));
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
        ShowNotice(new Notice("Claude Code was not found", HelpText.ClaudeNotFound(candidates),
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
        ])
        {
            Unprompted = true,
        });
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

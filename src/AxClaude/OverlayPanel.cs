using System.Windows.Forms.Automation;

namespace AxClaude;

/// <summary>A button on a notice. Unless <see cref="StaysOpen"/> is set, the notice closes before the action runs.</summary>
internal sealed record OverlayChoice(string Text, Action? Action = null, bool IsDefault = false, bool IsCancel = false, bool StaysOpen = false)
{
    /// <summary>The single button of a plain notice: Enter and Escape both close it.</summary>
    public static readonly OverlayChoice Close = new("&Close", IsDefault: true, IsCancel: true);
}

/// <summary>
/// A text field on a notice (Find): its label, its accessible name, the initial text, and what to say when the default
/// button is pressed while the field is empty (the notice then stays open).
/// </summary>
internal sealed record OverlayInput(string Label, string AccessibleName, string Initial, string EmptyMessage);

/// <summary>A recommended way to start Claude in the New session notice (FR-8.6): the radio button's text and the arguments it stands for.</summary>
internal sealed record OverlayPreset(string Text, IReadOnlyList<string> Arguments);

/// <summary>
/// The New session part of a notice (FR-8.6): the folder (null when none is chosen yet) with a Choose folder button
/// under it, the presets as radio buttons with Custom last (<paramref name="Selected"/> equal to the number of
/// presets selects Custom), and the argument field that shows while Custom is selected.
/// </summary>
internal sealed record OverlaySession(string? Folder, Action ChooseFolder, IReadOnlyList<OverlayPreset> Presets, int Selected, string CustomArguments);

/// <summary>
/// The window's own dialogs, drawn inside the window (D23). A notice, a question or a help text takes the place of the
/// conversation and the message field until it is answered, so there is never a second window to lose. The panel
/// holds a title, a read-only text, an optional text field, the optional New session controls and a row of buttons.
/// The window hides the controls underneath, blocks its own shortcuts and the menu, and routes Enter and Escape to
/// the default and cancel buttons through its AcceptButton and CancelButton; Tab moves between the text, the field
/// or the session controls, and the buttons.
/// </summary>
internal sealed class OverlayPanel : Panel
{
    private const string NoFolder = "(none chosen yet)";
    private const string ChooseFolderFirst = "Choose a folder first";

    private readonly Label _title = new();
    private readonly TextBox _text = new();
    private readonly Panel _inputRow = new();
    private readonly Label _inputLabel = new();
    private readonly TextBox _input = new();
    private readonly FlowLayoutPanel _session = new();
    private readonly Button _chooseFolder = new();
    private readonly GroupBox _presetGroup = new();
    private readonly FlowLayoutPanel _presetList = new();
    private readonly FlowLayoutPanel _customRow = new();
    private readonly Label _customLabel = new();
    private readonly TextBox _custom = new();
    private readonly List<RadioButton> _presets = [];
    private readonly FlowLayoutPanel _buttons = new();
    private OverlayInput? _inputSpec;
    private OverlaySession? _sessionSpec;
    private string? _sessionFolder;
    private string _body = string.Empty;

    public OverlayPanel()
    {
        Visible = false;
        Dock = DockStyle.Fill;
        BackColor = SystemColors.Control;
        Padding = new Padding(8);
        TabStop = false;

        _title.AutoSize = true;
        _title.Dock = DockStyle.Top;
        _title.Font = new Font(SystemFonts.MessageBoxFont ?? DefaultFont, FontStyle.Bold);
        _title.Padding = new Padding(0, 0, 0, 6);
        _title.UseMnemonic = false;

        // Enter is not taken by the text (AcceptsReturn is off), so it reaches the default button.
        _text.Multiline = true;
        _text.ReadOnly = true;
        _text.WordWrap = true;
        _text.ScrollBars = ScrollBars.Vertical;
        _text.AcceptsTab = false;
        _text.BackColor = SystemColors.Window;
        _text.ForeColor = SystemColors.WindowText;
        _text.Dock = DockStyle.Fill;
        _text.TabIndex = 2;

        _inputLabel.AutoSize = true;
        _inputLabel.Location = new Point(0, 6);
        _input.Location = new Point(0, 2);
        _inputRow.Dock = DockStyle.Top;
        _inputRow.Height = 32;
        _inputRow.TabIndex = 1;
        _inputRow.Controls.Add(_inputLabel);
        _inputRow.Controls.Add(_input);
        _inputRow.SizeChanged += (_, _) => LayoutInput();

        // The New session controls (FR-8.6), top to bottom under the text: the Choose folder button, the presets
        // in a named group (arrow keys move between them, Tab lands on the selected one), the Custom field.
        _session.Dock = DockStyle.Fill;
        _session.FlowDirection = FlowDirection.TopDown;
        _session.WrapContents = false;
        _session.AutoScroll = true;
        _session.Padding = new Padding(0, 6, 0, 0);
        _session.Visible = false;
        _session.TabIndex = 3;
        _session.SizeChanged += (_, _) => LayoutSession();

        _chooseFolder.Text = "Choose &folder...";
        _chooseFolder.AutoSize = true;
        _chooseFolder.Height = 32;
        _chooseFolder.Margin = new Padding(0, 0, 0, 8);
        _chooseFolder.UseVisualStyleBackColor = true;
        _chooseFolder.TabIndex = 0;
        _chooseFolder.Click += (_, _) => _sessionSpec?.ChooseFolder();

        _presetGroup.Text = "How Claude starts";
        _presetGroup.AutoSize = true;
        _presetGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _presetGroup.Margin = new Padding(0, 0, 0, 8);
        _presetGroup.TabIndex = 1;
        _presetList.FlowDirection = FlowDirection.TopDown;
        _presetList.WrapContents = false;
        _presetList.AutoSize = true;
        _presetList.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _presetList.Location = new Point(8, 20);
        _presetList.Padding = new Padding(0, 0, 8, 4);
        _presetGroup.Controls.Add(_presetList);

        _customRow.FlowDirection = FlowDirection.TopDown;
        _customRow.WrapContents = false;
        _customRow.AutoSize = true;
        _customRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _customRow.Margin = new Padding(0);
        _customRow.TabIndex = 2;
        _customLabel.AutoSize = true;
        _customLabel.Text = "Arguments for Claude Code, one or more on each line. Ctrl+Enter starts the session.";
        _customLabel.Margin = new Padding(0, 0, 0, 4);
        // Enter is a new line in this field; Ctrl+Enter presses the default button, as in the message field.
        _custom.Multiline = true;
        _custom.AcceptsReturn = true;
        _custom.WordWrap = true;
        _custom.ScrollBars = ScrollBars.Vertical;
        _custom.AccessibleName = "Custom arguments";
        _custom.Margin = new Padding(0);
        _custom.KeyDown += OnCustomKeyDown;
        _customRow.Controls.Add(_customLabel);
        _customRow.Controls.Add(_custom);
        _session.Controls.AddRange([_chooseFolder, _presetGroup, _customRow]);

        _buttons.Dock = DockStyle.Bottom;
        _buttons.FlowDirection = FlowDirection.LeftToRight;
        _buttons.WrapContents = true;
        _buttons.AutoSize = true;
        _buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _buttons.Padding = new Padding(0, 6, 0, 0);
        _buttons.TabIndex = 4;

        // Docked controls are laid out from the last in the collection to the first: the buttons take the bottom,
        // the title the top, the field the strip under the title, then the text takes what is left, or, in a New
        // session notice, a strip of a few lines with the session controls filling the rest under it.
        Controls.AddRange([_session, _text, _inputRow, _title, _buttons]);
    }

    /// <summary>A button was pressed. The window closes the notice (unless the choice stays open) and runs its action.</summary>
    public event Action<OverlayChoice>? Chosen;

    /// <summary>The button Enter presses: the window's AcceptButton while the notice shows. Null when there is none.</summary>
    public IButtonControl? DefaultButton { get; private set; }

    /// <summary>The button Escape presses: the window's CancelButton while the notice shows. Null when there is none.</summary>
    public IButtonControl? CancelButton { get; private set; }

    /// <summary>The text field's current text.</summary>
    public string InputText => _input.Text;

    /// <summary>The folder shown in the New session notice; null while none is chosen.</summary>
    public string? SessionFolder => _sessionFolder;

    /// <summary>The preset selected in the New session notice; null when Custom is selected.</summary>
    public OverlayPreset? SelectedPreset
    {
        get
        {
            var index = _presets.FindIndex(radio => radio.Checked);
            return _sessionSpec is { } session && index >= 0 && index < session.Presets.Count ? session.Presets[index] : null;
        }
    }

    /// <summary>The text of the Custom field in the New session notice.</summary>
    public string CustomArguments => _custom.Text;

    /// <summary>The font of the text, kept in step with the conversation's by the window.</summary>
    public void SetTextFont(Font font)
    {
        _text.Font = font;
        if (_sessionSpec is not null)
        {
            _text.Height = TextStripHeight();
        }
    }

    /// <summary>Fills the panel with a notice. The text box carries the title as its accessible name, so a screen reader hears the title and then the first line when the focus lands on it.</summary>
    public void Populate(string title, string text, IReadOnlyList<OverlayChoice> choices, OverlayInput? input, OverlaySession? session = null)
    {
        _title.Text = title;
        _text.AccessibleName = title;
        _body = text;
        _inputSpec = input;
        _sessionSpec = session;
        _session.Visible = session is not null;
        if (session is not null)
        {
            _text.Dock = DockStyle.Top;
            _text.Height = TextStripHeight();
            FillSession(session);
        }
        else
        {
            _text.Dock = DockStyle.Fill;
        }

        SetText();
        _inputRow.Visible = input is not null;
        if (input is not null)
        {
            _inputLabel.Text = input.Label;
            _input.AccessibleName = input.AccessibleName;
            _input.Text = input.Initial;
            _input.SelectAll();
            LayoutInput();
        }

        var old = _buttons.Controls.Cast<Control>().ToList();
        _buttons.Controls.Clear();
        foreach (var control in old)
        {
            control.Dispose();
        }

        DefaultButton = null;
        CancelButton = null;
        foreach (var choice in choices)
        {
            var button = new Button
            {
                Text = choice.Text,
                AutoSize = true,
                Height = 32,
                Margin = new Padding(0, 0, 8, 0),
                UseVisualStyleBackColor = true,
            };
            button.Click += (_, _) => Choose(choice);
            _buttons.Controls.Add(button);
            if (choice.IsDefault)
            {
                DefaultButton = button;
            }

            if (choice.IsCancel)
            {
                CancelButton = button;
            }
        }
    }

    /// <summary>The folder chosen through the Choose folder button: the first line of the text changes and is spoken.</summary>
    public void SetSessionFolder(string folder)
    {
        _sessionFolder = folder;
        SetText();
        Announce("Folder: " + folder, true);
    }

    /// <summary>Puts the focus where reading starts: in the text field when there is one, otherwise at the top of the text.</summary>
    public void FocusStart()
    {
        if (_inputSpec is not null)
        {
            _input.Select();
            return;
        }

        _text.Select(0, 0);
        _text.Select();
    }

    /// <summary>Speaks through a UI Automation notification raised on the notice, which holds the focus while it shows.</summary>
    public bool Announce(string text, bool interrupt)
    {
        Control source = _inputSpec is not null ? _input : _text;
        if (!source.IsHandleCreated)
        {
            return false;
        }

        var processing = interrupt ? AutomationNotificationProcessing.MostRecent : AutomationNotificationProcessing.All;
        return source.AccessibilityObject.RaiseAutomationNotification(AutomationNotificationKind.ActionCompleted, processing, text);
    }

    private void Choose(OverlayChoice choice)
    {
        if (choice.IsDefault && _inputSpec is { } spec && _input.Text.Length == 0)
        {
            // Nothing to act on yet (Find with an empty field): say so and stay.
            Announce(spec.EmptyMessage, true);
            _input.Select();
            return;
        }

        if (choice.IsDefault && _sessionSpec is not null && _sessionFolder is null)
        {
            Announce(ChooseFolderFirst, true);
            _chooseFolder.Select();
            return;
        }

        Chosen?.Invoke(choice);
    }

    private void SetText()
    {
        var body = _body.Replace("\r\n", "\n").Replace("\n", "\r\n");
        _text.Text = _sessionSpec is null
            ? body
            : "Folder: " + (_sessionFolder ?? NoFolder) + (body.Length > 0 ? "\r\n" + body : string.Empty);
        _text.Visible = _text.Text.Length > 0;
    }

    private void FillSession(OverlaySession session)
    {
        _sessionFolder = session.Folder;
        foreach (var old in _presets)
        {
            _presetList.Controls.Remove(old);
            old.Dispose();
        }

        _presets.Clear();
        for (var i = 0; i <= session.Presets.Count; i++)
        {
            var custom = i == session.Presets.Count;
            var radio = new RadioButton
            {
                Text = custom ? "Custom" : session.Presets[i].Text,
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 2),
                TabIndex = i,
                UseVisualStyleBackColor = true,
            };
            if (custom)
            {
                radio.CheckedChanged += (_, _) => _customRow.Visible = radio.Checked;
            }

            _presets.Add(radio);
            _presetList.Controls.Add(radio);
        }

        _custom.Text = session.CustomArguments.Replace("\r\n", "\n").Replace("\n", "\r\n");
        var selected = Math.Clamp(session.Selected, 0, session.Presets.Count);
        _customRow.Visible = selected == session.Presets.Count;
        _presets[selected].Checked = true;
        LayoutSession();
    }

    private void OnCustomKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.Control)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            DefaultButton?.PerformClick();
        }
    }

    private int TextStripHeight() => _text.Font.Height * 4 + 10;

    private void LayoutInput()
    {
        _input.Left = _inputLabel.Right + 6;
        _input.Width = Math.Max(100, _inputRow.ClientSize.Width - _input.Left);
    }

    private void LayoutSession()
    {
        var width = Math.Max(200, _session.ClientSize.Width - 24);
        _customLabel.MaximumSize = new Size(width, 0);
        _custom.Width = width;
        _custom.Height = _custom.Font.Height * 4 + 8;
    }
}

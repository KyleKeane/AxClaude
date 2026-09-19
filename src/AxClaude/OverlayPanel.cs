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

/// <summary>
/// The window's own dialogs, drawn inside the window (D23). A notice, a question or a help text takes the place of the
/// conversation and the message field until it is answered, so there is never a second window to lose. The panel
/// holds a title, a read-only text, an optional text field and a row of buttons. The window hides the controls
/// underneath, blocks its own shortcuts and the menu, and routes Enter and Escape to the default and cancel buttons
/// through its AcceptButton and CancelButton; Tab moves between the text, the field and the buttons.
/// </summary>
internal sealed class OverlayPanel : Panel
{
    private readonly Label _title = new();
    private readonly TextBox _text = new();
    private readonly Panel _inputRow = new();
    private readonly Label _inputLabel = new();
    private readonly TextBox _input = new();
    private readonly FlowLayoutPanel _buttons = new();
    private OverlayInput? _inputSpec;

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

        _buttons.Dock = DockStyle.Bottom;
        _buttons.FlowDirection = FlowDirection.LeftToRight;
        _buttons.WrapContents = true;
        _buttons.AutoSize = true;
        _buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _buttons.Padding = new Padding(0, 6, 0, 0);
        _buttons.TabIndex = 3;

        // Docked controls are laid out from the last in the collection to the first: the buttons take the bottom,
        // the title the top, the field the strip under the title, and the text what is left.
        Controls.AddRange([_text, _inputRow, _title, _buttons]);
    }

    /// <summary>A button was pressed. The window closes the notice (unless the choice stays open) and runs its action.</summary>
    public event Action<OverlayChoice>? Chosen;

    /// <summary>The button Enter presses: the window's AcceptButton while the notice shows. Null when there is none.</summary>
    public IButtonControl? DefaultButton { get; private set; }

    /// <summary>The button Escape presses: the window's CancelButton while the notice shows. Null when there is none.</summary>
    public IButtonControl? CancelButton { get; private set; }

    /// <summary>The text field's current text.</summary>
    public string InputText => _input.Text;

    /// <summary>The font of the text, kept in step with the conversation's by the window.</summary>
    public void SetTextFont(Font font) => _text.Font = font;

    /// <summary>Fills the panel with a notice. The text box carries the title as its accessible name, so a screen reader hears the title and then the first line when the focus lands on it.</summary>
    public void Populate(string title, string text, IReadOnlyList<OverlayChoice> choices, OverlayInput? input)
    {
        _title.Text = title;
        _text.AccessibleName = title;
        _text.Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
        _text.Visible = text.Length > 0;
        _inputSpec = input;
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

        Chosen?.Invoke(choice);
    }

    private void LayoutInput()
    {
        _input.Left = _inputLabel.Right + 6;
        _input.Width = Math.Max(100, _inputRow.ClientSize.Width - _input.Left);
    }
}

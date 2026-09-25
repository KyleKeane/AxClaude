namespace AxClaude;

/// <summary>
/// The control area (D33): the message field's place at the bottom of the window, which holds one control in the
/// field's stead while Claude waits on something: a list (a question's answers, with check boxes when it takes several;
/// /config's settings; a screen's lines and keys) or a one-line field for the user's own words. Every question gets a
/// new control, so the focus lands on a new control and NVDA reads its name, the question, before the item, as it
/// does whenever the focus comes back from the conversation. Enter chooses and Escape goes back; Tab and Shift+Tab
/// keep the meaning they have in the message field (MainForm). A line over the control shows the name on screen.
/// For a moment after a control appears, and after an answer goes, Enter, Space, Escape and typed keys do nothing:
/// they were meant for the message field, or the answer is on its way.
/// </summary>
internal sealed class ControlArea : Panel
{
    private const int HoldMs = 1000;

    private readonly Label _name = new();
    private long _holdUntil;

    public ControlArea()
    {
        Visible = false;
        Dock = DockStyle.Bottom;
        TabStop = false;
        _name.Dock = DockStyle.Top;
        _name.AutoEllipsis = true;
        _name.UseMnemonic = false;
        Controls.Add(_name);
    }

    /// <summary>The control shown, which takes the focus; null while the message field is in its place.</summary>
    public Control? Current { get; private set; }

    /// <summary>What the focus lands on: the list's focused item, or nothing for the field.</summary>
    public string CurrentItem => Current is ListView { FocusedItem: { } item } ? item.Text : string.Empty;

    /// <summary>The height of the message field, which the area never goes below.</summary>
    [System.ComponentModel.DefaultValue(60)]
    public int MinimumHeight { get; set; } = 60;

    /// <summary>
    /// A list named <paramref name="name"/>, starting on item <paramref name="start"/>. With <paramref name="ticked"/>
    /// it has check boxes, those items ticked. Enter calls <paramref name="choose"/> with the focused item and the
    /// ticked ones, Escape calls <paramref name="escape"/>. The list takes the focus when <paramref name="focus"/>;
    /// <paramref name="hold"/> makes it ignore the answering keys for a moment.
    /// </summary>
    public void ShowList(string name, IReadOnlyList<string> items, int start, IReadOnlyList<int>? ticked, bool focus, bool hold,
        Action<int, IReadOnlyList<int>> choose, Action escape)
    {
        var list = new ListView
        {
            View = View.Details,
            HeaderStyle = ColumnHeaderStyle.None,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            CheckBoxes = ticked is not null,
            AccessibleName = name,
            Dock = DockStyle.Fill,
        };
        list.Columns.Add(string.Empty);
        for (var i = 0; i < items.Count; i++)
        {
            list.Items.Add(new ListViewItem(items[i]) { Checked = ticked?.Contains(i) == true });
        }

        // One column as wide as the list; while the list is being disposed it has none.
        list.Resize += (_, _) =>
        {
            if (list.Columns.Count > 0)
            {
                list.Columns[0].Width = list.ClientSize.Width;
            }
        };
        list.KeyDown += (_, e) =>
        {
            if (Answering(e) && e.KeyCode == Keys.Enter && list.FocusedItem is { } item)
            {
                choose(item.Index, list.CheckedIndices.Cast<int>().ToList());
            }
            else if (Answering(e) && e.KeyCode == Keys.Escape)
            {
                escape();
            }
        };
        var old = Place(list, name);
        list.Columns[0].Width = list.ClientSize.Width;
        if (items.Count > 0)
        {
            // Only once the list has its window: before, the focused item is not kept.
            var first = list.Items[Math.Clamp(start, 0, items.Count - 1)];
            first.Selected = first.Focused = true;
            first.EnsureVisible();
        }

        Finish(list, old, focus, hold);
    }

    /// <summary>A one-line field named <paramref name="name"/>: Enter calls <paramref name="enter"/> with its text, Escape <paramref name="escape"/>.</summary>
    public void ShowField(string name, bool focus, bool hold, Action<string> enter, Action escape)
    {
        var field = new TextBox { AccessibleName = name, Dock = DockStyle.Top };
        field.KeyDown += (_, e) =>
        {
            if (Answering(e) && e.KeyCode == Keys.Enter)
            {
                enter(field.Text);
            }
            else if (Answering(e) && e.KeyCode == Keys.Escape)
            {
                escape();
            }
        };
        Finish(field, Place(field, name), focus, hold);
    }

    /// <summary>The area empties and hides; the window has put the message field back.</summary>
    public void Clear()
    {
        Current?.Dispose();
        Current = null;
        Visible = false;
    }

    /// <summary>The answering keys do nothing for this long: an answer is on its way, and a second Enter would send it twice.</summary>
    public void Hold(int milliseconds) => _holdUntil = Environment.TickCount64 + milliseconds;

    /// <summary>The height for the rows the control shows, from the message field's height up to half the window.</summary>
    public void FitHeight()
    {
        if (Current is null || Parent is null)
        {
            return;
        }

        var row = TextRenderer.MeasureText("Wg", Font).Height + 6;
        var rows = Current is ListView list ? list.Items.Count : 1;
        _name.Height = row;
        Height = Math.Clamp(row * (rows + 1) + 8, MinimumHeight, Math.Max(MinimumHeight, Parent.ClientSize.Height / 2));
    }

    /// <summary>
    /// Enter, Escape, Space and typed keys are the control's own (and are kept from the edit control or the list) unless
    /// the area holds them: then they do nothing. True when the key may act.
    /// </summary>
    private bool Answering(KeyEventArgs e)
    {
        var answering = e.KeyCode is Keys.Enter or Keys.Escape && e.Modifiers == Keys.None;
        if (Environment.TickCount64 < _holdUntil && (answering || e.KeyCode == Keys.Space || (e.Modifiers == Keys.None && e.KeyCode is >= Keys.A and <= Keys.Z or >= Keys.D0 and <= Keys.D9)))
        {
            e.Handled = e.SuppressKeyPress = true;
            return false;
        }

        if (answering)
        {
            e.Handled = e.SuppressKeyPress = true;
        }

        return answering;
    }

    /// <summary>Puts the new control over the old one, with its window created; returns the old one.</summary>
    private Control? Place(Control control, string name)
    {
        // Enter and Escape reach the control's KeyDown rather than the window's default and cancel buttons.
        control.PreviewKeyDown += (_, e) => e.IsInputKey |= e.KeyCode is Keys.Enter or Keys.Escape;
        var old = Current;
        _name.Text = name;
        Visible = true;
        Controls.Add(control);
        control.BringToFront();
        // Its window now, even while the area is hidden behind a notice: a list keeps its focused item only then.
        _ = control.Handle;
        Current = control;
        FitHeight();
        return old;
    }

    /// <summary>The new control takes the focus before the old one goes, so the focus never passes through the conversation on the way.</summary>
    private void Finish(Control control, Control? old, bool focus, bool hold)
    {
        _holdUntil = hold ? Environment.TickCount64 + HoldMs : 0;
        if (focus)
        {
            control.Focus();
        }

        old?.Dispose();
    }
}

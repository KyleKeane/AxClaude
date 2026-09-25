namespace AxClaude;

/// <summary>
/// The keys every list of the control area adds to a list box's own (D33). A number jumps forward through the items
/// it starts (the list's own type-ahead: 1, 10, 11); Shift and the number go back. 0, which starts no answer, goes
/// through the single-digit answers (1 to 9), Shift+0 back. Page Up and Page Down move ten items. Home and End are the
/// list box's own: the first and the last item; Ctrl+Home and Ctrl+End do the same.
/// </summary>
internal static class ListKeys
{
    private const int PageItems = 10;

    /// <summary>Handles <paramref name="e"/> when it is one of these keys; true when it did.</summary>
    public static bool Handle(ListBox list, KeyEventArgs e)
    {
        var count = list.Items.Count;
        if (count == 0)
        {
            return false;
        }

        if (e.KeyCode is >= Keys.D0 and <= Keys.D9 && e.Modifiers is Keys.None or Keys.Shift)
        {
            var digit = (char)('0' + (e.KeyCode - Keys.D0));
            var back = e.Modifiers == Keys.Shift;
            if (digit == '0')
            {
                Jump(list, back, SingleDigit);
            }
            else if (back)
            {
                Jump(list, back: true, item => item.StartsWith(digit));
            }
            else
            {
                // A digit without Shift stays the list's own type-ahead.
                return false;
            }

            e.Handled = e.SuppressKeyPress = true;
            return true;
        }

        if (e.KeyCode is Keys.PageDown or Keys.PageUp && e.Modifiers == Keys.None)
        {
            var step = e.KeyCode == Keys.PageDown ? PageItems : -PageItems;
            list.SelectedIndex = Math.Clamp(list.SelectedIndex + step, 0, count - 1);
            e.Handled = e.SuppressKeyPress = true;
            return true;
        }

        if (e.KeyCode is Keys.Home or Keys.End && e.Modifiers == Keys.Control)
        {
            // As Home and End, which the list box does itself; with Control it may only move its focus rectangle.
            list.SelectedIndex = e.KeyCode == Keys.Home ? 0 : count - 1;
            e.Handled = e.SuppressKeyPress = true;
            return true;
        }

        return false;
    }

    // "3. Blue", not "12. Model" or "y. Yes".
    private static bool SingleDigit(string item) => item.Length > 1 && char.IsAsciiDigit(item[0]) && !char.IsAsciiDigit(item[1]);

    /// <summary>Selects the nearest item after (or before) the selected one that matches, wrapping round the ends.</summary>
    private static void Jump(ListBox list, bool back, Func<string, bool> matches)
    {
        var count = list.Items.Count;
        for (var step = 1; step <= count; step++)
        {
            var i = ((list.SelectedIndex + (back ? -step : step)) % count + count) % count;
            if (list.Items[i] is string item && matches(item))
            {
                list.SelectedIndex = i;
                return;
            }
        }
    }
}

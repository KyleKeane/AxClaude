using System.Runtime.InteropServices;

namespace AxClaude;

/// <summary>
/// Page Up and Page Down for the conversation and the message field (FR-2.1, FR-3 key table). The caret moves by one
/// screen of wrapped rows, counted from the caret rather than from whatever is scrolled into view, and lands on the
/// first or last row when less than a screen is left; it goes to the start of its new row, and the view scrolls with
/// it so that the row keeps its place on screen. The edit control's own keys scroll the view by a page and keep the
/// caret on the same screen row instead, which strands the caret when its row is off screen, never reaches the first
/// or last row, and does nothing at all while the text fits the control. The screen reader reads the new row itself;
/// nothing is announced here.
/// </summary>
internal static class EditPaging
{
    private const int EM_LINESCROLL = 0x00B6;
    private const int EM_SCROLLCARET = 0x00B7;
    private const int EM_GETLINECOUNT = 0x00BA;
    private const int EM_GETFIRSTVISIBLELINE = 0x00CE;
    private const int EM_CHARFROMPOS = 0x00D7;

    /// <summary>Moves the caret one screen of rows down (<paramref name="direction"/> 1) or up (-1). False when it was on the last (first) row already and stays.</summary>
    public static bool Page(TextBoxBase box, int direction)
    {
        var handle = box.Handle;
        var lineCount = (int)SendMessage(handle, EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero);
        var first = (int)SendMessage(handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
        // The row at the bottom edge of the control, or the last row of the text when that ends higher up. The row
        // is the high word of the reply; the low word's character index is only good below 64K characters.
        var bottomPoint = (IntPtr)((Math.Max(0, box.ClientSize.Height - 1) << 16) | 1);
        var atBottom = (int)SendMessage(handle, EM_CHARFROMPOS, IntPtr.Zero, bottomPoint);
        var last = atBottom == -1 ? first : (atBottom >> 16) & 0xFFFF;
        var page = Math.Max(1, last - first);
        var caretRow = box.GetLineFromCharIndex(box.SelectionStart);
        var target = Math.Clamp(caretRow + direction * page, 0, Math.Max(0, lineCount - 1));
        if (target == caretRow)
        {
            return false;
        }

        box.Select(box.GetFirstCharIndexFromLine(target), 0);
        // Scroll so that the row keeps its place on screen, or to the top when it was scrolled out of view. The scroll
        // is explicit because the control's own scroll-to-caret is ignored while it does not have the focus.
        var offset = caretRow >= first && caretRow <= last ? caretRow - first : 0;
        var now = (int)SendMessage(handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
        SendMessage(handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)(Math.Max(0, target - offset) - now));
        SendMessage(handle, EM_SCROLLCARET, IntPtr.Zero, IntPtr.Zero);
        return true;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}

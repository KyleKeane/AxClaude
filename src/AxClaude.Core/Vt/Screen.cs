using System.Text;
using AxClaude.Core.Transcript;

namespace AxClaude.Core.Vt;

/// <summary>Where the screen puts the lines it creates, commits (scrolled off the top) or discards.</summary>
public interface ILineStore
{
    Line CreateLine(Line? before);
    void Commit(Line line);
    void Remove(Line line);
}

internal sealed class Row(int columns)
{
    /// <summary>Marks the second cell of a two-cell wide character.</summary>
    public const string Continuation = "\0";

    public readonly string?[] Cells = new string?[columns];
    public Line? Line;
    public bool Dirty;

    public string Text()
    {
        var sb = new StringBuilder(Cells.Length);
        foreach (var cell in Cells)
        {
            if (cell is null)
            {
                sb.Append(' ');
            }
            else if (cell != Continuation)
            {
                sb.Append(cell);
            }
        }

        return sb.ToString().TrimEnd();
    }

    public void Clear(int from, int toExclusive)
    {
        for (var i = Math.Max(0, from); i < Math.Min(Cells.Length, toExclusive); i++)
        {
            Cells[i] = null;
        }

        Dirty = true;
    }
}

/// <summary>
/// A headless terminal screen: a grid of cells, a cursor and a scroll region, driven by <see cref="VtParser"/>.
/// Rows carry a stable line identity so that in-place rewrites update existing transcript lines.
/// </summary>
public sealed class Screen : IVtSink
{
    // One shared string per printable ASCII character: a repaint prints thousands of cells per frame.
    private static readonly string[] Ascii = Enumerable.Range(0, 128).Select(c => ((char)c).ToString()).ToArray();

    private readonly ILineStore _store;
    private readonly Row[] _rows;
    private int _cx;
    private int _cy;
    private int _savedX;
    private int _savedY;
    private int _top;
    private int _bottom;
    private bool _pendingWrap;

    public Screen(int columns, int rows, ILineStore store)
    {
        if (columns < 2 || rows < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(columns), "The screen needs at least 2 columns and 2 rows.");
        }

        Columns = columns;
        Rows = rows;
        _store = store;
        _rows = new Row[rows];
        for (var i = 0; i < rows; i++)
        {
            _rows[i] = new Row(columns);
        }

        _bottom = rows - 1;
    }

    public int Columns { get; }
    public int Rows { get; }
    public int CursorRow => _cy;

    /// <summary>The cursor was shown again (<c>ESC[?25h</c>): the frame is complete.</summary>
    public event Action? FrameEnd;
    public event Action? Bell;
    public event Action<string>? TitleChanged;

    /// <summary>
    /// The cursor was put in the top left corner (<c>ESC[H</c>). With the screen not cleared since the last frame and
    /// conversation on it, this is Claude repainting everything from the top (SessionModel).
    /// </summary>
    public event Action? Homed;

    /// <summary>The whole screen was erased (<c>ESC[2J</c>, <c>ESC[3J</c>) since the last frame ended.</summary>
    public bool ClearedSinceFrameEnd { get; private set; }

    internal Row RowAt(int row) => _rows[row];

    public Line? LineAt(int row) => _rows[row].Line;

    // ---- IVtSink ----

    public void Print(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            var width = Wcwidth.Of(rune.Value);
            var s = rune.Value < 128 ? Ascii[rune.Value] : rune.ToString();
            if (width == 0)
            {
                AttachCombining(s);
                continue;
            }

            if (_pendingWrap)
            {
                _pendingWrap = false;
                _cx = 0;
                LineFeed();
            }

            if (width == 2 && _cx == Columns - 1)
            {
                ClearCell(_rows[_cy], _cx);
                _rows[_cy].Dirty = true;
                _cx = 0;
                LineFeed();
            }

            EnsureLine(_cy);
            var row = _rows[_cy];
            ClearCell(row, _cx);
            row.Cells[_cx] = s;
            if (width == 2)
            {
                ClearCell(row, _cx + 1);
                row.Cells[_cx + 1] = Row.Continuation;
            }

            row.Dirty = true;
            _cx += width;
            if (_cx >= Columns)
            {
                _cx = Columns - 1;
                _pendingWrap = true;
            }
        }
    }

    public void Control(char c)
    {
        switch (c)
        {
            case '\a':
                Bell?.Invoke();
                break;
            case '\b':
                if (_cx > 0)
                {
                    _cx--;
                }

                _pendingWrap = false;
                break;
            case '\t':
                _cx = Math.Min(Columns - 1, (_cx / 8 + 1) * 8);
                _pendingWrap = false;
                break;
            case '\n':
            case '\v':
            case '\f':
                LineFeed();
                break;
            case '\r':
                _cx = 0;
                _pendingWrap = false;
                break;
        }
    }

    public void Escape(char final, string intermediates)
    {
        if (intermediates.Length > 0)
        {
            return; // charset designations and similar
        }

        switch (final)
        {
            case '7':
                SaveCursor();
                break;
            case '8':
                RestoreCursor();
                break;
            case 'D':
                LineFeed();
                break;
            case 'E':
                _cx = 0;
                LineFeed();
                break;
            case 'M':
                ReverseIndex();
                break;
            case 'c':
                Reset();
                break;
        }
    }

    public void Csi(string parameters, string intermediates, char final)
    {
        if (intermediates.Length > 0)
        {
            if (final == 'p' && intermediates == "!")
            {
                Reset();
            }

            return;
        }

        var isPrivate = parameters.Length > 0 && parameters[0] is '?' or '>' or '<' or '=';
        var values = ParseParameters(isPrivate ? parameters.AsSpan(1) : parameters.AsSpan());
        if (isPrivate)
        {
            // Only the cursor visibility mode matters: ESC[?25h ends a frame. Every other private mode is ignored.
            if (parameters[0] == '?' && final == 'h' && values.Contains(25))
            {
                FrameEnd?.Invoke();
                ClearedSinceFrameEnd = false;
            }

            return;
        }

        int P(int index, int fallback) => index < values.Count && values[index] > 0 ? values[index] : fallback;
        var first = values[0]; // ParseParameters always yields at least one value; 0 is the default of J and K.

        switch (final)
        {
            case 'A':
                MoveCursor(_cx, _cy - P(0, 1));
                break;
            case 'B':
                MoveCursor(_cx, _cy + P(0, 1));
                break;
            case 'C':
                MoveCursor(_cx + P(0, 1), _cy);
                break;
            case 'D':
                MoveCursor(_cx - P(0, 1), _cy);
                break;
            case 'E':
                MoveCursor(0, _cy + P(0, 1));
                break;
            case 'F':
                MoveCursor(0, _cy - P(0, 1));
                break;
            case 'G':
            case '`':
                MoveCursor(P(0, 1) - 1, _cy);
                break;
            case 'H':
            case 'f':
                MoveCursor(P(1, 1) - 1, P(0, 1) - 1);
                if (_cx == 0 && _cy == 0)
                {
                    Homed?.Invoke();
                }

                break;
            case 'd':
                MoveCursor(_cx, P(0, 1) - 1);
                break;
            case 'J':
                EraseDisplay(first);
                break;
            case 'K':
                EraseLine(first);
                break;
            case 'X':
                _rows[_cy].Clear(_cx, _cx + P(0, 1));
                break;
            case '@':
                InsertCells(P(0, 1));
                break;
            case 'P':
                DeleteCells(P(0, 1));
                break;
            case 'L':
                InsertLines(P(0, 1));
                break;
            case 'M':
                DeleteLines(P(0, 1));
                break;
            case 'S':
                ScrollUp(P(0, 1));
                break;
            case 'T':
                ScrollDown(P(0, 1));
                break;
            case 'r':
                SetScrollRegion(P(0, 1), P(1, Rows));
                break;
            case 's':
                SaveCursor();
                break;
            case 'u':
                RestoreCursor();
                break;
        }
    }

    public void Osc(string content)
    {
        var separator = content.IndexOf(';');
        var code = separator < 0 ? content : content[..separator];
        var payload = separator < 0 ? string.Empty : content[(separator + 1)..];
        if (code is "0" or "2")
        {
            TitleChanged?.Invoke(payload);
        }
    }

    // ---- screen operations ----

    private static List<int> ParseParameters(ReadOnlySpan<char> text)
    {
        var values = new List<int>(4);
        var current = 0;
        var any = false;
        foreach (var c in text)
        {
            if (c >= '0' && c <= '9')
            {
                current = Math.Min(65535, current * 10 + (c - '0'));
                any = true;
            }
            else if (c == ';')
            {
                values.Add(any ? current : 0);
                current = 0;
                any = false;
            }
            // ':' and other characters are ignored
        }

        values.Add(any ? current : 0);
        return values;
    }

    private void MoveCursor(int x, int y)
    {
        _cx = Math.Clamp(x, 0, Columns - 1);
        _cy = Math.Clamp(y, 0, Rows - 1);
        _pendingWrap = false;
    }

    private void SaveCursor()
    {
        _savedX = _cx;
        _savedY = _cy;
    }

    private void RestoreCursor() => MoveCursor(_savedX, _savedY);

    private void LineFeed()
    {
        _pendingWrap = false;
        if (_cy == _bottom)
        {
            ScrollUp(1);
        }
        else if (_cy < Rows - 1)
        {
            _cy++;
        }
    }

    private void ReverseIndex()
    {
        _pendingWrap = false;
        if (_cy == _top)
        {
            ScrollDown(1);
        }
        else if (_cy > 0)
        {
            _cy--;
        }
    }

    private void Reset()
    {
        foreach (var row in _rows)
        {
            row.Clear(0, Columns);
        }

        _top = 0;
        _bottom = Rows - 1;
        MoveCursor(0, 0);
    }

    private void SetScrollRegion(int top, int bottom)
    {
        top = Math.Clamp(top, 1, Rows);
        bottom = Math.Clamp(bottom, 1, Rows);
        if (top < bottom)
        {
            _top = top - 1;
            _bottom = bottom - 1;
        }

        MoveCursor(0, 0);
    }

    private void EraseDisplay(int mode)
    {
        switch (mode)
        {
            case 0:
                _rows[_cy].Clear(_cx, Columns);
                for (var r = _cy + 1; r < Rows; r++)
                {
                    _rows[r].Clear(0, Columns);
                }

                break;
            case 1:
                for (var r = 0; r < _cy; r++)
                {
                    _rows[r].Clear(0, Columns);
                }

                _rows[_cy].Clear(0, _cx + 1);
                break;
            default:
                foreach (var row in _rows)
                {
                    row.Clear(0, Columns);
                }

                ClearedSinceFrameEnd = true;
                break;
        }
    }

    /// <summary>
    /// Lets go of every row's line, as if the rows had scrolled away: the lines stay in the transcript with the text
    /// they had at the last frame end, not what was written on their rows since, and what is printed on the rows
    /// next gets lines of its own.
    /// </summary>
    public void DetachRows()
    {
        foreach (var row in _rows)
        {
            if (row.Line is not { } line)
            {
                continue;
            }

            row.Dirty = false;
            _store.Commit(line);
            line.Row = null;
            row.Line = null;
        }
    }

    private void EraseLine(int mode)
    {
        var row = _rows[_cy];
        switch (mode)
        {
            case 0:
                row.Clear(_cx, Columns);
                break;
            case 1:
                row.Clear(0, _cx + 1);
                break;
            default:
                row.Clear(0, Columns);
                break;
        }
    }

    private void InsertCells(int count)
    {
        var row = _rows[_cy];
        count = Math.Min(count, Columns - _cx);
        for (var x = Columns - 1; x >= _cx + count; x--)
        {
            row.Cells[x] = row.Cells[x - count];
        }

        row.Clear(_cx, _cx + count);
    }

    private void DeleteCells(int count)
    {
        var row = _rows[_cy];
        count = Math.Min(count, Columns - _cx);
        for (var x = _cx; x < Columns - count; x++)
        {
            row.Cells[x] = row.Cells[x + count];
        }

        row.Clear(Columns - count, Columns);
    }

    private void InsertLines(int count)
    {
        if (_cy < _top || _cy > _bottom)
        {
            return;
        }

        count = Math.Min(count, _bottom - _cy + 1);
        for (var k = 0; k < count; k++)
        {
            DiscardRow(_bottom);
            for (var r = _bottom; r > _cy; r--)
            {
                _rows[r] = _rows[r - 1];
            }

            _rows[_cy] = new Row(Columns);
        }

        _cx = 0;
        _pendingWrap = false;
    }

    private void DeleteLines(int count)
    {
        if (_cy < _top || _cy > _bottom)
        {
            return;
        }

        count = Math.Min(count, _bottom - _cy + 1);
        for (var k = 0; k < count; k++)
        {
            DiscardRow(_cy);
            for (var r = _cy; r < _bottom; r++)
            {
                _rows[r] = _rows[r + 1];
            }

            _rows[_bottom] = new Row(Columns);
        }

        _cx = 0;
        _pendingWrap = false;
    }

    private void ScrollUp(int count)
    {
        for (var k = 0; k < count; k++)
        {
            var top = _rows[_top];
            if (top.Line is { } line)
            {
                if (_top == 0)
                {
                    // The row may have been written and scrolled away inside one frame.
                    if (top.Dirty)
                    {
                        line.Text = top.Text();
                        top.Dirty = false;
                    }

                    _store.Commit(line);
                }
                else
                {
                    _store.Remove(line);
                }

                line.Row = null;
                top.Line = null;
            }

            for (var r = _top; r < _bottom; r++)
            {
                _rows[r] = _rows[r + 1];
            }

            _rows[_bottom] = new Row(Columns);
        }
    }

    private void ScrollDown(int count)
    {
        for (var k = 0; k < count; k++)
        {
            DiscardRow(_bottom);
            for (var r = _bottom; r > _top; r--)
            {
                _rows[r] = _rows[r - 1];
            }

            _rows[_top] = new Row(Columns);
        }
    }

    private void DiscardRow(int index)
    {
        var row = _rows[index];
        if (row.Line is { } line)
        {
            _store.Remove(line);
            line.Row = null;
            row.Line = null;
        }
    }

    /// <summary>Gives the row, and every row above it, a transcript line so interior blank rows keep their place.</summary>
    private void EnsureLine(int index)
    {
        if (_rows[index].Line is not null)
        {
            return;
        }

        for (var i = 0; i <= index; i++)
        {
            if (_rows[i].Line is not null)
            {
                continue;
            }

            Line? before = null;
            for (var j = i + 1; j < Rows; j++)
            {
                if (_rows[j].Line is { } below)
                {
                    before = below;
                    break;
                }
            }

            var line = _store.CreateLine(before);
            line.Row = _rows[i];
            _rows[i].Line = line;
            _rows[i].Dirty = true;
        }
    }

    private void AttachCombining(string s)
    {
        var x = _pendingWrap ? _cx : _cx - 1;
        if (x < 0)
        {
            return;
        }

        var row = _rows[_cy];
        if (row.Cells[x] == Row.Continuation && x > 0)
        {
            x--;
        }

        row.Cells[x] = (row.Cells[x] ?? " ") + s;
        row.Dirty = true;
    }

    private void ClearCell(Row row, int x)
    {
        if (x < 0 || x >= Columns)
        {
            return;
        }

        if (row.Cells[x] == Row.Continuation && x > 0)
        {
            row.Cells[x - 1] = null;
        }

        if (x + 1 < Columns && row.Cells[x + 1] == Row.Continuation)
        {
            row.Cells[x + 1] = null;
        }

        row.Cells[x] = null;
    }
}

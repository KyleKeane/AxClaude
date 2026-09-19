using System.Runtime.InteropServices;
using System.Text;

namespace AxClaude.Core.Transcript;

/// <summary>One replacement in the mirrored text: <see cref="OldLength"/> characters at <see cref="Start"/> become <see cref="Text"/>.</summary>
public readonly record struct MirrorEdit(int Start, int OldLength, string Text);

/// <summary>
/// Keeps the visible transcript lines as one text, the way a multi-line edit control stores it, and turns each change
/// of the line list into the smallest edits that bring that text up to date. Lines are separated by "\r\n", except a
/// line that continues a row Claude wrapped (<see cref="Line.JoinedToPrevious"/>), which follows its predecessor
/// after a space so that the two read as one line. The window applies the edits to its edit control and asks the
/// update where the caret belongs afterwards; nothing here needs a window, so it is unit tested.
/// <para>
/// Reading breaks (FR-3.10): while the reader follows the line Claude is writing, the view marks the end of what has
/// been heard with <see cref="BreakAtEnd"/>; the text that arrives after the mark is rendered on a line of its own,
/// so that the screen reader's Down Arrow reads only what is new. The breaks are rendering only: the line texts,
/// the display line numbers and <see cref="DisplayTextAt"/> never see them, and <see cref="ClearBreaks"/> joins
/// the line up again.
/// </para>
/// </summary>
public sealed class TranscriptMirror
{
    private List<Line> _shown = [];
    private List<Line> _scratch = [];
    private readonly List<string> _text = [];
    private List<int> _starts = [];
    private readonly List<string> _separators = [];
    private readonly List<int> _display = [];
    private readonly List<int> _extra = [];
    private readonly List<ReadingBreak> _breaks = [];
    private int _breakStart = int.MaxValue;

    /// <summary>
    /// A reading break: the reader has heard <see cref="Row"/> up to <see cref="Mark"/>. When the row grows past the
    /// mark in the middle of a word, the break moves in front of the word, but never before <see cref="Floor"/>, the
    /// column the caret was on, so the caret stays on the heard side.
    /// </summary>
    private readonly record struct ReadingBreak(Line Row, int Mark, int Floor);

    /// <summary>Number of transcript lines shown, counting each wrapped fragment.</summary>
    public int LineCount => _shown.Count;

    /// <summary>Number of lines as the reader sees them: joined fragments count once.</summary>
    public int DisplayLineCount { get; private set; }

    /// <summary>Length of the mirrored text.</summary>
    public int Length => _shown.Count == 0 ? 0 : _starts[^1] + _text[^1].Length + _extra[^1];

    /// <summary>True while reading breaks are rendered (FR-3.10).</summary>
    public bool HasBreaks => _breaks.Count > 0;

    public Line LineAt(int index) => _shown[index];

    public string TextAt(int index) => _text[index];

    /// <summary>Character position where a visible line starts.</summary>
    public int StartAt(int index) => _starts[index];

    /// <summary>Index of the visible line that contains the character position (0 when there are no lines).</summary>
    public int LineIndexAt(int position) => LineIndexAt(_starts, position);

    /// <summary>The display line (as the reader counts them) that a visible line belongs to.</summary>
    public int DisplayLineOf(int index) => _display[index];

    /// <summary>The display line that holds the reading breaks, or -1 when there are none or their line is gone.</summary>
    public int BreakDisplayLine
    {
        get
        {
            if (_breaks.Count == 0)
            {
                return -1;
            }

            var index = IndexOf(_breaks[0].Row);
            return index < 0 ? -1 : _display[index];
        }
    }

    /// <summary>
    /// The character position of a column of a visible line's text: the column itself, plus the line breaks that
    /// reading breaks in front of it have added.
    /// </summary>
    public int RenderedColumn(int index, int column)
    {
        column = Math.Min(column, _text[index].Length);
        if (_breaks.Count == 0)
        {
            return column;
        }

        var text = _text[index];
        var rendered = column;
        foreach (var b in _breaks)
        {
            if (ReferenceEquals(b.Row, _shown[index]) && ResolveColumn(b, text) is var c && c > 0 && c < text.Length && c <= column)
            {
                rendered += 2;
            }
        }

        return rendered;
    }

    /// <summary>
    /// Marks the end of the last line as heard (FR-3.10): what Claude appends to it, or joins to it, from now on is
    /// rendered on a line of its own after the next <see cref="Update"/>. <paramref name="caretColumn"/> is the
    /// character position of the caret within the line; the break never moves in front of it.
    /// </summary>
    public void BreakAtEnd(int caretColumn)
    {
        if (_shown.Count == 0)
        {
            return;
        }

        var last = _shown.Count - 1;
        var row = _shown[last];
        var text = _text[last];
        if (text.Length == 0)
        {
            return;
        }

        var floor = LogicalColumn(last, caretColumn);
        for (var i = _breaks.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_breaks[i].Row, row) && ResolveColumn(_breaks[i], text) == text.Length)
            {
                return;
            }
        }

        _breaks.Add(new ReadingBreak(row, text.Length, floor));
    }

    /// <summary>Takes the reading breaks away: the next <see cref="Update"/> joins the line up again.</summary>
    public void ClearBreaks() => _breaks.Clear();

    /// <summary>
    /// The text of the display line that a visible line belongs to, as the reader sees it: a row Claude wrapped and
    /// its continuations joined with their separators.
    /// </summary>
    public string DisplayTextAt(int index)
    {
        var display = _display[index];
        var first = index;
        while (first > 0 && _display[first - 1] == display)
        {
            first--;
        }

        var last = index;
        while (last + 1 < _shown.Count && _display[last + 1] == display)
        {
            last++;
        }

        if (first == last)
        {
            return _text[index];
        }

        var sb = new StringBuilder(_text[first]);
        for (var i = first + 1; i <= last; i++)
        {
            sb.Append(SeparatorBefore(_shown[i])).Append(_text[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// The text that goes between a line and the one before it: a line break, or, for a line that continues a
    /// wrapped row, a single space (nothing when the line already starts with one).
    /// </summary>
    public static string SeparatorBefore(Line line) =>
        !line.JoinedToPrevious ? "\r\n" : line.Text.StartsWith(' ') ? string.Empty : " ";

    /// <summary>The visible lines as one text, the way the view shows them without reading breaks.</summary>
    public static string Render(IEnumerable<Line> lines)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var line in lines)
        {
            if (line.Hidden)
            {
                continue;
            }

            if (!first)
            {
                sb.Append(SeparatorBefore(line));
            }

            sb.Append(line.Text);
            first = false;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Finds the next (or previous) visible line whose text contains <paramref name="text"/>, ignoring case, starting
    /// after (before) <paramref name="from"/> and wrapping around to end with <paramref name="from"/> itself. Returns
    /// the line index or -1; <paramref name="column"/> is where the match starts within the line, and
    /// <paramref name="wrapped"/> tells that the search passed the end (or start) of the transcript.
    /// </summary>
    public int Find(string text, int from, bool backward, out int column, out bool wrapped)
    {
        column = 0;
        wrapped = false;
        var count = _shown.Count;
        if (count == 0 || text.Length == 0)
        {
            return -1;
        }

        var step = backward ? -1 : 1;
        var index = Math.Clamp(from, 0, count - 1);
        for (var k = 0; k < count; k++)
        {
            index += step;
            if (index >= count)
            {
                index = 0;
                wrapped = true;
            }
            else if (index < 0)
            {
                index = count - 1;
                wrapped = true;
            }

            column = _text[index].IndexOf(text, StringComparison.OrdinalIgnoreCase);
            if (column >= 0)
            {
                return index;
            }
        }

        column = 0;
        return -1;
    }

    /// <summary>
    /// Takes the model's lines (hidden ones are skipped) and returns the edits that bring the mirrored text in line
    /// with them, in descending start order so that applying them one after the other needs no offset bookkeeping.
    /// The update is meant to be applied at once: its view of the previous state is reused by the next call.
    /// </summary>
    public MirrorUpdate Update(IReadOnlyList<Line> lines)
    {
        // The list of the previous state is refilled rather than allocated: at 20 000 lines it would go to the
        // large object heap on every frame, most of which change nothing visible.
        var next = _scratch;
        next.Clear();
        foreach (var line in lines)
        {
            if (!line.Hidden)
            {
                next.Add(line);
            }
        }

        // The breaks all sit in one display line near the end: rows in front of it never consult them.
        _breakStart = int.MaxValue;
        if (_breaks.Count > 0)
        {
            PruneBreaks(next);
            if (_breaks.Count > 0)
            {
                _breakStart = IndexOf(next, _breaks[0].Row);
            }
        }

        var max = Math.Min(_shown.Count, next.Count);
        var prefix = 0;
        while (prefix < max && Same(prefix, prefix - 1, next))
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < max - prefix)
        {
            var nextIndex = next.Count - 1 - suffix;
            if (!Same(_shown.Count - 1 - suffix, nextIndex - 1, next))
            {
                break;
            }

            suffix++;
        }

        var edits = new List<MirrorEdit>();
        for (var i = 0; i < prefix; i++)
        {
            if (_text[i] != next[i].Text || _extra[i] != ExtraOf(i, next[i]))
            {
                edits.Add(new MirrorEdit(_starts[i], _text[i].Length + _extra[i], Rendered(next[i])));
            }
        }

        for (var k = 1; k <= suffix; k++)
        {
            var shownIndex = _shown.Count - k;
            var nextIndex = next.Count - k;
            if (_text[shownIndex] != next[nextIndex].Text || _extra[shownIndex] != ExtraOf(nextIndex, next[nextIndex]))
            {
                edits.Add(new MirrorEdit(_starts[shownIndex], _text[shownIndex].Length + _extra[shownIndex], Rendered(next[nextIndex])));
            }
        }

        var shownMiddleEnd = _shown.Count - suffix;
        var nextMiddleEnd = next.Count - suffix;
        if (shownMiddleEnd > prefix || nextMiddleEnd > prefix)
        {
            // The middle is replaced together with the separator in front of it. When nothing precedes it, the first
            // kept line has no separator of its own, so the one after the middle changes hands instead.
            var start = prefix == 0 ? 0 : _starts[prefix - 1] + _text[prefix - 1].Length + _extra[prefix - 1];
            var end = shownMiddleEnd > prefix ? _starts[shownMiddleEnd - 1] + _text[shownMiddleEnd - 1].Length + _extra[shownMiddleEnd - 1] : start;
            if (prefix == 0 && suffix > 0 && shownMiddleEnd > prefix)
            {
                end += _separators[shownMiddleEnd].Length;
            }

            var middle = new StringBuilder();
            if (nextMiddleEnd > prefix)
            {
                for (var i = prefix; i < nextMiddleEnd; i++)
                {
                    if (i > 0)
                    {
                        middle.Append(RenderedSeparator(i - 1, next));
                    }

                    middle.Append(Rendered(next[i]));
                }

                if (prefix == 0 && suffix > 0)
                {
                    middle.Append(RenderedSeparator(nextMiddleEnd - 1, next));
                }
            }

            edits.Add(new MirrorEdit(start, end - start, middle.ToString()));
        }

        if (edits.Count == 0)
        {
            // The same lines with the same texts and separators: nothing to recompute.
            return new MirrorUpdate(edits, _shown, _starts, _shown, _text, _extra, _starts, prefix, suffix);
        }

        edits.Sort((a, b) => b.Start.CompareTo(a.Start));

        // Everything up to the first prefix line whose text changed keeps its text, offset, separator and display
        // line; only the rest is recomputed. The old start offsets stay intact for the caret mapping.
        var stable = 0;
        while (stable < prefix && ReferenceEquals(_text[stable], next[stable].Text) && _extra[stable] == ExtraOf(stable, next[stable]))
        {
            stable++;
        }

        var starts = new List<int>(next.Count);
        starts.AddRange(CollectionsMarshal.AsSpan(_starts)[..stable]);
        _text.RemoveRange(stable, _text.Count - stable);
        _extra.RemoveRange(stable, _extra.Count - stable);
        _separators.RemoveRange(stable, _separators.Count - stable);
        _display.RemoveRange(stable, _display.Count - stable);
        var offset = stable == 0 ? 0 : starts[stable - 1] + _text[stable - 1].Length + _extra[stable - 1];
        var displayLine = stable == 0 ? -1 : _display[stable - 1];
        for (var i = stable; i < next.Count; i++)
        {
            var line = next[i];
            var separator = RenderedSeparator(i - 1, next);
            _separators.Add(separator);
            if (i > 0)
            {
                offset += separator.Length;
            }

            if (i == 0 || !line.JoinedToPrevious)
            {
                displayLine++;
            }

            var extra = ExtraOf(i, line);
            _text.Add(line.Text);
            _extra.Add(extra);
            starts.Add(offset);
            _display.Add(displayLine);
            offset += line.Text.Length + extra;
        }

        var update = new MirrorUpdate(edits, _shown, _starts, next, _text, _extra, starts, prefix, suffix);
        _scratch = _shown;
        _shown = next;
        _starts = starts;
        DisplayLineCount = displayLine + 1;
        return update;
    }

    /// <summary>A shown line is unchanged in place when it is the same line and still has the same separator in front of it.</summary>
    private bool Same(int shownIndex, int previousIndex, List<Line> next) =>
        ReferenceEquals(_shown[shownIndex], next[previousIndex + 1]) && _separators[shownIndex] == RenderedSeparator(previousIndex, next);

    /// <summary>
    /// The separator in front of the line after <paramref name="previousIndex"/> as rendered: a line that continues
    /// a row heard up to its end starts a new line too.
    /// </summary>
    private string RenderedSeparator(int previousIndex, List<Line> next)
    {
        var line = next[previousIndex + 1];
        if (!line.JoinedToPrevious || previousIndex < 0)
        {
            return "\r\n";
        }

        if (previousIndex >= _breakStart && EndsWithBreak(next[previousIndex]))
        {
            return "\r\n";
        }

        return line.Text.StartsWith(' ') ? string.Empty : " ";
    }

    private bool EndsWithBreak(Line row)
    {
        foreach (var b in _breaks)
        {
            if (ReferenceEquals(b.Row, row) && ResolveColumn(b, row.Text) == row.Text.Length)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>How many characters the reading breaks add to the text of the line at an index of the new list: two for each break inside it.</summary>
    private int ExtraOf(int index, Line line) => index < _breakStart ? 0 : ExtraOf(line);

    /// <summary>How many characters the reading breaks add to a line's text: two for each break inside it.</summary>
    private int ExtraOf(Line line)
    {
        if (_breaks.Count == 0)
        {
            return 0;
        }

        var extra = 0;
        foreach (var b in _breaks)
        {
            if (ReferenceEquals(b.Row, line) && ResolveColumn(b, line.Text) is var c && c > 0 && c < line.Text.Length)
            {
                extra += 2;
            }
        }

        return extra;
    }

    /// <summary>The line's text as rendered, with a line break at each reading break inside it.</summary>
    private string Rendered(Line line)
    {
        if (ExtraOf(line) == 0)
        {
            return line.Text;
        }

        var text = line.Text;
        var sb = new StringBuilder(text.Length + 2 * _breaks.Count);
        var from = 0;
        foreach (var b in _breaks)
        {
            if (!ReferenceEquals(b.Row, line))
            {
                continue;
            }

            var c = ResolveColumn(b, text);
            if (c > from && c < text.Length)
            {
                sb.Append(text, from, c - from).Append("\r\n");
                from = c;
            }
        }

        sb.Append(text, from, text.Length - from);
        return sb.ToString();
    }

    /// <summary>
    /// Where a break falls in the row's current text: at its mark, or, when the text continues the word the mark
    /// cut through, in front of that word; -1 when the row has shrunk below the mark.
    /// </summary>
    private static int ResolveColumn(ReadingBreak b, string text)
    {
        if (b.Mark > text.Length || b.Mark == 0)
        {
            return -1;
        }

        var c = b.Mark;
        if (c == text.Length)
        {
            return c;
        }

        if (char.IsWhiteSpace(text[c]))
        {
            // The new text starts with a space: it stays on the heard side, so the new line starts with a word.
            while (c < text.Length && char.IsWhiteSpace(text[c]))
            {
                c++;
            }

            return c;
        }

        if (char.IsWhiteSpace(text[c - 1]))
        {
            return c;
        }

        var space = text.LastIndexOf(' ', c - 1);
        return space >= 0 && space + 1 >= b.Floor ? space + 1 : c;
    }

    /// <summary>Forgets breaks whose line is gone or has shrunk below the mark; the breaks stay in text order.</summary>
    private void PruneBreaks(List<Line> next)
    {
        for (var i = _breaks.Count - 1; i >= 0; i--)
        {
            var b = _breaks[i];
            var index = IndexOf(next, b.Row);
            if (index < 0 || b.Mark > next[index].Text.Length)
            {
                _breaks.RemoveAt(i);
            }
        }
    }

    /// <summary>The column of a character position within a line, without the line breaks that reading breaks added.</summary>
    private int LogicalColumn(int index, int renderedColumn)
    {
        if (_breaks.Count == 0)
        {
            return Math.Min(renderedColumn, _text[index].Length);
        }

        var text = _text[index];
        var column = renderedColumn;
        foreach (var b in _breaks)
        {
            if (ReferenceEquals(b.Row, _shown[index]) && ResolveColumn(b, text) is var c && c > 0 && c < text.Length && c + 2 <= renderedColumn)
            {
                column -= 2;
            }
        }

        return Math.Clamp(column, 0, text.Length);
    }

    private int IndexOf(Line row) => IndexOf(_shown, row);

    /// <summary>Break rows sit near the end, where the search starts.</summary>
    private static int IndexOf(List<Line> rows, Line row)
    {
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(rows[i], row))
            {
                return i;
            }
        }

        return -1;
    }

    internal static int LineIndexAt(List<int> starts, int position)
    {
        var low = 0;
        var high = starts.Count - 1;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (starts[mid] <= position)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return low;
    }
}

/// <summary>
/// The edits of one <see cref="TranscriptMirror.Update"/> and where a caret from before it belongs afterwards.
/// Valid until the next update: the mirror reuses the previous line list.
/// </summary>
public sealed class MirrorUpdate
{
    private readonly List<Line> _oldShown;
    private readonly List<int> _oldStarts;
    private readonly List<Line> _next;
    private readonly List<string> _text;
    private readonly List<int> _extra;
    private readonly List<int> _starts;
    private readonly int _prefix;
    private readonly int _suffix;

    internal MirrorUpdate(List<MirrorEdit> edits, List<Line> oldShown, List<int> oldStarts, List<Line> next, List<string> text, List<int> extra, List<int> starts, int prefix, int suffix)
    {
        Edits = edits;
        _oldShown = oldShown;
        _oldStarts = oldStarts;
        _next = next;
        _text = text;
        _extra = extra;
        _starts = starts;
        _prefix = prefix;
        _suffix = suffix;
    }

    /// <summary>Replacements in descending start order; empty when nothing changed.</summary>
    public IReadOnlyList<MirrorEdit> Edits { get; }

    public bool Changed => Edits.Count > 0;

    /// <summary>
    /// Where a caret that sat at a position in the old text belongs in the new text: the same column of the same
    /// transcript line when that line is still shown (clamped to the line's new length), otherwise the start of the
    /// nearest later line that is still shown, or of the lines that took its place. The caret therefore stays with
    /// the content it was on while lines above it change and while the line it is on is rewritten.
    /// </summary>
    public int MapPosition(int position)
    {
        if (_oldShown.Count == 0 || _next.Count == 0)
        {
            return 0;
        }

        var index = TranscriptMirror.LineIndexAt(_oldStarts, position);
        var column = position - _oldStarts[index];
        var target = NewIndexOf(index);
        if (target >= 0)
        {
            return _starts[target] + Math.Min(column, _text[target].Length + _extra[target]);
        }

        for (var i = index + 1; i < _oldShown.Count; i++)
        {
            target = NewIndexOf(i);
            if (target >= 0)
            {
                return _starts[target];
            }
        }

        return _prefix < _next.Count ? _starts[_prefix] : _starts[^1] + _text[^1].Length + _extra[^1];
    }

    private int NewIndexOf(int oldIndex)
    {
        if (oldIndex < _prefix)
        {
            return oldIndex;
        }

        if (oldIndex >= _oldShown.Count - _suffix)
        {
            return oldIndex - _oldShown.Count + _next.Count;
        }

        var line = _oldShown[oldIndex];
        for (var k = _prefix; k < _next.Count - _suffix; k++)
        {
            if (ReferenceEquals(_next[k], line))
            {
                return k;
            }
        }

        return -1;
    }
}

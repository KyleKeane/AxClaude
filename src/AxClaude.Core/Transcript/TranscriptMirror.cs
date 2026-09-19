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
/// </summary>
public sealed class TranscriptMirror
{
    private List<Line> _shown = [];
    private List<Line> _scratch = [];
    private readonly List<string> _text = [];
    private List<int> _starts = [];
    private readonly List<string> _separators = [];
    private readonly List<int> _display = [];

    /// <summary>Number of transcript lines shown, counting each wrapped fragment.</summary>
    public int LineCount => _shown.Count;

    /// <summary>Number of lines as the reader sees them: joined fragments count once.</summary>
    public int DisplayLineCount { get; private set; }

    /// <summary>Length of the mirrored text.</summary>
    public int Length => _shown.Count == 0 ? 0 : _starts[^1] + _text[^1].Length;

    public Line LineAt(int index) => _shown[index];

    public string TextAt(int index) => _text[index];

    /// <summary>Character position where a visible line starts.</summary>
    public int StartAt(int index) => _starts[index];

    /// <summary>Index of the visible line that contains the character position (0 when there are no lines).</summary>
    public int LineIndexAt(int position) => LineIndexAt(_starts, position);

    /// <summary>The display line (as the reader counts them) that a visible line belongs to.</summary>
    public int DisplayLineOf(int index) => _display[index];

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
            sb.Append(_separators[i]).Append(_text[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// The text that goes between a line and the one before it: a line break, or, for a line that continues a
    /// wrapped row, a single space (nothing when the line already starts with one).
    /// </summary>
    public static string SeparatorBefore(Line line) =>
        !line.JoinedToPrevious ? "\r\n" : line.Text.StartsWith(' ') ? string.Empty : " ";

    /// <summary>The visible lines as one text, the way the view shows them.</summary>
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

        var max = Math.Min(_shown.Count, next.Count);
        var prefix = 0;
        while (prefix < max && Same(prefix, next[prefix]))
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < max - prefix && Same(_shown.Count - 1 - suffix, next[^(suffix + 1)]))
        {
            suffix++;
        }

        var edits = new List<MirrorEdit>();
        for (var i = 0; i < prefix; i++)
        {
            if (_text[i] != next[i].Text)
            {
                edits.Add(new MirrorEdit(_starts[i], _text[i].Length, next[i].Text));
            }
        }

        for (var k = 1; k <= suffix; k++)
        {
            var shownIndex = _shown.Count - k;
            var nextIndex = next.Count - k;
            if (_text[shownIndex] != next[nextIndex].Text)
            {
                edits.Add(new MirrorEdit(_starts[shownIndex], _text[shownIndex].Length, next[nextIndex].Text));
            }
        }

        var shownMiddleEnd = _shown.Count - suffix;
        var nextMiddleEnd = next.Count - suffix;
        if (shownMiddleEnd > prefix || nextMiddleEnd > prefix)
        {
            // The middle is replaced together with the separator in front of it. When nothing precedes it, the first
            // kept line has no separator of its own, so the one after the middle changes hands instead.
            var start = prefix == 0 ? 0 : _starts[prefix - 1] + _text[prefix - 1].Length;
            var end = shownMiddleEnd > prefix ? _starts[shownMiddleEnd - 1] + _text[shownMiddleEnd - 1].Length : start;
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
                        middle.Append(SeparatorBefore(next[i]));
                    }

                    middle.Append(next[i].Text);
                }

                if (prefix == 0 && suffix > 0)
                {
                    middle.Append(SeparatorBefore(next[nextMiddleEnd]));
                }
            }

            edits.Add(new MirrorEdit(start, end - start, middle.ToString()));
        }

        if (edits.Count == 0)
        {
            // The same lines with the same texts and separators: nothing to recompute.
            return new MirrorUpdate(edits, _shown, _starts, _shown, _text, _starts, prefix, suffix);
        }

        edits.Sort((a, b) => b.Start.CompareTo(a.Start));

        // Everything up to the first prefix line whose text changed keeps its text, offset, separator and display
        // line; only the rest is recomputed. The old start offsets stay intact for the caret mapping.
        var stable = 0;
        while (stable < prefix && ReferenceEquals(_text[stable], next[stable].Text))
        {
            stable++;
        }

        var starts = new List<int>(next.Count);
        starts.AddRange(CollectionsMarshal.AsSpan(_starts)[..stable]);
        _text.RemoveRange(stable, _text.Count - stable);
        _separators.RemoveRange(stable, _separators.Count - stable);
        _display.RemoveRange(stable, _display.Count - stable);
        var offset = stable == 0 ? 0 : starts[stable - 1] + _text[stable - 1].Length;
        var displayLine = stable == 0 ? -1 : _display[stable - 1];
        for (var i = stable; i < next.Count; i++)
        {
            var line = next[i];
            var separator = SeparatorBefore(line);
            _separators.Add(separator);
            if (i > 0)
            {
                offset += separator.Length;
            }

            if (i == 0 || !line.JoinedToPrevious)
            {
                displayLine++;
            }

            _text.Add(line.Text);
            starts.Add(offset);
            _display.Add(displayLine);
            offset += line.Text.Length;
        }

        var update = new MirrorUpdate(edits, _shown, _starts, next, _text, starts, prefix, suffix);
        _scratch = _shown;
        _shown = next;
        _starts = starts;
        DisplayLineCount = displayLine + 1;
        return update;
    }

    /// <summary>A shown line is unchanged in place when it is the same line and still has the same separator in front of it.</summary>
    private bool Same(int shownIndex, Line line) =>
        ReferenceEquals(_shown[shownIndex], line) && _separators[shownIndex] == SeparatorBefore(line);

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
    private readonly List<int> _starts;
    private readonly int _prefix;
    private readonly int _suffix;

    internal MirrorUpdate(List<MirrorEdit> edits, List<Line> oldShown, List<int> oldStarts, List<Line> next, List<string> text, List<int> starts, int prefix, int suffix)
    {
        Edits = edits;
        _oldShown = oldShown;
        _oldStarts = oldStarts;
        _next = next;
        _text = text;
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
            return _starts[target] + Math.Min(column, _text[target].Length);
        }

        for (var i = index + 1; i < _oldShown.Count; i++)
        {
            target = NewIndexOf(i);
            if (target >= 0)
            {
                return _starts[target];
            }
        }

        return _prefix < _next.Count ? _starts[_prefix] : _starts[^1] + _text[^1].Length;
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

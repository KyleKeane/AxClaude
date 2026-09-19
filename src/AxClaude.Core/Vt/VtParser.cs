using System.Text;

namespace AxClaude.Core.Vt;

/// <summary>Receives the decoded pieces of a VT byte stream.</summary>
public interface IVtSink
{
    void Print(string text);
    void Control(char c);
    void Escape(char final, string intermediates);
    void Csi(string parameters, string intermediates, char final);
    void Osc(string content);
}

/// <summary>
/// Splits a stream of characters into printable runs, C0 controls, ESC sequences, CSI sequences and OSC strings.
/// The parser keeps its state between calls, so a sequence may be split across chunks.
/// </summary>
public sealed class VtParser(IVtSink sink)
{
    private enum State { Ground, Escape, Csi, Osc, OscEscape, String, StringEscape }

    private State _state = State.Ground;
    private readonly StringBuilder _print = new();
    private readonly StringBuilder _parameters = new();
    private readonly StringBuilder _intermediates = new();
    private readonly StringBuilder _osc = new();

    public void Feed(ReadOnlySpan<char> chars)
    {
        foreach (var c in chars)
        {
            Step(c);
        }

        FlushPrint();
    }

    private void Step(char c)
    {
        switch (_state)
        {
            case State.Ground:
                if (c == '\x1b')
                {
                    FlushPrint();
                    _intermediates.Clear();
                    _state = State.Escape;
                }
                else if (c < ' ')
                {
                    FlushPrint();
                    sink.Control(c);
                }
                else if (c != '\x7f')
                {
                    _print.Append(c);
                }

                break;

            case State.Escape:
                if (c == '[')
                {
                    _parameters.Clear();
                    _intermediates.Clear();
                    _state = State.Csi;
                }
                else if (c == ']')
                {
                    _osc.Clear();
                    _state = State.Osc;
                }
                else if (c is 'P' or 'X' or '^' or '_')
                {
                    _state = State.String;
                }
                else if (c >= ' ' && c <= '/')
                {
                    _intermediates.Append(c);
                }
                else if (c >= '0' && c <= '~')
                {
                    var intermediates = _intermediates.ToString();
                    _intermediates.Clear();
                    _state = State.Ground;
                    sink.Escape(c, intermediates);
                }
                else if (c == '\x1b')
                {
                    _intermediates.Clear();
                }
                else if (c < ' ')
                {
                    sink.Control(c);
                }
                else
                {
                    _state = State.Ground;
                }

                break;

            case State.Csi:
                if (c >= '0' && c <= '?')
                {
                    _parameters.Append(c);
                }
                else if (c >= ' ' && c <= '/')
                {
                    _intermediates.Append(c);
                }
                else if (c >= '@' && c <= '~')
                {
                    _state = State.Ground;
                    sink.Csi(_parameters.ToString(), _intermediates.ToString(), c);
                }
                else if (c == '\x1b')
                {
                    _intermediates.Clear();
                    _state = State.Escape;
                }
                else if (c is '\x18' or '\x1a')
                {
                    _state = State.Ground;
                }
                else if (c < ' ')
                {
                    sink.Control(c);
                }
                else if (c != '\x7f')
                {
                    _state = State.Ground;
                }

                break;

            case State.Osc:
                if (c == '\a')
                {
                    _state = State.Ground;
                    sink.Osc(_osc.ToString());
                }
                else if (c == '\x1b')
                {
                    _state = State.OscEscape;
                }
                else if (c >= ' ')
                {
                    _osc.Append(c);
                }

                break;

            case State.OscEscape:
                if (c == '\\')
                {
                    _state = State.Ground;
                    sink.Osc(_osc.ToString());
                }
                else
                {
                    // Not a string terminator: the OSC is abandoned and ESC starts a new sequence.
                    _intermediates.Clear();
                    _state = State.Escape;
                    Step(c);
                }

                break;

            case State.String:
                if (c == '\x1b')
                {
                    _state = State.StringEscape;
                }
                else if (c == '\a')
                {
                    _state = State.Ground;
                }

                break;

            case State.StringEscape:
                if (c == '\\')
                {
                    _state = State.Ground;
                }
                else if (c != '\x1b')
                {
                    _state = State.String;
                }

                break;
        }
    }

    private void FlushPrint()
    {
        if (_print.Length == 0)
        {
            return;
        }

        var text = _print.ToString();
        _print.Clear();
        sink.Print(text);
    }
}

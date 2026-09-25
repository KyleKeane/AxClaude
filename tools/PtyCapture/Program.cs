// PtyCapture: records the raw byte stream that a Windows pseudo console (ConPTY) emits while running a command,
// driven by a small script. The recordings are the parser's test fixtures and the material for studying how Claude
// Code renders in --ax-screen-reader mode. The console and the child's environment are the app's own (PtyHost and
// ClaudeLauncher in AxClaude.Core), so a recording shows what the app sees.
//
// Usage:
//   PtyCapture --out <file.vt> [--cwd <dir>] [--cols N] [--rows N] [--script <file>] -- <command line>
//   PtyCapture --dump <file.vt> [--raw]    (print a human readable rendering of a recording; e-mail addresses,
//                                          the account in paths, pipe names and session ids are replaced unless --raw)
//
// Script commands (one per line, '#' starts a comment):
//   send <text>        write text to the PTY input; supports \r \n \t \e \xHH escapes
//   mark               remember the current output offset (used by "wait")
//   iffound <command>  run <command> only if the previous "wait" found its text
//   wait <ms> <text>   wait up to <ms> until output since the last mark contains <text>
//   quiet <ms> <max>   wait until no output arrives for <ms> milliseconds (give up after <max>)
//   sleep <ms>         sleep
//   exit <ms>          wait up to <ms> for the process to exit, then stop it

using System.Diagnostics;
using System.Text;
using AxClaude.Core.Privacy;
using AxClaude.Core.Pty;

namespace PtyCapture;

internal static class Program
{
    private static int Main(string[] args)
    {
        string? cwd = null, outPath = null, scriptPath = null, dumpPath = null;
        int cols = 120, rows = 40;
        var raw = false;
        var command = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--cwd": cwd = args[++i]; break;
                case "--out": outPath = args[++i]; break;
                case "--script": scriptPath = args[++i]; break;
                case "--dump": dumpPath = args[++i]; break;
                case "--raw": raw = true; break;
                case "--cols": cols = int.Parse(args[++i]); break;
                case "--rows": rows = int.Parse(args[++i]); break;
                case "--":
                    command.AddRange(args.Skip(i + 1));
                    i = args.Length;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    return 2;
            }
        }

        if (dumpPath is not null)
        {
            Console.OutputEncoding = Encoding.UTF8;
            // Personal data (the e-mail address /status prints, the account in paths, session ids) is replaced unless --raw.
            var rendered = Dump.Render(ReadShared(dumpPath));
            Console.Out.Write(raw ? rendered : Redaction.Redact(rendered));
            return 0;
        }

        if (outPath is null || command.Count == 0)
        {
            Console.Error.WriteLine("Usage: PtyCapture --out <file.vt> [--cwd <dir>] [--cols N] [--rows N] [--script <file>] -- <command line>");
            Console.Error.WriteLine("       PtyCapture --dump <file.vt> [--raw]");
            return 2;
        }

        cwd ??= Environment.CurrentDirectory;
        var commandLine = ClaudeLauncher.BuildCommandLine(command[0], command.Skip(1));
        string[] script = scriptPath is null ? [] : File.ReadAllLines(scriptPath);
        Console.Error.WriteLine($"[capture] cwd={cwd} size={cols}x{rows}");
        Console.Error.WriteLine($"[capture] command: {commandLine}");

        using var recorder = new StreamRecorder(outPath);
        var output = new Output();
        var clock = Stopwatch.StartNew();
        var exited = new ManualResetEventSlim();
        using var host = PtyHost.Start(new PtyOptions(commandLine, cwd, cols, rows, ClaudeLauncher.BuildEnvironment()));
        host.DataReceived += data =>
        {
            recorder.Write(data);
            output.Append(data, clock.ElapsedMilliseconds);
        };
        host.Exited += code =>
        {
            Console.Error.WriteLine($"[capture] process exited with code {code} at {clock.ElapsedMilliseconds} ms");
            exited.Set();
        };
        Console.Error.WriteLine($"[capture] started pid {host.ProcessId}");

        var runner = new ScriptRunner(host, output, exited, clock);
        foreach (var line in script)
        {
            runner.Execute(line);
        }

        if (!host.HasExited)
        {
            Console.Error.WriteLine("[capture] script finished; stopping the process");
        }

        host.Dispose(); // Closes the console, which ends the client, and waits for the last of its output.
        Console.Error.WriteLine($"[capture] wrote {recorder.Length} bytes to {outPath}");
        return 0;
    }

    private static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}

/// <summary>The output so far, kept in memory for the script's wait and quiet commands.</summary>
internal sealed class Output
{
    private readonly MemoryStream _memory = new();
    private readonly object _lock = new();
    private long _lastAt;

    /// <summary>The clock reading at which the last chunk arrived.</summary>
    public long LastAt { get { lock (_lock) { return _lastAt; } } }

    public long Length { get { lock (_lock) { return _memory.Length; } } }

    public void Append(ReadOnlySpan<byte> data, long elapsedMs)
    {
        lock (_lock)
        {
            _memory.Write(data);
            _lastAt = elapsedMs;
        }
    }

    public string TextSince(long offset)
    {
        lock (_lock)
        {
            return Encoding.UTF8.GetString(_memory.GetBuffer().AsSpan((int)offset, (int)(_memory.Length - offset)));
        }
    }
}

internal sealed class ScriptRunner(PtyHost host, Output output, ManualResetEventSlim exited, Stopwatch clock)
{
    private long _mark;
    private bool _lastFound;

    public void Execute(string rawLine)
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#')) return;
        var space = line.IndexOf(' ');
        var verb = space < 0 ? line : line[..space];
        var rest = space < 0 ? string.Empty : line[(space + 1)..];
        Console.Error.WriteLine($"[script t={clock.ElapsedMilliseconds}] {line}");

        switch (verb)
        {
            case "send":
                host.Write(Unescape(rest));
                break;
            case "mark":
                _mark = output.Length;
                break;
            case "iffound":
                if (_lastFound) Execute(rest);
                break;
            case "wait":
                {
                    var parts = rest.Split(' ', 2);
                    var timeout = int.Parse(parts[0]);
                    var needle = Unescape(parts[1]);
                    var deadline = clock.ElapsedMilliseconds + timeout;
                    _lastFound = false;
                    while (clock.ElapsedMilliseconds < deadline)
                    {
                        if (output.TextSince(_mark).Contains(needle, StringComparison.Ordinal))
                        {
                            Console.Error.WriteLine($"[script t={clock.ElapsedMilliseconds}]   found");
                            _lastFound = true;
                            return;
                        }
                        if (host.HasExited) { Console.Error.WriteLine("[script]   process exited"); return; }
                        Thread.Sleep(50);
                    }
                    Console.Error.WriteLine($"[script t={clock.ElapsedMilliseconds}]   TIMEOUT waiting for {needle}");
                    break;
                }
            case "quiet":
                {
                    var parts = rest.Split(' ');
                    var quietFor = int.Parse(parts[0]);
                    var max = int.Parse(parts[1]);
                    var deadline = clock.ElapsedMilliseconds + max;
                    while (clock.ElapsedMilliseconds < deadline)
                    {
                        if (clock.ElapsedMilliseconds - output.LastAt >= quietFor) return;
                        if (host.HasExited) return;
                        Thread.Sleep(50);
                    }
                    Console.Error.WriteLine($"[script t={clock.ElapsedMilliseconds}]   never went quiet");
                    break;
                }
            case "sleep":
                Thread.Sleep(int.Parse(rest));
                break;
            case "exit":
                {
                    var timeout = int.Parse(rest);
                    if (!exited.Wait(timeout))
                    {
                        Console.Error.WriteLine("[script]   process did not exit; stopping it");
                        host.Dispose();
                    }
                    break;
                }
            default:
                Console.Error.WriteLine($"[script]   unknown command '{verb}'");
                break;
        }
    }

    private static string Unescape(string s)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
            i++;
            switch (s[i])
            {
                case 'r': sb.Append('\r'); break;
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'e': sb.Append('\x1b'); break;
                case '\\': sb.Append('\\'); break;
                case 'x':
                    sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 2), 16));
                    i += 2;
                    break;
                default: sb.Append('\\').Append(s[i]); break;
            }
        }
        return sb.ToString();
    }
}

internal static class Dump
{
    public static string Render(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        var sb = new StringBuilder(text.Length * 2);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                case '\x1b':
                    var end = EscapeEnd(text, i);
                    sb.Append('⟨').Append("ESC").Append(text, i + 1, end - i - 1).Append('⟩');
                    i = end - 1;
                    break;
                case '\r': sb.Append("⟨CR⟩"); break;
                case '\n': sb.Append("⟨LF⟩\n"); break;
                case '\a': sb.Append("⟨BEL⟩"); break;
                case '\t': sb.Append("⟨TAB⟩"); break;
                case < ' ': sb.Append('⟨').Append('^').Append((char)(c + 64)).Append('⟩'); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    // Returns the index one past the end of the escape sequence starting at text[start].
    private static int EscapeEnd(string text, int start)
    {
        var i = start + 1;
        if (i >= text.Length) return i;
        switch (text[i])
        {
            case '[': // CSI: parameters and intermediates, then a final byte 0x40-0x7E
                i++;
                while (i < text.Length && text[i] < '\x40') i++;
                return Math.Min(i + 1, text.Length);
            case ']': // OSC: terminated by BEL or ESC \
                i++;
                while (i < text.Length)
                {
                    if (text[i] == '\a') return i + 1;
                    if (text[i] == '\x1b' && i + 1 < text.Length && text[i + 1] == '\\') return i + 2;
                    i++;
                }
                return i;
            case 'P': // DCS and friends, terminated by ESC \
            case 'X':
            case '^':
            case '_':
                i++;
                while (i + 1 < text.Length && !(text[i] == '\x1b' && text[i + 1] == '\\')) i++;
                return Math.Min(i + 2, text.Length);
            default: // two-byte escape such as ESC 7, ESC 8, ESC =, ESC >
                return i + 1;
        }
    }
}

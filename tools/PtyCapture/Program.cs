// PtyCapture: records the raw byte stream that a Windows pseudo console (ConPTY)
// emits while running a command, driven by a small script. The recordings are
// used as fixtures for the VT parser tests and for studying how Claude Code
// renders in --ax-screen-reader mode.
//
// Usage:
//   PtyCapture --out <file.vt> [--cwd <dir>] [--cols N] [--rows N] [--script <file>] -- <command line>
//   PtyCapture --dump <file.vt>            (print a human readable rendering of a recording)
//
// Script commands (one per line, '#' starts a comment):
//   send <text>        write text to the PTY input; supports \r \n \t \e \xHH escapes
//   mark               remember the current output offset (used by "wait")
//   iffound <command>  run <command> only if the previous "wait" found its text
//   wait <ms> <text>   wait up to <ms> until output since the last mark contains <text>
//   quiet <ms> <max>   wait until no output arrives for <ms> milliseconds (give up after <max>)
//   sleep <ms>         sleep
//   exit <ms>          wait up to <ms> for the process to exit, then terminate it

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PtyCapture;

internal static class Program
{
    private static int Main(string[] args)
    {
        string? cwd = null, outPath = null, scriptPath = null, dumpPath = null;
        short cols = 120, rows = 40;
        var command = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--cwd": cwd = args[++i]; break;
                case "--out": outPath = args[++i]; break;
                case "--script": scriptPath = args[++i]; break;
                case "--dump": dumpPath = args[++i]; break;
                case "--cols": cols = short.Parse(args[++i]); break;
                case "--rows": rows = short.Parse(args[++i]); break;
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
            Console.Out.Write(Dump.Render(ReadShared(dumpPath)));
            return 0;
        }

        if (outPath is null || command.Count == 0)
        {
            Console.Error.WriteLine("Usage: PtyCapture --out <file.vt> [--cwd <dir>] [--cols N] [--rows N] [--script <file>] -- <command line>");
            Console.Error.WriteLine("       PtyCapture --dump <file.vt>");
            return 2;
        }

        cwd ??= Environment.CurrentDirectory;
        var commandLine = string.Join(' ', command.Select(Quote));
        var script = scriptPath is null ? new List<string>() : File.ReadAllLines(scriptPath).ToList();

        Console.Error.WriteLine($"[capture] cwd={cwd} size={cols}x{rows}");
        Console.Error.WriteLine($"[capture] command: {commandLine}");

        using var recorder = new Recorder(outPath);
        using var session = PtySession.Start(commandLine, cwd, cols, rows, ChildEnvironment());
        var stopwatch = Stopwatch.StartNew();

        var reader = new Thread(() =>
        {
            var buffer = new byte[65536];
            try
            {
                while (true)
                {
                    var n = session.Output.Read(buffer, 0, buffer.Length);
                    if (n <= 0) break;
                    recorder.Append(buffer.AsSpan(0, n), stopwatch.ElapsedMilliseconds);
                }
            }
            catch (IOException) { /* pipe closed */ }
            catch (ObjectDisposedException) { }
            Console.Error.WriteLine($"[capture] output pipe closed at {stopwatch.ElapsedMilliseconds} ms");
        }) { IsBackground = true, Name = "pty-reader" };
        reader.Start();

        var runner = new ScriptRunner(session, recorder, stopwatch);
        foreach (var line in script)
        {
            runner.Execute(line);
        }

        if (!session.HasExited)
        {
            Console.Error.WriteLine("[capture] script finished; terminating process");
            session.Terminate();
        }

        session.ClosePseudoConsole();
        reader.Join(TimeSpan.FromSeconds(5));
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

    private static string Quote(string arg) =>
        arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0 ? arg : "\"" + arg.Replace("\"", "\\\"") + "\"";

    // The child must not think it is running inside this Claude Code session (or any other).
    private static Dictionary<string, string> ChildEnvironment()
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = (string)entry.Key;
            if (key.StartsWith("CLAUDE", StringComparison.OrdinalIgnoreCase)) continue;
            env[key] = (string?)entry.Value ?? string.Empty;
        }
        env["TERM"] = "xterm-256color";
        return env;
    }
}

internal sealed class Recorder : IDisposable
{
    private readonly FileStream _file;
    private readonly StreamWriter _index;
    private readonly MemoryStream _memory = new();
    private readonly object _lock = new();
    public long LastOutputAt { get; private set; }
    public long Length { get { lock (_lock) return _memory.Length; } }

    public Recorder(string path)
    {
        _file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        _index = new StreamWriter(path + ".chunks.txt", false, Encoding.UTF8) { AutoFlush = true };
        _index.WriteLine("# offset length elapsedMs");
    }

    public void Append(ReadOnlySpan<byte> data, long elapsedMs)
    {
        lock (_lock)
        {
            _index.WriteLine($"{_memory.Length} {data.Length} {elapsedMs}");
            _memory.Write(data);
            _file.Write(data);
            _file.Flush();
            LastOutputAt = elapsedMs;
        }
    }

    public string TextSince(long offset)
    {
        lock (_lock)
        {
            var bytes = _memory.GetBuffer().AsSpan((int)offset, (int)(_memory.Length - offset));
            return Encoding.UTF8.GetString(bytes);
        }
    }

    public void Dispose()
    {
        _index.Dispose();
        _file.Dispose();
    }
}

internal sealed class ScriptRunner(PtySession session, Recorder recorder, Stopwatch clock)
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
                session.Write(Unescape(rest));
                break;
            case "mark":
                _mark = recorder.Length;
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
                    if (recorder.TextSince(_mark).Contains(needle, StringComparison.Ordinal))
                    {
                        Console.Error.WriteLine($"[script t={clock.ElapsedMilliseconds}]   found");
                        _lastFound = true;
                        return;
                    }
                    if (session.HasExited) { Console.Error.WriteLine("[script]   process exited"); return; }
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
                    if (clock.ElapsedMilliseconds - recorder.LastOutputAt >= quietFor) return;
                    if (session.HasExited) return;
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
                if (!session.WaitForExit(timeout))
                {
                    Console.Error.WriteLine("[script]   process did not exit; terminating");
                    session.Terminate();
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

internal sealed class PtySession : IDisposable
{
    private IntPtr _pseudoConsole;
    private PROCESS_INFORMATION _process;
    private SafeFileHandle? _inputWrite;
    private SafeFileHandle? _outputRead;
    private bool _consoleClosed;

    public FileStream Input { get; private set; } = null!;
    public FileStream Output { get; private set; } = null!;

    public static PtySession Start(string commandLine, string cwd, short cols, short rows, Dictionary<string, string> environment)
    {
        var session = new PtySession();

        if (!Native.CreatePipe(out var inputRead, out var inputWrite, IntPtr.Zero, 0)) throw Native.Error("CreatePipe(input)");
        if (!Native.CreatePipe(out var outputRead, out var outputWrite, IntPtr.Zero, 0)) throw Native.Error("CreatePipe(output)");

        var size = new COORD { X = cols, Y = rows };
        var hr = Native.CreatePseudoConsole(size, inputRead, outputWrite, 0, out session._pseudoConsole);
        if (hr != 0) throw new InvalidOperationException($"CreatePseudoConsole failed: 0x{hr:X8}");

        // The pseudo console duplicated its ends of the pipes; release ours.
        inputRead.Dispose();
        outputWrite.Dispose();

        var attributeListSize = IntPtr.Zero;
        Native.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeListSize);
        var startup = new STARTUPINFOEX();
        startup.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
        startup.lpAttributeList = Marshal.AllocHGlobal(attributeListSize);
        if (!Native.InitializeProcThreadAttributeList(startup.lpAttributeList, 1, 0, ref attributeListSize)) throw Native.Error("InitializeProcThreadAttributeList");
        if (!Native.UpdateProcThreadAttribute(startup.lpAttributeList, 0, (IntPtr)Native.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                session._pseudoConsole, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero)) throw Native.Error("UpdateProcThreadAttribute");

        var envBlock = BuildEnvironmentBlock(environment);
        var envPtr = Marshal.StringToHGlobalUni(envBlock);
        // If this process was started with redirected stdio (pipes), CreateProcess would
        // propagate those pipe handles to the child even with bInheritHandles=false, and the
        // child would then not see a console on its stdin/stdout. Clearing our standard
        // handles for the duration of the call makes the child receive handles to the
        // pseudo console instead.
        var savedStdIn = Native.GetStdHandle(Native.STD_INPUT_HANDLE);
        var savedStdOut = Native.GetStdHandle(Native.STD_OUTPUT_HANDLE);
        var savedStdErr = Native.GetStdHandle(Native.STD_ERROR_HANDLE);
        Native.SetStdHandle(Native.STD_INPUT_HANDLE, IntPtr.Zero);
        Native.SetStdHandle(Native.STD_OUTPUT_HANDLE, IntPtr.Zero);
        Native.SetStdHandle(Native.STD_ERROR_HANDLE, IntPtr.Zero);
        try
        {
            if (!Native.CreateProcess(null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                    Native.EXTENDED_STARTUPINFO_PRESENT | Native.CREATE_UNICODE_ENVIRONMENT,
                    envPtr, cwd, ref startup, out session._process))
            {
                throw Native.Error("CreateProcess");
            }
        }
        finally
        {
            Native.SetStdHandle(Native.STD_INPUT_HANDLE, savedStdIn);
            Native.SetStdHandle(Native.STD_OUTPUT_HANDLE, savedStdOut);
            Native.SetStdHandle(Native.STD_ERROR_HANDLE, savedStdErr);
            Marshal.FreeHGlobal(envPtr);
            Native.DeleteProcThreadAttributeList(startup.lpAttributeList);
            Marshal.FreeHGlobal(startup.lpAttributeList);
        }

        session._inputWrite = inputWrite;
        session._outputRead = outputRead;
        session.Input = new FileStream(inputWrite, FileAccess.Write, 1, false);
        session.Output = new FileStream(outputRead, FileAccess.Read, 65536, false);
        Console.Error.WriteLine($"[capture] started pid {session._process.dwProcessId}");
        return session;
    }

    public bool HasExited => Native.WaitForSingleObject(_process.hProcess, 0) == 0;

    public bool WaitForExit(int timeoutMs) => Native.WaitForSingleObject(_process.hProcess, (uint)timeoutMs) == 0;

    public void Write(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        Input.Write(bytes, 0, bytes.Length);
        Input.Flush();
    }

    public void Terminate()
    {
        Native.TerminateProcess(_process.hProcess, 1);
        WaitForExit(5000);
    }

    public void ClosePseudoConsole()
    {
        if (_consoleClosed) return;
        _consoleClosed = true;
        Native.ClosePseudoConsole(_pseudoConsole);
    }

    public void Dispose()
    {
        ClosePseudoConsole();
        Input?.Dispose();
        Output?.Dispose();
        _inputWrite?.Dispose();
        _outputRead?.Dispose();
        if (_process.hProcess != IntPtr.Zero) Native.CloseHandle(_process.hProcess);
        if (_process.hThread != IntPtr.Zero) Native.CloseHandle(_process.hThread);
    }

    private static string BuildEnvironmentBlock(Dictionary<string, string> environment)
    {
        var sb = new StringBuilder();
        foreach (var pair in environment.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(pair.Key).Append('=').Append(pair.Value).Append('\0');
        }
        sb.Append('\0');
        return sb.ToString();
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct COORD { public short X; public short Y; }

[StructLayout(LayoutKind.Sequential)]
internal struct STARTUPINFO
{
    public int cb;
    public IntPtr lpReserved;
    public IntPtr lpDesktop;
    public IntPtr lpTitle;
    public int dwX;
    public int dwY;
    public int dwXSize;
    public int dwYSize;
    public int dwXCountChars;
    public int dwYCountChars;
    public int dwFillAttribute;
    public int dwFlags;
    public short wShowWindow;
    public short cbReserved2;
    public IntPtr lpReserved2;
    public IntPtr hStdInput;
    public IntPtr hStdOutput;
    public IntPtr hStdError;
}

[StructLayout(LayoutKind.Sequential)]
internal struct STARTUPINFOEX
{
    public STARTUPINFO StartupInfo;
    public IntPtr lpAttributeList;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROCESS_INFORMATION
{
    public IntPtr hProcess;
    public IntPtr hThread;
    public int dwProcessId;
    public int dwThreadId;
}

internal static class Native
{
    public const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    public const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;
    public const int STD_INPUT_HANDLE = -10;
    public const int STD_OUTPUT_HANDLE = -11;
    public const int STD_ERROR_HANDLE = -12;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetStdHandle(int nStdHandle, IntPtr hHandle);

    public static Exception Error(string what) =>
        new InvalidOperationException($"{what} failed: Win32 error {Marshal.GetLastWin32Error()}");

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateProcess(string? lpApplicationName, string lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
        bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory, ref STARTUPINFOEX lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);
}

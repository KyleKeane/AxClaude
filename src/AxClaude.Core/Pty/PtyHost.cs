using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AxClaude.Core.Pty;

public sealed record PtyOptions(
    string CommandLine,
    string WorkingDirectory,
    int Columns,
    int Rows,
    IReadOnlyDictionary<string, string> Environment);

/// <summary>
/// Runs a command inside a Windows pseudo console. Output arrives on a background thread through
/// <see cref="DataReceived"/>; <see cref="Exited"/> fires once, from another background thread.
/// </summary>
public sealed class PtyHost : IDisposable
{
    private readonly object _writeLock = new();
    private IntPtr _console;
    private Native.PROCESS_INFORMATION _process;
    private FileStream _input = null!;
    private FileStream _output = null!;
    private Thread _reader = null!;
    private Thread _waiter = null!;
    private int _exited;
    private bool _disposed;

    private PtyHost()
    {
    }

    public event Action<byte[]>? DataReceived;
    public event Action<int>? Exited;

    public int ProcessId => _process.dwProcessId;
    public bool HasExited => Volatile.Read(ref _exited) == 1;

    public static PtyHost Start(PtyOptions options)
    {
        var host = new PtyHost();
        SafeFileHandle? inputRead = null, inputWrite = null, outputRead = null, outputWrite = null;
        var attributeList = IntPtr.Zero;
        try
        {
            if (!Native.CreatePipe(out inputRead, out inputWrite, IntPtr.Zero, 0)) throw Native.Error("CreatePipe");
            if (!Native.CreatePipe(out outputRead, out outputWrite, IntPtr.Zero, 0)) throw Native.Error("CreatePipe");

            var size = new Native.COORD { X = (short)options.Columns, Y = (short)options.Rows };
            var hr = Native.CreatePseudoConsole(size, inputRead, outputWrite, 0, out host._console);
            if (hr != 0) throw new InvalidOperationException($"CreatePseudoConsole failed with 0x{hr:X8}.");

            // The pseudo console holds its own copies of these ends.
            inputRead.Dispose();
            outputWrite.Dispose();
            inputRead = outputWrite = null;

            var listSize = IntPtr.Zero;
            Native.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref listSize);
            attributeList = Marshal.AllocHGlobal(listSize);
            if (!Native.InitializeProcThreadAttributeList(attributeList, 1, 0, ref listSize)) throw Native.Error("InitializeProcThreadAttributeList");
            if (!Native.UpdateProcThreadAttribute(attributeList, 0, (IntPtr)Native.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                    host._console, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
            {
                throw Native.Error("UpdateProcThreadAttribute");
            }

            var startup = new Native.STARTUPINFOEX { lpAttributeList = attributeList };
            startup.StartupInfo.cb = Marshal.SizeOf<Native.STARTUPINFOEX>();
            var environmentBlock = Marshal.StringToHGlobalUni(BuildEnvironmentBlock(options.Environment));

            // A launcher with redirected stdio would otherwise hand its pipes to the child, which then sees
            // no console on its standard handles. Clearing ours for the call makes the child use the pseudo console.
            var savedIn = Native.GetStdHandle(Native.STD_INPUT_HANDLE);
            var savedOut = Native.GetStdHandle(Native.STD_OUTPUT_HANDLE);
            var savedErr = Native.GetStdHandle(Native.STD_ERROR_HANDLE);
            Native.SetStdHandle(Native.STD_INPUT_HANDLE, IntPtr.Zero);
            Native.SetStdHandle(Native.STD_OUTPUT_HANDLE, IntPtr.Zero);
            Native.SetStdHandle(Native.STD_ERROR_HANDLE, IntPtr.Zero);
            bool created;
            try
            {
                created = Native.CreateProcess(null, options.CommandLine, IntPtr.Zero, IntPtr.Zero, false,
                    Native.EXTENDED_STARTUPINFO_PRESENT | Native.CREATE_UNICODE_ENVIRONMENT,
                    environmentBlock, options.WorkingDirectory, ref startup, out host._process);
            }
            finally
            {
                Native.SetStdHandle(Native.STD_INPUT_HANDLE, savedIn);
                Native.SetStdHandle(Native.STD_OUTPUT_HANDLE, savedOut);
                Native.SetStdHandle(Native.STD_ERROR_HANDLE, savedErr);
                Marshal.FreeHGlobal(environmentBlock);
            }

            if (!created) throw Native.Error("CreateProcess");

            host._input = new FileStream(inputWrite, FileAccess.Write, 1, false);
            host._output = new FileStream(outputRead, FileAccess.Read, 65536, false);
            inputWrite = outputRead = null;
        }
        catch
        {
            if (host._console != IntPtr.Zero) Native.ClosePseudoConsole(host._console);
            inputRead?.Dispose();
            inputWrite?.Dispose();
            outputRead?.Dispose();
            outputWrite?.Dispose();
            throw;
        }
        finally
        {
            if (attributeList != IntPtr.Zero)
            {
                Native.DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }
        }

        host._reader = new Thread(host.ReadLoop) { IsBackground = true, Name = "pty-reader" };
        host._waiter = new Thread(host.WaitLoop) { IsBackground = true, Name = "pty-waiter" };
        host._reader.Start();
        host._waiter.Start();
        return host;
    }

    public void Write(string text) => Write(Encoding.UTF8.GetBytes(text));

    public void Write(byte[] bytes)
    {
        lock (_writeLock)
        {
            if (_disposed || HasExited)
            {
                return;
            }

            try
            {
                _input.Write(bytes, 0, bytes.Length);
                _input.Flush();
            }
            catch (IOException)
            {
                // The pipe is gone; the exit event will follow.
            }
        }
    }

    /// <summary>Closes the console, which ends the client, and terminates it if it lingers.</summary>
    public void Dispose()
    {
        lock (_writeLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Native.ClosePseudoConsole(_console);
        if (!HasExited && Native.WaitForSingleObject(_process.hProcess, 2000) != 0)
        {
            Native.TerminateProcess(_process.hProcess, 1);
        }

        _reader.Join(1000);
        _waiter.Join(1000);
        _input.Dispose();
        _output.Dispose();
        Native.CloseHandle(_process.hProcess);
        Native.CloseHandle(_process.hThread);
    }

    private void ReadLoop()
    {
        var buffer = new byte[65536];
        try
        {
            while (true)
            {
                var count = _output.Read(buffer, 0, buffer.Length);
                if (count <= 0)
                {
                    break;
                }

                DataReceived?.Invoke(buffer.AsSpan(0, count).ToArray());
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The pipe closed; the process is exiting or was stopped.
        }
    }

    private void WaitLoop()
    {
        Native.WaitForSingleObject(_process.hProcess, Native.INFINITE);
        Native.GetExitCodeProcess(_process.hProcess, out var code);
        Volatile.Write(ref _exited, 1);
        Exited?.Invoke((int)code);
    }

    private static string BuildEnvironmentBlock(IReadOnlyDictionary<string, string> environment)
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

internal static class Native
{
    public const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    public const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;
    public const int STD_INPUT_HANDLE = -10;
    public const int STD_OUTPUT_HANDLE = -11;
    public const int STD_ERROR_HANDLE = -12;
    public const uint INFINITE = 0xFFFFFFFF;

    public static Exception Error(string what) =>
        new InvalidOperationException($"{what} failed with Win32 error {Marshal.GetLastWin32Error()}.");

    [StructLayout(LayoutKind.Sequential)]
    public struct COORD
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct STARTUPINFO
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
    public struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC);

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
    public static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetStdHandle(int nStdHandle, IntPtr hHandle);
}

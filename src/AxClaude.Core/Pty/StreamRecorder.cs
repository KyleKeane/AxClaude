using System.Diagnostics;
using System.Text;

namespace AxClaude.Core.Pty;

/// <summary>
/// Writes the raw console output to a <c>.vt</c> file plus a <c>.chunks.txt</c> index (offset, length, elapsed ms per
/// chunk), the same format <c>tools/PtyCapture</c> produces, so a recording made from the app can become a test fixture.
/// </summary>
public sealed class StreamRecorder : IDisposable
{
    private readonly object _gate = new();
    private readonly FileStream _file;
    private readonly StreamWriter _index;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _length;
    private bool _disposed;

    public StreamRecorder(string path)
    {
        Path = path;
        _file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        _index = new StreamWriter(path + ".chunks.txt", false, new UTF8Encoding(false)) { AutoFlush = true };
        _index.WriteLine("# offset length elapsedMs");
    }

    public string Path { get; }

    public long Length
    {
        get
        {
            lock (_gate)
            {
                return _length;
            }
        }
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        lock (_gate)
        {
            if (_disposed || data.Length == 0)
            {
                return;
            }

            _index.WriteLine($"{_length} {data.Length} {_clock.ElapsedMilliseconds}");
            _file.Write(data);
            _file.Flush();
            _length += data.Length;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _index.Dispose();
            _file.Dispose();
        }
    }
}

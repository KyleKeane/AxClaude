namespace AxClaude;

/// <summary>Rolling diagnostic log in <c>%LOCALAPPDATA%\AxClaude\logs\axclaude.log</c> (FR-13.2). Never throws.</summary>
internal static class Log
{
    private const long RollOverBytes = 1024 * 1024;
    private static readonly object Gate = new();

    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AxClaude", "logs");

    public static string FilePath { get; } = Path.Combine(Directory, "axclaude.log");

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}\r\n{exception}");

    /// <summary>The last lines of the log, for Help, Copy diagnostics.</summary>
    public static string Tail(int lines)
    {
        try
        {
            lock (Gate)
            {
                if (!File.Exists(FilePath))
                {
                    return string.Empty;
                }

                var all = File.ReadAllLines(FilePath);
                return string.Join("\r\n", all.Skip(Math.Max(0, all.Length - lines)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > RollOverBytes)
                {
                    File.Move(FilePath, Path.ChangeExtension(FilePath, ".old.log"), overwrite: true);
                }

                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}\r\n");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A log that cannot be written must never take the app down.
        }
    }
}

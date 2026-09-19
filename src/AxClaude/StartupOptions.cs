namespace AxClaude;

/// <summary>
/// Command line: <c>AxClaude [&lt;folder&gt;] [--project &lt;folder&gt;] [--claude &lt;path&gt;] [--cols N] [--rows N] [--no-ax]
/// [--record &lt;file.vt&gt;] [-- claude arguments]</c>. Values not given fall back to the settings file.
/// </summary>
internal sealed record StartupOptions(
    string? Folder,
    string? ClaudePath,
    bool NoAx,
    int? Columns,
    int? Rows,
    string? RecordPath,
    IReadOnlyList<string> ClaudeArgs)
{
    public const string Usage = """
        Usage: AxClaude [<folder>] [--project <folder>] [--claude <path>] [--cols N] [--rows N] [--no-ax] [--record <file.vt>] [-- claude arguments]

          <folder>            the project folder (default: the current folder, or the last one used)
          --claude <path>     claude.exe or claude.cmd to run (default: found on PATH or in the usual install folders)
          --cols N, --rows N  size of the hidden console Claude writes to (default 240 by 50)
          --record <file.vt>  record the raw console stream for a bug report
          --no-ax             start Claude without --ax-screen-reader (testing only)
          -- ...              everything after -- goes to claude, for example -- --continue or -- --resume <id>
          --help, --version   this text, or the version
        """;

    public static StartupOptions Parse(string[] args)
    {
        string? folder = null;
        string? claude = null;
        string? record = null;
        var noAx = false;
        int? columns = null;
        int? rows = null;
        var claudeArgs = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--":
                    claudeArgs.AddRange(args.Skip(i + 1));
                    i = args.Length;
                    break;
                case "--claude":
                    claude = Next(args, ref i);
                    break;
                case "--project":
                    folder = Next(args, ref i);
                    break;
                case "--cols":
                    columns = ParseNumber(Next(args, ref i), 20, 1000, "--cols");
                    break;
                case "--rows":
                    rows = ParseNumber(Next(args, ref i), 5, 500, "--rows");
                    break;
                case "--record":
                    record = Path.GetFullPath(Next(args, ref i));
                    break;
                case "--no-ax":
                    noAx = true;
                    break;
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"Unknown option {arg}.\n{Usage}");
                    }

                    if (folder is not null)
                    {
                        throw new ArgumentException($"Only one folder can be given.\n{Usage}");
                    }

                    folder = arg;
                    break;
            }
        }

        if (folder is null)
        {
            // Started from a console inside a project: use that folder. Started from Explorer or a shortcut
            // the current directory is the app's own folder, which is never a project.
            var current = Path.GetFullPath(Environment.CurrentDirectory);
            var appDirectory = Path.GetFullPath(AppContext.BaseDirectory);
            if (!string.Equals(TrimSeparator(current), TrimSeparator(appDirectory), StringComparison.OrdinalIgnoreCase))
            {
                folder = current;
            }
        }
        else
        {
            folder = Path.GetFullPath(folder);
            if (!Directory.Exists(folder))
            {
                throw new ArgumentException($"The folder does not exist: {folder}");
            }
        }

        return new StartupOptions(folder, claude, noAx, columns, rows, record, claudeArgs);
    }

    private static string Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"{args[i]} needs a value.\n{Usage}");
        }

        return args[++i];
    }

    private static int ParseNumber(string text, int min, int max, string option)
    {
        if (!int.TryParse(text, out var value) || value < min || value > max)
        {
            throw new ArgumentException($"{option} must be a number between {min} and {max}.");
        }

        return value;
    }

    private static string TrimSeparator(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

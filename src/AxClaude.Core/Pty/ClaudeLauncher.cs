using System.Collections;
using System.Text;

namespace AxClaude.Core.Pty;

/// <summary>Finds the Claude Code executable and builds the command line and environment for it.</summary>
public static class ClaudeLauncher
{
    /// <summary>The native Windows installer, as documented at <see cref="InstallUrl"/>.</summary>
    public const string InstallCommand = "irm https://claude.ai/install.ps1 | iex";

    public const string InstallUrl = "https://code.claude.com/docs/en/setup";

    /// <summary>
    /// The places searched for Claude Code, in order (FR-1.2): the explicit path when one is given, otherwise
    /// <c>claude.exe</c> or <c>claude.cmd</c> in every PATH folder, the native installer's folder and the npm folder.
    /// </summary>
    public static IReadOnlyList<string> Candidates(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return [explicitPath];
        }

        var candidates = new List<string>();
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in new[] { "claude.exe", "claude.cmd" })
            {
                candidates.Add(Path.Combine(directory, name));
            }
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        candidates.Add(Path.Combine(profile, ".local", "bin", "claude.exe"));
        candidates.Add(Path.Combine(appData, "npm", "claude.cmd"));
        return candidates;
    }

    public static string? Find(string? explicitPath)
    {
        foreach (var candidate in Candidates(explicitPath))
        {
            try
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // A PATH entry with characters that are not allowed in a path.
            }
        }

        return null;
    }

    public static string BuildCommandLine(string claudePath, IEnumerable<string> arguments)
    {
        var args = string.Join(' ', arguments.Select(Quote));
        return claudePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            ? $"cmd.exe /d /s /c \"\"{claudePath}\" {args}\""
            : $"\"{claudePath}\" {args}";
    }

    /// <summary>
    /// The current environment without any CLAUDE* variable (so a nested session is not detected), plus the
    /// variables that make screen reader mode immediate.
    /// </summary>
    public static Dictionary<string, string> BuildEnvironment()
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = (string)entry.Key;
            if (key.StartsWith("CLAUDE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            env[key] = (string?)entry.Value ?? string.Empty;
        }

        env["TERM"] = "xterm-256color";
        env["CLAUDE_AX_STARTUP_QUIET_MS"] = "0";
        env["CLAUDE_AX_PREPARK_MS"] = "0";
        env["CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN"] = "1";
        return env;
    }

    /// <summary>
    /// The arguments in a line the user typed (the New session notice, FR-8.6): split at whitespace; a double-quoted
    /// part keeps its spaces and a backslash before a quote gives a literal quote. The inverse of <see cref="JoinArguments"/>.
    /// </summary>
    public static IReadOnlyList<string> SplitArguments(string text)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && text[i + 1] == '"')
            {
                current.Append('"');
                i++;
                hasToken = true;
            }
            else if (c == '"')
            {
                // "" is an empty argument; a quoted part joins what is around it: a"b c"d is one argument.
                inQuotes = !inQuotes;
                hasToken = true;
            }
            else if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (hasToken)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }

        if (hasToken)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    /// <summary>The arguments as one line, quoted where needed, so that <see cref="SplitArguments"/> gives them back.</summary>
    public static string JoinArguments(IEnumerable<string> arguments) => string.Join(' ', arguments.Select(Quote));

    private static string Quote(string argument) =>
        argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0
            ? argument
            : "\"" + argument.Replace("\"", "\\\"") + "\"";
}

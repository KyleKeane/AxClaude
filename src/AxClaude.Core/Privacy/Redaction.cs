using System.Text.RegularExpressions;

namespace AxClaude.Core.Privacy;

/// <summary>
/// Personal data in text read off Claude's screen: `/status` prints the account's e-mail address, organisation, session
/// id and a pipe name, and paths name the Windows account. <see cref="Redact"/> replaces each with a placeholder, so
/// that what a tool prints about a recording can be shown and shared (PtyCapture --dump); <see cref="Find"/> lists them,
/// for the test that keeps them out of the repository.
/// </summary>
public static partial class Redaction
{
    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailRegex();

    // The account folder under a drive's Users folder, also with doubled or forward slashes; the placeholder names below are left alone.
    [GeneratedRegex(@"(?i)([A-Z]:(?:\\{1,2}|/)Users(?:\\{1,2}|/))(?!<user>|Public\b|Default\b|runneradmin\b)[^\\/\s""'<>|:*?]+")]
    private static partial Regex UserPathRegex();

    [GeneratedRegex(@"\\\\\.\\pipe\\[^\s""'<>|]+")]
    private static partial Regex PipeRegex();

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b")]
    private static partial Regex IdRegex();

    /// <summary>
    /// The text with e-mail addresses, the account folder in paths under Users, pipe names, session ids and the
    /// given names (the Windows account name, by default) replaced by <c>&lt;email&gt;</c>, <c>&lt;user&gt;</c>,
    /// <c>&lt;pipe&gt;</c>, <c>&lt;id&gt;</c> and <c>&lt;name&gt;</c>.
    /// </summary>
    public static string Redact(string text, IEnumerable<string>? names = null)
    {
        text = EmailRegex().Replace(text, "<email>");
        text = PipeRegex().Replace(text, "<pipe>");
        text = UserPathRegex().Replace(text, "$1<user>");
        text = IdRegex().Replace(text, "<id>");
        foreach (var name in (names ?? DefaultNames()).Where(IsDistinctive))
        {
            text = Regex.Replace(text, $@"(?i)\b{Regex.Escape(name)}\b", "<name>");
        }

        return text;
    }

    /// <summary>The e-mail addresses and account paths in <paramref name="text"/>: what must never be committed.</summary>
    public static IEnumerable<string> Find(string text) =>
        EmailRegex().Matches(text).Concat(UserPathRegex().Matches(text)).Select(match => match.Value);

    /// <summary>The Windows account name, which paths and prompts can show.</summary>
    public static IReadOnlyList<string> DefaultNames() => [Environment.UserName];

    // A name short or common enough to be part of ordinary words is not replaced.
    private static bool IsDistinctive(string name) =>
        name.Length >= 3 && !name.Equals("user", StringComparison.OrdinalIgnoreCase) && !name.Equals("admin", StringComparison.OrdinalIgnoreCase);
}

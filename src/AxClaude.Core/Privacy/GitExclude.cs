namespace AxClaude.Core.Privacy;

/// <summary>
/// Keeps AxClaude's own files (raw recordings, saved conversations; all named <c>axclaude-…</c>) out of the git
/// repository of the project folder Claude runs in (D34). The patterns go into the repository's local exclude file,
/// <c>.git\info\exclude</c>, which git reads like a <c>.gitignore</c> but never commits, so the project's own files are
/// not touched. A marked block holds them; off removes the block, to commit such a file on purpose.
/// </summary>
public static class GitExclude
{
    public const string BlockStart = "# AxClaude: its recordings and saved conversations stay out of git (Options menu)";
    public const string BlockEnd = "# end AxClaude";

    /// <summary>The patterns: every file AxClaude offers to save starts with <c>axclaude-</c>.</summary>
    public static readonly IReadOnlyList<string> Patterns = ["axclaude-*.vt", "axclaude-*.vt.chunks.txt", "axclaude-*.txt"];

    /// <summary>
    /// The exclude file's text with the block present (<paramref name="on"/>) or gone, everything else kept; the same
    /// text when nothing changes.
    /// </summary>
    public static string Apply(string existing, bool on)
    {
        var lines = existing.Replace("\r\n", "\n").Split('\n').ToList();
        var start = lines.IndexOf(BlockStart);
        var end = start < 0 ? -1 : lines.IndexOf(BlockEnd, start);
        var block = new List<string> { BlockStart };
        block.AddRange(Patterns);
        block.Add(BlockEnd);
        if (start >= 0 && end > start)
        {
            if (on && lines.Skip(start).Take(end - start + 1).SequenceEqual(block))
            {
                return existing;
            }

            lines.RemoveRange(start, end - start + 1);
        }
        else if (!on)
        {
            return existing;
        }

        if (on)
        {
            // After what is there, with the file's last line ended.
            while (lines.Count > 0 && lines[^1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            lines.AddRange(block);
            lines.Add(string.Empty);
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Writes the block into the exclude file of the repository at <paramref name="folder"/> (a <c>.git</c> folder, or
    /// a <c>.git</c> file pointing at one, as in a worktree), or removes it. Returns the exclude file's path when it
    /// changed; null when there is no repository or nothing to change.
    /// </summary>
    public static string? Update(string folder, bool on)
    {
        if (GitDirectory(folder) is not { } gitDir)
        {
            return null;
        }

        var path = Path.Combine(gitDir, "info", "exclude");
        var existing = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        var updated = Apply(existing, on);
        if (updated == existing)
        {
            return null;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, updated);
        return path;
    }

    private static string? GitDirectory(string folder)
    {
        var dotGit = Path.Combine(folder, ".git");
        if (Directory.Exists(dotGit))
        {
            return dotGit;
        }

        if (File.Exists(dotGit) && File.ReadAllText(dotGit).Trim() is var text && text.StartsWith("gitdir:", StringComparison.Ordinal))
        {
            var target = text["gitdir:".Length..].Trim();
            var full = Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(folder, target));
            return Directory.Exists(full) ? full : null;
        }

        return null;
    }
}

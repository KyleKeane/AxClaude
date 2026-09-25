using AxClaude.Core.Privacy;

namespace AxClaude.Tests;

public class GitExcludeTests
{
    private const string GitDefault = "# git ls-files --others --exclude-from=.git/info/exclude\n# *.[oa]\n";

    [Fact]
    public void On_adds_the_block_after_what_is_there_once()
    {
        var once = GitExclude.Apply(GitDefault, on: true);
        Assert.StartsWith(GitDefault, once);
        Assert.Contains("axclaude-*.vt\n", once);
        Assert.EndsWith(GitExclude.BlockEnd + "\n", once);
        Assert.Same(once, GitExclude.Apply(once, on: true));
    }

    [Fact]
    public void Off_removes_the_block_and_keeps_the_rest()
    {
        var on = GitExclude.Apply(GitDefault + "secret.txt\n", on: true);
        var off = GitExclude.Apply(on, on: false);
        Assert.DoesNotContain("axclaude", off);
        Assert.Contains("secret.txt", off);
        Assert.Equal(off, GitExclude.Apply(off, on: false));
    }

    [Fact]
    public void Update_writes_into_the_repository_and_leaves_a_plain_folder_alone()
    {
        var root = Directory.CreateTempSubdirectory("axclaude-exclude-").FullName;
        try
        {
            Assert.Null(GitExclude.Update(root, on: true));
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            var path = GitExclude.Update(root, on: true);
            Assert.Equal(Path.Combine(root, ".git", "info", "exclude"), path);
            Assert.Contains("axclaude-*.txt", File.ReadAllText(path!));
            Assert.Null(GitExclude.Update(root, on: true));
            GitExclude.Update(root, on: false);
            Assert.DoesNotContain("axclaude", File.ReadAllText(path!));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_file_the_app_offers_to_save_matches_a_pattern()
    {
        // The default names of Save conversation and Record raw stream (MainForm).
        foreach (var name in new[] { "axclaude-myproject-20260925-1300.txt", "axclaude-20260925-1300.vt", "axclaude-20260925-1300.vt.chunks.txt" })
        {
            Assert.Contains(GitExclude.Patterns, pattern => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, name));
        }
    }
}

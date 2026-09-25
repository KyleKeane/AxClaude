using AxClaude.Core.Privacy;

namespace AxClaude.Tests;

public class PrivacyTests
{
    [Fact]
    public void Redact_replaces_what_status_prints_about_the_account()
    {
        var text = Redaction.Redact(
            // Built from parts, so that the scan below does not find these samples in this file.
            "Email: someone" + "@example.com\nPeer address: uds:\\\\.\\pipe\\LOCAL\\cc-msg-e875aa\ncwd: C:\\" + "Users\\Someone\\Desktop\\x\n"
            + "Session ID: e585e125-b63e-4a5e-af77-b2eb1f6ddaa2\nHello Someone.",
            ["Someone"]);
        Assert.Equal(
            "Email: <email>\nPeer address: uds:<pipe>\ncwd: C:\\Users\\<user>\\Desktop\\x\nSession ID: <id>\nHello <name>.", text);
    }

    [Fact]
    public void Redact_leaves_short_names_and_placeholders_alone()
    {
        Assert.Equal("C:\\Users\\<user>\\x, an Al fact", Redaction.Redact("C:\\Users\\<user>\\x, an Al fact", ["Al"]));
    }

    /// <summary>
    /// No file in the repository holds an e-mail address or a path into a Windows account: recordings of /status carry
    /// the account's address, and paths name the account. The licence line names the author and is the one exception.
    /// </summary>
    [Fact]
    public void The_repository_holds_no_email_address_or_account_path()
    {
        var root = RepositoryRoot();
        var found = new List<string>();
        foreach (var file in RepositoryFiles(root))
        {
            foreach (var hit in Redaction.Find(File.ReadAllText(file)))
            {
                found.Add($"{Path.GetRelativePath(root, file)}: {hit}");
            }
        }

        Assert.Empty(found);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AxClaude.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("AxClaude.sln not found above the test directory.");
    }

    // Text files outside build output and git's own folder, which is what a commit can carry.
    private static IEnumerable<string> RepositoryFiles(string root)
    {
        string[] skipped = ["bin", "obj", ".git", ".vs", "publish", "artifacts", "TestResults", ".claude"];
        string[] binary = [".ico", ".png", ".dll", ".exe", ".zip", ".pdb"];
        var pending = new Stack<string>([root]);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                if (!skipped.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase))
                {
                    pending.Push(sub);
                }
            }

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                if (!binary.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }
}

using AxClaude.Core.Pty;

namespace AxClaude.Tests;

public class ClaudeLauncherTests
{
    [Fact]
    public void Split_at_whitespace_including_new_lines()
    {
        Assert.Equal(["--continue", "--permission-mode", "plan"], ClaudeLauncher.SplitArguments("  --continue\r\n--permission-mode   plan \n"));
    }

    [Fact]
    public void Empty_and_blank_text_give_no_arguments()
    {
        Assert.Empty(ClaudeLauncher.SplitArguments(string.Empty));
        Assert.Empty(ClaudeLauncher.SplitArguments(" \r\n\t"));
    }

    [Fact]
    public void Quotes_keep_spaces_and_an_escaped_quote_is_literal()
    {
        Assert.Equal(
            ["--append-system-prompt", "Always say \"hi\" first", "--name", "my feature"],
            ClaudeLauncher.SplitArguments("--append-system-prompt \"Always say \\\"hi\\\" first\" --name \"my feature\""));
        Assert.Equal(["ab c"], ClaudeLauncher.SplitArguments("a\"b c\""));
        Assert.Equal([string.Empty], ClaudeLauncher.SplitArguments("\"\""));
    }

    [Fact]
    public void Join_and_split_round_trip()
    {
        string[] arguments = ["--continue", "--name", "my feature", "say \"hi\"", string.Empty];
        var line = ClaudeLauncher.JoinArguments(arguments);
        Assert.Equal("--continue --name \"my feature\" \"say \\\"hi\\\"\" \"\"", line);
        Assert.Equal(arguments, ClaudeLauncher.SplitArguments(line));
    }
}

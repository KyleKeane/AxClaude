using System.Reflection;
using AxClaude.Core.Pty;

namespace AxClaude;

/// <summary>The texts the window shows as notices: the keyboard shortcuts (F1), the user guide, and the help when Claude Code is missing.</summary>
internal static class HelpText
{
    public const string Shortcuts = """
        Anywhere
          Ctrl+Tab, Ctrl+Shift+Tab or F6: switch between the message field and the conversation
          Ctrl+Enter: send the message (an empty field sends a plain Enter)
          Shift+Escape: interrupt Claude, or close one of Claude's dialogs
          Ctrl+F: find text in the conversation. F3 and Shift+F3: next or previous match
          Ctrl+S: save the conversation as a text file
          Ctrl+N: change the project folder
          Ctrl+Shift+R: restart Claude in the same folder
          Ctrl+O: Claude's detailed view on or off
          Ctrl+Shift+C: send Ctrl+C to Claude
          Ctrl+Shift+M: next permission mode
          Ctrl+Shift+O: go to the latest reply
          Ctrl+Shift+K: bookmark the conversation line the caret is on, or take the bookmark away
          Ctrl+Plus, Ctrl+Minus: bigger or smaller text
          F1: this list
          Alt+F4: close AxClaude (asks first while Claude is working)

        Message field
          Enter: new line
          Ctrl+Enter: send
          Escape: does nothing; a reminder says that Shift+Escape interrupts Claude
          Shift+Tab: next permission mode
          Up on the first line, Down on the last line: earlier messages
          Ctrl+Up, Ctrl+Down: send Up or Down to Claude

        Conversation
          Arrow keys, Home, End, Ctrl+Home, Ctrl+End: move as in any text
          Page Up, Page Down: one screen up or down
          l: say the line number, like "Line 12 of 340"
          i and Shift+I: next and previous "# Input" line (a message you sent)
          o and Shift+O, or r and Shift+R: next and previous "# Output" line (a reply from Claude)
          h and Shift+H: next and previous heading. 1 to 6: heading of that level
            (Input and Output lines are level 1, Claude's headings level 2 and deeper)
          c and Shift+C: next and previous "claude:" line
          t and Shift+T: next and previous tool line
          p and Shift+P: next and previous question that needs an answer
          e and Shift+E: next and previous error or warning
          d and Shift+D: next and previous "done" line at the end of a reply
          s and Shift+S: next and previous system line
          b and Shift+B: next and previous blank line
          m: bookmark this line (a "Bookmark 1" line appears above it), or take the bookmark away again
          k and Shift+K: next and previous bookmark, read together with the line it marks
          Enter: copy the line into the message field, to reply to it
          Escape: go to the message field

        Notices
          AxClaude's questions, errors and help texts appear inside the window, in place of the conversation.
          Tab moves between the text, a field and the buttons. Enter chooses the main button. Escape closes the notice.

        Claude Code itself
          Type /help in the message field and press Ctrl+Enter for Claude Code's own commands.
          The Help menu opens the Claude Code documentation in your browser.
        """;

    /// <summary>docs/user-guide.md, embedded in the executable at build time.</summary>
    public static string UserGuide()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("UserGuide.md");
        if (stream is null)
        {
            return "The user guide is not included in this build. It is docs/user-guide.md in the source repository.";
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>FR-1.9: where the app looked, the install command, and the ways back.</summary>
    public static string ClaudeNotFound(IReadOnlyList<string> candidates)
    {
        var lines = new List<string>
        {
            "Claude Code was not found, so AxClaude cannot start it.",
            string.Empty,
            "AxClaude looked here:",
        };
        lines.AddRange(candidates.Select(c => "  " + c));
        lines.AddRange(
        [
            string.Empty,
            "To install Claude Code, open Windows PowerShell and run this command (the Copy install command button copies it):",
            "  " + ClaudeLauncher.InstallCommand,
            string.Empty,
            "Then run claude once in PowerShell to log in. Come back here, close this notice and press Ctrl+Shift+R.",
            string.Empty,
            "If Claude Code is installed somewhere else, use Locate claude.exe. AxClaude remembers the path.",
            string.Empty,
            "Install page: " + ClaudeLauncher.InstallUrl,
        ]);
        return string.Join("\n", lines);
    }
}

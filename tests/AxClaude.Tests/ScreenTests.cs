using AxClaude.Core.Transcript;

namespace AxClaude.Tests;

/// <summary>Screens of Claude's that wait on a key-hint row or a tab row rather than a prompt row (docs/claude-screens.md, D33). The rows are the ones Claude Code 2.1.281 drew.</summary>
public class ScreenTests
{
    [Fact]
    public void The_tasks_recording_waits_on_its_hint_row()
    {
        const string name = "tasks-screen-claude2.1.281-240x50.vt";
        var model = new SessionModel(240, 50);
        model.Feed(TestHelpers.Fixture(name));
        model.EndFrame();

        var screen = Assert.IsType<ClaudeScreen>(model.PendingScreen);
        Assert.True(model.PromptPending);
        Assert.Null(model.PendingQuestion);
        Assert.Equal("Background", screen.Title);
        Assert.Equal(["No tasks currently running"], screen.Lines);
        Assert.Equal(["Up", "Down", "Enter", "Escape"], screen.Keys.Select(k => k.Name));
        Assert.Equal(["\x1b[A", "\x1b[B", "\r", "\x1b"], screen.Keys.Select(k => k.Send));
        Assert.Equal(["select", "select", "view", "close"], screen.Keys.Select(k => k.Action));
    }

    [Fact]
    public void A_tab_screen_takes_its_lines_from_below_the_tab_row()
    {
        string[] rows =
        [
            "you: /status",
            "Settings  Status   Config   Usage   Stats",
            "Version: 2.1.281",
            "",
            "Model: Default (Opus 5.5 with 1M context)",
            "Esc to cancel",
        ];
        var screen = ScreenReader.Read(rows, 1)!;
        Assert.Equal("Tabs: Settings, Status, Config, Usage, Stats", screen.Title);
        Assert.Equal(["Version: 2.1.281", "Model: Default (Opus 5.5 with 1M context)"], screen.Lines);
        Assert.Equal(["Escape"], screen.Keys.Select(k => k.Name));
    }

    [Fact]
    public void Hint_rows_name_their_keys()
    {
        string[] autocompact = ["you: /autocompact", "Auto-compact window", "Current setting: 1m tokens", "Select auto-compact window:  auto", "←/→ to adjust · Enter to apply · Esc to cancel"];
        Assert.Equal(["Left", "Right", "Enter", "Escape"], ScreenReader.Read(autocompact, 4)!.Keys.Select(k => k.Name));

        string[] details = ["you: /tasks", "Shell details", "Status: running", "← to go back · Esc/Enter/Space to close · x to stop"];
        var shell = ScreenReader.Read(details, 3)!;
        Assert.Equal("Shell details", shell.Title);
        Assert.Equal(["Left", "Escape", "Enter", "Space", "x"], shell.Keys.Select(k => k.Name));
        Assert.Equal("stop", shell.Keys[^1].Action);

        string[] goal = ["you: /goal", "Goal", "No goal set", "/goal <condition> to set one", "Esc to dismiss"];
        Assert.Equal(["No goal set", "/goal <condition> to set one"], ScreenReader.Read(goal, 4)!.Lines);
    }

    [Fact]
    public void The_prompt_and_rows_without_escape_are_no_screen()
    {
        string[] idle = ["claude: done", "auto mode on (shift+tab to cycle)", "$"];
        Assert.Null(ScreenReader.Read(idle, 2));
        Assert.Null(ScreenReader.Keys("Enter to confirm"));
        Assert.Null(ScreenReader.Keys("Showing detailed transcript · ctrl+o to toggle"));
        Assert.Null(ScreenReader.Keys("Then run /rc to continue this session from your phone"));
    }
}

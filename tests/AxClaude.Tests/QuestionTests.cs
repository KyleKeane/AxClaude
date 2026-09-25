using AxClaude.Core.Transcript;

namespace AxClaude.Tests;

/// <summary>Reading Claude's questions off the screen and routing them to a dialog (SPEC.md D33). The rows are the ones Claude Code 2.1.281 drew.</summary>
public class QuestionTests
{
    private static readonly string[] TrustRows =
    [
        "[Screen Reader Mode: on via flag]",
        "Permission Required: Accessing workspace:",
        @"C:\Temp\example",
        "Quick safety check: Is this a project you created or one you trust?",
        "Claude Code'll be able to read, edit, and execute files here.",
        "Security guide",
        "y. Yes, I trust this folder",
        "n. No, exit",
        "Enter y/n:",
        "Enter to confirm · Esc to cancel",
    ];

    private static readonly string[] ModelRows =
    [
        "Claude Code v2.1.281",
        "you: /model",
        "Select model",
        "Switch between Claude models. Your pick becomes the default for new sessions.",
        "1. (selected) Default (recommended) — Opus 5.5 with 1M context · Best for everyday, complex tasks",
        "2. Opus (1M context) — Opus 5.5 with 1M context · Best for everyday, complex tasks",
        "3. Fable — Fable 5.1 · Most capable for your hardest and longest-running tasks",
        "4. Sonnet — Sonnet 5 · Efficient for routine tasks",
        "5. Haiku — Haiku 4.5 · Fastest for quick answers",
        "Select with numbers [1-5]. Then Enter to submit or Escape to cancel:",
        "○  effort ←/→ to adjust",
        "Use /fast to turn on Fast mode (Opus 5.5).",
        "Enter to set as default · s to use this session only · Esc to cancel",
        "",
        "",
    ];

    private static readonly string[] ColourRows =
    [
        "you: Use the AskUserQuestion tool to ask me which colour I prefer, offering red, green and blue.",
        " ☐ Colour",
        "Which colour do you prefer?",
        "1. Red — The colour red",
        "2. Green — The colour green",
        "3. Blue — The colour blue",
        "4. Other",
        "5. Chat about this",
        "Select with numbers [1-5]. Then Enter to submit or Escape to cancel:",
    ];

    [Fact]
    public void The_trust_dialog_is_a_yes_or_no_question_under_its_own_title()
    {
        var question = QuestionReader.Read(TrustRows, 8)!;
        Assert.Equal("Permission Required: Accessing workspace", question.Title);
        Assert.Equal([@"C:\Temp\example", "Quick safety check: Is this a project you created or one you trust?", "Claude Code'll be able to read, edit, and execute files here.", "Security guide"], question.Text);
        Assert.Equal(["y", "n"], question.Options.Select(o => o.Key));
        Assert.Equal("No, exit", question.Options[1].Text);
        Assert.Equal(["Enter to confirm · Esc to cancel"], question.Hints);
    }

    [Fact]
    public void The_model_picker_stops_at_the_echo_and_marks_the_current_model()
    {
        var question = QuestionReader.Read(ModelRows, 9)!;
        Assert.Equal("Select model", question.Title);
        Assert.Single(question.Text);
        Assert.Equal(["1", "2", "3", "4", "5"], question.Options.Select(o => o.Key));
        Assert.True(question.Options[0].Current);
        Assert.Equal("Default (recommended) — Opus 5.5 with 1M context · Best for everyday, complex tasks", question.Options[0].Text);
        Assert.False(question.Options[1].Current);
        Assert.Equal(3, question.Hints.Count);
    }

    [Fact]
    public void A_question_from_claude_loses_its_check_box()
    {
        var question = QuestionReader.Read(ColourRows, 8)!;
        Assert.Equal("Colour", question.Title);
        Assert.Equal(["Which colour do you prefer?"], question.Text);
        Assert.Equal(5, question.Options.Count);
        Assert.Empty(question.Hints);
    }

    [Fact]
    public void A_blank_row_inside_a_question_keeps_its_title()
    {
        string[] rows =
        [
            "you: /resume",
            "Resume session",
            "⌕ Search…",
            "example-project",
            "",
            "1. Reading notes.txt — 5 days ago · HEAD · 223.4KB",
            "2. Session naming — 5 days ago · HEAD · 221.6KB",
            "Select with numbers [1-2]. Then Enter to submit or Escape to cancel:",
            "Ctrl+A to show all projects · Ctrl+B to only show current branch · Type to search · Esc to cancel",
        ];
        var question = QuestionReader.Read(rows, 7)!;
        Assert.Equal("Resume session", question.Title);
        Assert.Equal(["⌕ Search…", "example-project"], question.Text);
        Assert.Equal(2, question.Options.Count);

        string[] apart = ["claude: an earlier reply", "", "", "Effort", "1. low", "2. high", "Select with numbers [1-2]. Then Enter to submit or Escape to cancel:"];
        Assert.Equal("Effort", QuestionReader.Read(apart, 6)!.Title);
    }

    /// <summary>Replays a recording with the app's quiet rule and returns the model and every distinct question it reported waiting.</summary>
    private static (SessionModel Model, List<Question> Seen) QuestionsIn(string name)
    {
        var bytes = TestHelpers.Fixture(name);
        var chunks = File.ReadAllLines(Path.Combine(TestHelpers.FixtureDirectory(), name + ".chunks.txt"))
            .Where(l => l.Length > 0 && char.IsDigit(l[0]))
            .Select(l => l.Split(' ').Select(int.Parse).ToArray());
        var model = new SessionModel(240, 50);
        var seen = new List<Question>();
        model.Changed += () =>
        {
            if (model.PendingQuestion is { } q && (seen.Count == 0 || seen[^1].Signature != q.Signature))
            {
                seen.Add(q);
            }
        };

        var previous = 0;
        foreach (var chunk in chunks)
        {
            if (chunk[2] - previous >= 100)
            {
                model.EndFrame();
            }

            model.Feed(bytes.AsSpan(chunk[0], chunk[1]));
            previous = chunk[2];
        }

        model.EndFrame();
        return (model, seen);
    }

    [Fact]
    public void A_tool_permission_prompt_gives_a_question_for_the_answer_notice()
    {
        var (model, seen) = QuestionsIn("permission-prompt-claude2.1.281-240x50.vt");
        var question = Assert.Single(seen);
        Assert.Equal("Permission Required: Create file", question.Title);
        Assert.Equal("Do you want to create permission-test.txt?", question.Text[^1]);
        Assert.Equal(["1", "2", "3"], question.Options.Select(o => o.Key));
        Assert.Equal("No", question.Options[2].Text);
        Assert.Equal(QuestionDialog.AnswerNotice, QuestionRouter.Route(question, null).Dialog);

        // Escape declined; the question is gone.
        Assert.False(model.PromptPending);
        Assert.Null(model.PendingQuestion);
    }

    [Fact]
    public void Several_questions_in_one_call_read_one_by_one_and_several_answers_go_to_the_message_field()
    {
        var (_, seen) = QuestionsIn("several-questions-claude2.1.281-240x50.vt");
        Assert.Equal(["Which colour do you prefer?", "Which fruits do you like?", "Review your answers"], seen.Select(q => q.Title));

        // One answer: the answer notice. The tab row above it and the message above that are not the question.
        var colour = seen[0];
        Assert.False(colour.AllowsSeveral);
        Assert.Equal(5, colour.Options.Count);
        Assert.Equal(QuestionDialog.AnswerNotice, QuestionRouter.Route(colour, null).Dialog);

        // Several answers: the answer notice with check boxes, which sends "1,3" (the recording typed 1, comma, 3 as
        // three keystrokes and the review shows "→ Apple, Cherry"); typed in the message field, "1,3" answers too.
        var fruit = seen[1];
        Assert.True(fruit.AllowsSeveral);
        Assert.Equal(QuestionDialog.AnswerNotice, QuestionRouter.Route(fruit, null).Dialog);
        Assert.True(fruit.Accepts("1,3"));
        Assert.True(fruit.Accepts("1, 3"));
        Assert.False(fruit.Accepts("1,9"));
        Assert.False(colour.Accepts("1,3"));

        var review = seen[2];
        Assert.Contains("→ Apple, Cherry", review.Text);
        Assert.Equal(["y", "n"], review.Options.Select(o => o.Key));
    }

    [Fact]
    public void Other_asks_for_the_users_own_words()
    {
        // The recording answered 4 (Other), then typed t, u, l, i, p one keystroke each; Claude took "tulip".
        const string name = "other-answer-claude2.1.281-240x50.vt";
        var bytes = TestHelpers.Fixture(name);
        var model = new SessionModel(240, 50);
        var awaited = false;
        model.Changed += () => awaited |= model.AwaitsText && model.PromptPending && model.PendingQuestion is null;
        // The "Enter text for" row comes without a frame bracket: the app's quiet rule ends that frame.
        var previous = 0;
        foreach (var chunk in File.ReadAllLines(Path.Combine(TestHelpers.FixtureDirectory(), name + ".chunks.txt")).Where(l => l.Length > 0 && char.IsDigit(l[0])).Select(l => l.Split(' ').Select(int.Parse).ToArray()))
        {
            if (chunk[2] - previous >= 100)
            {
                model.EndFrame();
            }

            model.Feed(bytes.AsSpan(chunk[0], chunk[1]));
            previous = chunk[2];
        }

        model.EndFrame();
        Assert.True(awaited);
        Assert.False(model.AwaitsText);
        Assert.True(LineClassifier.IsTextPrompt("Enter text for option 4 (Other), or Escape for the list:"));
    }

    [Fact]
    public void A_permission_question_starts_at_its_permission_row()
    {
        // Claude Code 2.1.281, --permission-mode default: the tip inside it is skipped, the tool rows above it are not
        // part of it.
        string[] bash =
        [
            "tool: Bash (echo hello-permission > perm-probe.txt)",
            "Waiting…",
            "Permission Required: Bash command",
            "Tip: auto mode handles these prompts for you — choose \"switch to auto mode\" below",
            "echo hello-permission > perm-probe.txt",
            "Write hello-permission to a probe file",
            "Do you want to proceed?",
            "1. Yes",
            "2. Yes, and always allow access to C:\\Temp\\example from this project",
            "3. Yes, and switch to auto mode — · auto mode handles these prompts for you",
            "4. No",
            "Select with numbers [1-4]. Then Enter to submit or Escape to cancel:",
            "Esc to cancel · Tab to amend",
        ];
        var question = QuestionReader.Read(bash, 11)!;
        Assert.Equal("Permission Required: Bash command", question.Title);
        Assert.Equal(["echo hello-permission > perm-probe.txt", "Write hello-permission to a probe file", "Do you want to proceed?"], question.Text);
        Assert.Equal(2, question.FirstRow);

        // Plan approval (--permission-mode plan): a row left over from the tool call above it is not the title, and the
        // numbered steps of the plan are text, not answers.
        string[] plan =
        [
            "tool: Updated plan",
            "/plan to preview",
            "Permission Required: Ready to code?",
            "Here is Claude's plan:",
            "Steps",
            "1. Use the Write tool to create plan-test.txt with the content hi.",
            "Claude has written up a plan and is ready to execute. Would you like to proceed?",
            "1. Yes, and use auto mode",
            "2. Yes, manually approve edits",
            "3. No, keep planning — shift+tab to approve with this feedback",
            "Select with numbers [1-3]. Then Enter to submit or Escape to cancel:",
        ];
        var approval = QuestionReader.Read(plan, 10)!;
        Assert.Equal("Permission Required: Ready to code?", approval.Title);
        Assert.Equal(3, approval.Options.Count);
        Assert.Contains("1. Use the Write tool to create plan-test.txt with the content hi.", approval.Text);
    }

    [Fact]
    public void A_question_without_text_rows_starts_at_its_top_answer()
    {
        string[] rows = ["claude: Pick one.", "1. Red", "2. Green", "Select with numbers [1-2]. Then Enter to submit or Escape to cancel:"];
        var question = QuestionReader.Read(rows, 3)!;
        Assert.Equal(1, question.FirstRow);
        Assert.Empty(question.Text);
    }

    [Fact]
    public void A_question_is_recorded_in_front_of_its_first_row_once()
    {
        const string Esc = "\x1b";
        var model = new SessionModel(80, 12);
        TestHelpers.Feed(model, $"{Esc}[?25lyou: pick one{Esc}[K\r\n ☐ Colour{Esc}[K\r\nWhich colour?{Esc}[K\r\n1. Red{Esc}[K\r\n2. Green{Esc}[K\r\nSelect with numbers [1-2]. Then Enter to submit or Escape to cancel:{Esc}[K{Esc}[?25h");
        Assert.NotNull(model.PendingQuestion);
        model.MarkQuestion();
        model.MarkQuestion();
        var visible = TestHelpers.Visible(model);
        Assert.Single(visible, t => t == "Question: Colour");
        Assert.Equal(visible.IndexOf("Question: Colour") + 1, visible.IndexOf(" ☐ Colour"));

        // Claude redraws the answered question as its result: the record stays in front of it.
        TestHelpers.Feed(model, $"{Esc}[?25l{Esc}[2;1H●  User answered Claude's questions:{Esc}[K\r\n· Which colour? → Green{Esc}[K\r\n{Esc}[K\r\n{Esc}[K\r\n{Esc}[K\r\n${Esc}[K{Esc}[?25h");
        visible = TestHelpers.Visible(model);
        Assert.Equal(visible.IndexOf("Question: Colour") + 1, visible.IndexOf("●  User answered Claude's questions:"));
    }

    [Fact]
    public void A_warning_belongs_to_the_question_it_is_in()
    {
        string[] rows =
        [
            "←   ☒ Colour   ☐ Fruit   ✔ Submit   →",
            "Review your answers",
            "warning: You have not answered all questions",
            "Which colour do you prefer?",
            "→ Green",
            "Ready to submit your answers?",
            "y. Submit answers",
            "n. Cancel",
            "Enter y/n:",
        ];
        var review = QuestionReader.Read(rows, 8)!;
        Assert.Equal("Review your answers", review.Title);
        Assert.Equal("warning: You have not answered all questions", review.Text[0]);
    }

    [Fact]
    public void The_model_picker_recording_gives_a_pending_question_for_the_answer_notice()
    {
        var (model, seen) = QuestionsIn("model-picker-claude2.1.281-240x50.vt");

        // One question while the picker was open, read the same way however often it was redrawn.
        var question = Assert.Single(seen);
        Assert.Equal("Select model", question.Title);
        Assert.Equal(["1", "2", "3", "4", "5"], question.Options.Select(o => o.Key));
        Assert.True(question.Options[0].Current);
        Assert.Equal(QuestionDialog.AnswerNotice, QuestionRouter.Route(question, SlashCommands.Find("/model")).Dialog);

        // The recording's Escape went out a millisecond before Ctrl+C and was not taken as a key of its own: the
        // picker was still open when the recorder stopped Claude.
        Assert.True(model.PromptPending);
        Assert.Equal(question.Signature, model.PendingQuestion?.Signature);
    }

    [Fact]
    public void Only_a_prompt_row_holds_a_question()
    {
        Assert.Null(QuestionReader.Read(ModelRows, 8));
        Assert.Null(QuestionReader.Read(ModelRows, 40));
    }

    [Fact]
    public void Commands_are_found_by_name_with_or_without_arguments()
    {
        Assert.Equal("model", SlashCommands.Find("/model")?.Name);
        Assert.Equal("model", SlashCommands.Find("  /Model sonnet")?.Name);
        Assert.Null(SlashCommands.Find("model"));
        Assert.Null(SlashCommands.Find("/no-such-command"));
        Assert.Null(SlashCommands.Find("/"));
    }

    [Fact]
    public void Every_command_name_and_alias_is_listed_once()
    {
        var names = SlashCommands.All.SelectMany(c => c.Aliases.Prepend(c.Name)).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.All(names, name => Assert.Matches("^[a-z][a-z-]*$", name));
        Assert.Equal("config", SlashCommands.Find("/settings")?.Name);
    }

    [Fact]
    public void A_list_of_answers_goes_to_the_answer_notice()
    {
        var question = QuestionReader.Read(ModelRows, 9)!;
        Assert.Equal(QuestionDialog.AnswerNotice, QuestionRouter.Route(question, SlashCommands.Find("/model")).Dialog);
        Assert.Equal(QuestionDialog.AnswerNotice, QuestionRouter.Route(question, SlashCommands.Find("/config")).Dialog);
        Assert.Equal(QuestionDialog.AnswerNotice, QuestionRouter.Route(QuestionReader.Read(TrustRows, 8)!, null).Dialog);
    }

    [Fact]
    public void A_panel_command_leaves_its_lists_to_the_message_field()
    {
        var question = QuestionReader.Read(ModelRows, 9)!;
        var panel = SlashCommands.Find("/permissions");
        Assert.Equal(QuestionDialog.MessageField, QuestionRouter.Route(question, panel).Dialog);
        Assert.Equal(QuestionDialog.AnswerNotice, QuestionRouter.Route(question with { AllowsSeveral = true }, null).Dialog);
    }

    [Fact]
    public void The_config_list_is_a_question_titled_by_its_prompt_row()
    {
        // Recorded with Claude Code 2.1.282: 44 settings under the tab row, the cursor on the prompt row.
        var rows = new List<string> { "you: /config", "Settings  Status   Config   Usage   Stats" };
        rows.AddRange(Enumerable.Range(1, 44).Select(n => n == 4 ? "4. Show tips: true" : $"{n}. Setting {n}: false"));
        rows.Add("Enter a number to change [1-44], or Escape to save and close:");

        var question = QuestionReader.Read(rows, rows.Count - 1)!;
        Assert.Equal("Enter a number to change [1-44], or Escape to save and close", question.Title);
        Assert.Empty(question.Text);
        Assert.Equal(44, question.Options.Count);
        Assert.Equal("Show tips: true", question.OptionFor("4")?.Text);
        Assert.Equal(2, question.FirstRow);
        Assert.True(question.Accepts("44"));
        Assert.True(question.IsSettings);
        Assert.False(QuestionReader.Read(ModelRows, 9)!.IsSettings);
    }

    [Theory]
    [InlineData("Show tips: true", "Show tips", "true", true)]
    [InlineData("Reduce motion: false", "Reduce motion", "false", false)]
    [InlineData("Fast mode (Opus 5.5): false", "Fast mode (Opus 5.5)", "false", false)]
    [InlineData("Thinking mode: true (Thinking can't be turned off for Opus 5.5)", "Thinking mode", "true (Thinking can't be turned off for Opus 5.5)", true)]
    [InlineData("Time format: auto", "Time format", "auto", null)]
    [InlineData("Project instructions: claude-md-or-agents-md", "Project instructions", "claude-md-or-agents-md", null)]
    [InlineData("Switch models when a message is flagged: Not asked yet", "Switch models when a message is flagged", "Not asked yet", null)]
    public void A_config_row_is_a_setting_with_a_value(string text, string name, string value, bool? on)
    {
        var option = new QuestionOption("4", text);
        Assert.Equal((name, value), option.Setting);
        Assert.Equal(on, option.Switch);
    }

    [Fact]
    public void A_prompt_row_without_answers_above_it_is_no_question()
    {
        // Claude clears a question's rows for a frame while it redraws it.
        string[] rows = ["Which fruits do you like?", "", "", "", "Select with numbers [1-4]. Then Enter to submit or Escape to cancel:"];
        Assert.Null(QuestionReader.Read(rows, 4));
    }
}

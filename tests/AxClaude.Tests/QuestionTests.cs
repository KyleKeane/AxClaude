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
    public void A_list_of_answers_goes_to_the_answer_notice_with_the_command_keys()
    {
        var question = QuestionReader.Read(ModelRows, 9)!;
        var route = QuestionRouter.Route(question, SlashCommands.Find("/model"));
        Assert.Equal(QuestionDialog.AnswerNotice, route.Dialog);
        Assert.Equal(["s"], route.ExtraKeys.Select(k => k.Send));

        Assert.Equal(QuestionDialog.AnswerNotice, QuestionRouter.Route(QuestionReader.Read(TrustRows, 8)!, null).Dialog);
    }

    [Fact]
    public void Panels_and_questions_without_answers_stay_in_the_message_field()
    {
        var question = QuestionReader.Read(ModelRows, 9)!;
        var panel = new SlashCommand("config", CommandInteraction.Panel, "docs");
        Assert.Equal(QuestionDialog.MessageField, QuestionRouter.Route(question, panel).Dialog);

        var bare = question with { Options = [] };
        Assert.Equal(QuestionDialog.MessageField, QuestionRouter.Route(bare, null).Dialog);
    }
}

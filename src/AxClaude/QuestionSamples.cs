using AxClaude.Core.Transcript;

namespace AxClaude;

/// <summary>
/// TRIAL ONLY (branch answer-notice): three questions as Claude Code 2.1.281 printed them in screen reader mode, for
/// Help → Try the answer notice. Nothing is sent to Claude from them. Remove this file and the menu entry once the
/// notice opens for Claude's real questions.
/// </summary>
internal static class QuestionSamples
{
    public static readonly (string Name, Question Question)[] All =
    [
        ("&Workspace trust at startup", new Question(
            "Permission Required: Accessing workspace:",
            [
                @"C:\Users\Kyle\Desktop\example",
                "Quick safety check: Is this a project you created or one you trust? (Like your own code, a well-known open source project, or work from your team). If not, take a moment to review what's in this folder first.",
                "Claude Code'll be able to read, edit, and execute files here.",
                "Security guide",
            ],
            [
                new QuestionOption("y", "Yes, I trust this folder"),
                new QuestionOption("n", "No, exit"),
            ],
            ["Enter to confirm · Esc to cancel"])),

        ("The /&model picker", new Question(
            "Select model",
            ["Switch between Claude models. Your pick becomes the default for new sessions. For other/previous model names, specify with --model."],
            [
                new QuestionOption("1", "Default (recommended) — Opus 5.5 with 1M context · Best for everyday, complex tasks", Current: true),
                new QuestionOption("2", "Opus (1M context) — Opus 5.5 with 1M context · Best for everyday, complex tasks"),
                new QuestionOption("3", "Fable — Fable 5.1 · Most capable for your hardest and longest-running tasks"),
                new QuestionOption("4", "Sonnet — Sonnet 5 · Efficient for routine tasks"),
                new QuestionOption("5", "Haiku — Haiku 4.5 · Fastest for quick answers"),
            ],
            [
                "○  effort ←/→ to adjust",
                "Use /fast to turn on Fast mode (Opus 5.5).",
                "Enter to set as default · s to use this session only · Esc to cancel",
            ])),

        ("A &question from Claude", new Question(
            "Colour",
            ["Which colour do you prefer?"],
            [
                new QuestionOption("1", "Red — The colour red"),
                new QuestionOption("2", "Green — The colour green"),
                new QuestionOption("3", "Blue — The colour blue"),
                new QuestionOption("4", "Other"),
                new QuestionOption("5", "Chat about this"),
            ],
            [])),
    ];
}

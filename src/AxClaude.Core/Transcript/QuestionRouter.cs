namespace AxClaude.Core.Transcript;

/// <summary>Where the app presents a question Claude waits on.</summary>
public enum QuestionDialog
{
    /// <summary>The answer notice: one answer out of a list, chosen with its key or the arrow keys.</summary>
    AnswerNotice,

    /// <summary>
    /// No dialog of the app's own: the question stays in the conversation and is answered from the message field
    /// (several answers at once, and the lists of settings panels the app cannot show yet).
    /// </summary>
    MessageField,
}

/// <summary>The dialog for a question, the slash command it answers (null when none led to it), and the extra keys that command's screen takes.</summary>
public sealed record QuestionRoute(QuestionDialog Dialog, SlashCommand? Command, IReadOnlyList<CommandKey> ExtraKeys);

/// <summary>
/// Decides how a question Claude waits on is presented (SPEC.md D33). A question with one answer to give goes to the
/// answer notice. It stays in the message field when it takes several answers (the notice gives one), or when the
/// slash command sent last opens more than a list (<see cref="CommandInteraction.Multi"/>,
/// <see cref="CommandInteraction.Viewer"/>, <see cref="CommandInteraction.Panel"/>, docs/claude-screens.md). A
/// picker brings its extra keys (<c>s</c> in <c>/model</c>).
/// </summary>
public static class QuestionRouter
{
    public static QuestionRoute Route(Question question, SlashCommand? lastCommand)
    {
        var inField = question.AllowsSeveral
            || lastCommand is { Interaction: CommandInteraction.Multi or CommandInteraction.Viewer or CommandInteraction.Panel };
        return new QuestionRoute(inField ? QuestionDialog.MessageField : QuestionDialog.AnswerNotice, lastCommand, lastCommand?.Keys ?? []);
    }
}

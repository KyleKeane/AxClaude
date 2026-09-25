namespace AxClaude.Core.Transcript;

/// <summary>Where the app presents a question Claude waits on.</summary>
public enum QuestionDialog
{
    /// <summary>The answer notice: one answer out of a list, or several ticked, chosen with their keys or the arrow keys.</summary>
    AnswerNotice,

    /// <summary>
    /// No dialog of the app's own: the question stays in the conversation and is answered from the message field
    /// (the lists of settings panels the app cannot show yet).
    /// </summary>
    MessageField,
}

/// <summary>The dialog for a question and the slash command it answers (null when none led to it).</summary>
public sealed record QuestionRoute(QuestionDialog Dialog, SlashCommand? Command);

/// <summary>
/// Decides how a question Claude waits on is presented (SPEC.md D33). A list of answers goes to the answer notice (radio
/// buttons, or check boxes when it takes several). It stays in the message field when the slash command sent last
/// opens more than a list (<see cref="CommandInteraction.Multi"/>, <see cref="CommandInteraction.Viewer"/>,
/// <see cref="CommandInteraction.Panel"/>, docs/claude-screens.md).
/// </summary>
public static class QuestionRouter
{
    public static QuestionRoute Route(Question question, SlashCommand? lastCommand)
    {
        var inField = lastCommand is { Interaction: CommandInteraction.Multi or CommandInteraction.Viewer or CommandInteraction.Panel };
        return new QuestionRoute(inField ? QuestionDialog.MessageField : QuestionDialog.AnswerNotice, lastCommand);
    }
}

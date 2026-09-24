namespace AxClaude.Core.Transcript;

/// <summary>Where the app presents a question Claude waits on.</summary>
public enum QuestionDialog
{
    /// <summary>The answer notice: one answer out of a list, chosen with its key or the arrow keys.</summary>
    AnswerNotice,

    /// <summary>
    /// No dialog of the app's own: the question stays in the conversation and is answered from the message field
    /// (typed answers, and Claude's screens the app cannot show yet: multi-select lists, settings panels).
    /// </summary>
    MessageField,
}

/// <summary>The dialog for a question, the slash command it answers (null when none led to it), and the extra keys that command's screen takes.</summary>
public sealed record QuestionRoute(QuestionDialog Dialog, SlashCommand? Command, IReadOnlyList<CommandKey> ExtraKeys);

/// <summary>
/// Decides how a question Claude waits on is presented (SPEC.md D33). The question's shape decides first: a list of
/// answers goes to the answer notice, anything else to the message field. The slash command sent last refines it:
/// a command whose screen is more than one list (<see cref="CommandInteraction.Multi"/>,
/// <see cref="CommandInteraction.Viewer"/>, <see cref="CommandInteraction.Panel"/>) stays in the message field
/// until it has a dialog of its own (the screen notice, docs/claude-screens.md), and a
/// picker brings its extra keys (<c>s</c> in <c>/model</c>).
/// </summary>
public static class QuestionRouter
{
    public static QuestionRoute Route(Question question, SlashCommand? lastCommand)
    {
        var keys = lastCommand?.Keys ?? [];
        if (question.Options.Count == 0)
        {
            return new QuestionRoute(QuestionDialog.MessageField, lastCommand, keys);
        }

        if (lastCommand is { Interaction: CommandInteraction.Multi or CommandInteraction.Viewer or CommandInteraction.Panel })
        {
            return new QuestionRoute(QuestionDialog.MessageField, lastCommand, keys);
        }

        return new QuestionRoute(QuestionDialog.AnswerNotice, lastCommand, keys);
    }
}

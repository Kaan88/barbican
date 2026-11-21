// Mirrors: the add-on chat panel contract (decision D-M6b-08).

namespace Barbican.Plugins;

/// <summary>The outcome of one question: the answer text, or an error message the panel shows.</summary>
/// <param name="Error">Null when the question was answered.</param>
/// <param name="Text">The answer when there is no error.</param>
public sealed record PluginChatReply(string? Error, string Text)
{
    /// <summary>An answer.</summary>
    public static PluginChatReply Answer(string text) => new(null, text);

    /// <summary>A failure the user reads.</summary>
    public static PluginChatReply Failure(string error) => new(error, string.Empty);
}

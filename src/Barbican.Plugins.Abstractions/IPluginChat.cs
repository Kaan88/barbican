// Mirrors: Rust assistant column (B-AI-006 Clear, B-AI-010 "this email"); the add-on chat panel contract (decision D-M6b-08). THREADING: SendAsync runs off the UI thread; Changed is raised on a pool thread.

namespace Barbican.Plugins;

/// <summary>A conversation the host shows in its generic add-on chat panel. The panel draws the turns, an input, Send and Clear; the add-on owns everything else.</summary>
public interface IPluginChat
{
    /// <summary>The panel heading ("Assistant").</summary>
    string Title { get; }

    /// <summary>The hint of the empty input.</summary>
    string InputPlaceholder { get; }

    /// <summary>Shown while the conversation is empty.</summary>
    string EmptyHint { get; }

    /// <summary>Loads the saved conversation of the account off the UI thread.</summary>
    Task InitializeAsync(string accountKey, CancellationToken ct);

    /// <summary>The visible turns so far (a memory read).</summary>
    IReadOnlyList<PluginChatTurn> Turns(string accountKey);

    /// <summary>Sends a message. <paramref name="openMessageId"/> is the message open in the reader ("this email"), or null.</summary>
    Task<PluginChatReply> SendAsync(string accountKey, string text, string? openMessageId, CancellationToken ct);

    /// <summary>Forgets the conversation of the account.</summary>
    Task ClearAsync(string accountKey, CancellationToken ct);

    /// <summary>Raised when the turns changed or progress arrived, on a pool thread.</summary>
    event EventHandler? Changed;
}

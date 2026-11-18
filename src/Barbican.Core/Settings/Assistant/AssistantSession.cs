// Mirrors: see IAssistantSession. Stub from the M6 plan section 4.4; owner set-data replaces the class (constructor dependencies: IAssistantClient, IAssistantTools, SettingsStore, IProfilePaths).

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Assistant;

/// <summary>The default <see cref="IAssistantSession"/>.</summary>
public sealed class AssistantSession : IAssistantSession
{
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public IReadOnlyList<AssistantMessage> History(string accountKey) => throw new PortPendingException("AssistantSession.History");

    public Task<Result<string>> AskAsync(string accountKey, string question, string? openMessageId, CancellationToken ct) => throw new PortPendingException("AssistantSession.AskAsync");

    public Task<Result<Unit>> ClearAsync(string accountKey, CancellationToken ct) => throw new PortPendingException("AssistantSession.ClearAsync");
}

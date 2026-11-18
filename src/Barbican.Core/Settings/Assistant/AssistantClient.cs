// Mirrors: see IAssistantClient. Stub from the M6 plan section 4.4; owner set-data replaces the class (constructor dependencies: HttpClient factory, ISecretStore).

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Assistant;

/// <summary>The default <see cref="IAssistantClient"/>.</summary>
public sealed class AssistantClient : IAssistantClient
{
    public Task<Result<AssistantMessage>> CompleteAsync(AssistantConfig config, IReadOnlyList<AssistantMessage> messages, IReadOnlyList<AssistantToolSpec> tools, CancellationToken ct) => throw new PortPendingException("AssistantClient.CompleteAsync");
}

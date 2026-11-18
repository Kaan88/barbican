// Mirrors: see IAssistantTools. Stub from the M6 plan section 4.4; owner set-data replaces the class (constructor dependencies: IAccountManager, IMailService, IVacationService, IFilterService, SettingValues).

using System.Text.Json.Nodes;

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Assistant;

/// <summary>The default <see cref="IAssistantTools"/>.</summary>
public sealed class AssistantTools : IAssistantTools
{
    public IReadOnlyList<AssistantToolSpec> Specs(AssistantActionMode mode) => throw new PortPendingException("AssistantTools.Specs");

    public Task<string> RunAsync(string accountKey, string name, JsonObject args, CancellationToken ct) => throw new PortPendingException("AssistantTools.RunAsync");
}

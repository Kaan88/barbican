// Mirrors: Bulwark host-api-input.ts sessionClient, strengthened by account-generation fencing. Host-only, never a wire value.
using System.Text.Json.Serialization;
using Barbican.Core.Accounts;
namespace Barbican.Core.Plugins.Bridge;
public sealed class PluginSessionCapture
{
    public PluginSessionCapture(IAccountSession session, long? activeSelectionGeneration)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (activeSelectionGeneration is <= 0) throw new ArgumentOutOfRangeException(nameof(activeSelectionGeneration));
        Session = session;
        ActiveSelectionGeneration = activeSelectionGeneration;
    }
    [JsonIgnore]
    public IAccountSession Session { get; }
    public string LocalAccountKey => Session.Account.Key;
    public long? ActiveSelectionGeneration { get; }
}

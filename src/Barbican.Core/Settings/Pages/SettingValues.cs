// Mirrors: see ISettingValues. Stub from the M6 plan section 4.2; owner set-data replaces the class (constructor dependencies: IPrefsStore, IHookBus).

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Settings.Pages;

/// <summary>The default <see cref="ISettingValues"/>.</summary>
public sealed class SettingValues : ISettingValues
{
    public event EventHandler<SettingChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }

    public JsonNode? Stored(string key) => throw new PortPendingException("SettingValues.Stored");

    public Task<Result<Unit>> SetAsync(string key, JsonNode? value, CancellationToken ct) => throw new PortPendingException("SettingValues.SetAsync");
}

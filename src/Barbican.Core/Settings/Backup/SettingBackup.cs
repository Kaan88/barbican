// Mirrors: see ISettingBackup. Stub from the M6 plan section 4.3; owner set-data replaces the class (constructor dependencies: SettingsStore, IHookBus).

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Settings.Backup;

/// <summary>The default <see cref="ISettingBackup"/>.</summary>
public sealed class SettingBackup : ISettingBackup
{
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public Task<Result<JsonObject>> ExportAsync(CancellationToken ct) => throw new PortPendingException("SettingBackup.ExportAsync");

    public Task<Result<Unit>> ImportAsync(JsonNode document, CancellationToken ct) => throw new PortPendingException("SettingBackup.ImportAsync");

    public Task<Result<Unit>> ResetAsync(CancellationToken ct) => throw new PortPendingException("SettingBackup.ResetAsync");
}

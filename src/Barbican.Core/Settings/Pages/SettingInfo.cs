// Mirrors: see ISettingInfo. Stub from the M6 plan section 4.2; owner set-data replaces the class.



using Barbican.Core.Common;

namespace Barbican.Core.Settings.Pages;

/// <summary>The default <see cref="ISettingInfo"/>.</summary>
public sealed class SettingInfo : ISettingInfo
{
    public bool Provides(string rowId) => throw new PortPendingException("SettingInfo.Provides");

    public Task<string> TextAsync(string rowId, CancellationToken ct) => throw new PortPendingException("SettingInfo.TextAsync");
}

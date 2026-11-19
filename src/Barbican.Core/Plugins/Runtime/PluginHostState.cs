// Mirrors: Bulwark protocol.ts regional/theme/settings/host snapshots. Host-only data must already be projected.
using System.Text.Json;
namespace Barbican.Core.Plugins.Runtime;
public sealed class PluginHostState
{
    public PluginHostState(string locale, JsonElement regional, JsonElement theme, JsonElement settings, JsonElement features)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        Locale = locale;
        Regional = regional.Clone();
        Theme = theme.Clone();
        Settings = settings.Clone();
        Features = features.Clone();
    }
    public string Locale { get; }
    public JsonElement Regional { get; }
    public JsonElement Theme { get; }
    public JsonElement Settings { get; }
    public JsonElement Features { get; }
}

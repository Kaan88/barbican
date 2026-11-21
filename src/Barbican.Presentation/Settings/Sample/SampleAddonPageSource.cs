// Mirrors: M6 plan section 3.5 (D-M6-00); decision D-M6b-08. Sample data for the add-on settings page and chat (BARBICAN_SAMPLE=1, view-model tests): in-memory rows, settings and secrets, a chat that echoes. Never real configuration.

using Barbican.Presentation.Settings.Addons;
using Barbican.Plugins;

namespace Barbican.Presentation.Settings.Sample;

/// <summary>An <see cref="IAddonPageSource"/> with one sample add-on page for any id.</summary>
public sealed class SampleAddonPageSource : IAddonPageSource
{
    private static readonly IReadOnlyList<PluginSettingRow> Rows =
    [
        new("provider", "Provider", "Sample provider choice.", PluginSettingKind.Choice, [new PluginChoice("0", "First"), new PluginChoice("1", "Second")], Default: "0"),
        new("model", "Model", "Sample text row.", PluginSettingKind.Text, Placeholder: "Enter a model ID"),
        new("memory", "Remember notes", "Sample toggle row.", PluginSettingKind.Toggle, Default: "true"),
        new("api-key", "API key", "Sample secret row.", PluginSettingKind.Secret, Placeholder: "API key"),
    ];

    private readonly SampleAddonChat _chat = new();
    private readonly MemorySettings _settings = new();

    /// <inheritdoc/>
    public AddonPage? Find(string pluginId) => new(pluginId, "Sample add-on", "Sample data, not a real add-on.", Rows, _settings, _settings, (_, _) => new Dictionary<string, string?>(StringComparer.Ordinal), _chat);

    private sealed class MemorySettings : IPluginSettings, IPluginSecrets
    {
        private readonly Lock _lock = new();
        private readonly Dictionary<(string, string), string> _values = [];

        public event EventHandler? Changed;

        public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;

        public string? Read(string accountKey, string key)
        {
            lock (_lock)
            {
                return _values.TryGetValue((accountKey, key), out var value) ? value : null;
            }
        }

        public async Task SetAsync(string accountKey, IReadOnlyDictionary<string, string?> values, CancellationToken ct)
        {
            await SampleThreading.OffThreadAsync(() =>
            {
                lock (_lock)
                {
                    foreach (var (key, value) in values)
                    {
                        if (value is null) _ = _values.Remove((accountKey, key));
                        else _values[(accountKey, key)] = value;
                    }
                }

                return 0;
            }).ConfigureAwait(false);
            SampleThreading.RaiseOnPool(() => Changed?.Invoke(this, EventArgs.Empty));
        }

        public Task<string?> ReadAsync(string name, CancellationToken ct) => Task.FromResult(Read("secret", name));

        public Task WriteAsync(string name, string? value, CancellationToken ct) => SetAsync("secret", new Dictionary<string, string?>(StringComparer.Ordinal) { [name] = string.IsNullOrEmpty(value) ? null : value }, ct);
    }
}

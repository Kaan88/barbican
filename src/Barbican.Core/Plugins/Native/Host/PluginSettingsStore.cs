// Mirrors: Rust ai.rs:25-65 (Config load and save), settings.rs:160 (the desktop file is settings.json beside prefs.json); D-M6-A08, D-M6-A13 (one object per account, a shared write gate); decision D-M6b-08.
// One object per add-on and account under "addons" in settings.json; other keys of the file are kept as they are. An add-on whose id names a section of that file from before add-ons existed (the assistant's "assistant")
// reads that section until its first save, then keeps its own: values are never lost and the old section is left in place. Values are text; numbers and booleans of the old shape read as their text.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Settings.Prefs;
using Barbican.Core.Storage;
using Barbican.Plugins;

namespace Barbican.Core.Plugins.Native.Host;

/// <summary>The per-account settings of one add-on, in the profile's desktop settings file.</summary>
public sealed class PluginSettingsStore : IPluginSettings, IDisposable
{
    private const string Section = "addons";

    private readonly Lock _lock = new();
    private readonly IProfilePaths _paths;
    private readonly SettingWriteGate _gate;
    private readonly bool _ownsGate;
    private readonly string _pluginId;
    private JsonObject _file = [];
    private bool _initialized;

    /// <summary>Creates the store of the add-on <paramref name="pluginId"/>; a missing <paramref name="gate"/> makes a private one.</summary>
    public PluginSettingsStore(IProfilePaths paths, string pluginId, SettingWriteGate? gate = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrEmpty(pluginId);
        _paths = paths;
        _pluginId = pluginId;
        _gate = gate ?? new SettingWriteGate();
        _ownsGate = gate is null;
        _gate.DesktopChanged += OnDesktopChanged;
    }

    /// <inheritdoc/>
    public event EventHandler? Changed;

    private string FilePath => Path.Combine(_paths.ConfigDir, SettingsStore.DesktopFileName);

    /// <inheritdoc/>
    public void Dispose()
    {
        _gate.DesktopChanged -= OnDesktopChanged;
        if (_ownsGate) _gate.Dispose();
    }

    /// <inheritdoc/>
    public string? Read(string accountKey, string key)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(key);
        lock (_lock)
        {
            return Text(AccountObject(_file, accountKey)?[key]);
        }
    }

    /// <inheritdoc/>
    public Task InitializeAsync(CancellationToken ct) => Task.Run(() => InitializeCoreAsync(ct), ct);

    /// <inheritdoc/>
    public Task SetAsync(string accountKey, IReadOnlyDictionary<string, string?> values, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(values);
        return Task.Run(() => SetCoreAsync(accountKey, values, ct), ct);
    }

    private static JsonObject? AccountObject(JsonObject file, string pluginId, string accountKey)
    {
        var own = (file[Section] as JsonObject)?[pluginId] as JsonObject;
        return (own ?? file[pluginId] as JsonObject)?[accountKey] as JsonObject;
    }

    private static string? Text(JsonNode? node) => node switch
    {
        null => null,
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v when v.TryGetValue<bool>(out var b) => b ? "true" : "false",
        JsonValue v when v.TryGetValue<long>(out var n) => n.ToString(CultureInfo.InvariantCulture),
        _ => node.ToJsonString(),
    };

    private JsonObject? AccountObject(JsonObject file, string accountKey) => AccountObject(file, _pluginId, accountKey);

    private async Task InitializeCoreAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        var loaded = false;
        try
        {
            bool needsLoad;
            lock (_lock) { needsLoad = !_initialized; }
            if (needsLoad)
            {
                var file = ReadFile();
                lock (_lock)
                {
                    _file = file;
                    _initialized = true;
                }

                loaded = true;
            }
        }
        finally
        {
            _gate.Release();
        }

        if (loaded) Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task SetCoreAsync(string accountKey, IReadOnlyDictionary<string, string?> values, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            bool needsLoad;
            lock (_lock) { needsLoad = !_initialized; }
            if (needsLoad)
            {
                var existing = ReadFile();
                lock (_lock)
                {
                    _file = existing;
                    _initialized = true;
                }
            }

            JsonObject file;
            lock (_lock) { file = (JsonObject)_file.DeepClone(); }

            var account = AccountObject(file, accountKey) is { } current ? (JsonObject)current.DeepClone() : [];
            foreach (var (key, value) in values)
            {
                account[key] = value is null ? null : JsonValue.Create(value);
            }

            var addons = file[Section] as JsonObject ?? [];
            var own = addons[_pluginId] as JsonObject ?? (file[_pluginId] as JsonObject is { } legacy ? (JsonObject)legacy.DeepClone() : []);
            own[accountKey] = account;
            addons[_pluginId] = own;
            file[Section] = addons;
            AtomicFile.WriteAllText(FilePath, file.ToJsonString());
            _gate.PublishDesktop(file);
        }
        catch (IOException e)
        {
            throw new IOException("Could not save the add-on settings: " + e.Message, e);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnDesktopChanged(object? sender, SettingDesktopChangedEventArgs e)
    {
        lock (_lock)
        {
            _file = (JsonObject)e.Desktop.DeepClone();
            _initialized = true;
        }

        ThreadPool.QueueUserWorkItem(_ => Changed?.Invoke(this, EventArgs.Empty));
    }

    private JsonObject ReadFile()
    {
        try
        {
            return AtomicFile.ReadAllTextOrNull(FilePath) is { } text ? JsonNode.Parse(text) as JsonObject ?? [] : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

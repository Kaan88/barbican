// Mirrors: nothing in Bulwark (decision D-M6b-08). Full-trust native add-ons still get their outbound HTTP, secrets, settings and mail operations from the host: one HttpClient without redirects or cookies (the assistant's providers
// are separate origins that must not share JMAP credentials, as before), secrets scoped by add-on id, settings in the desktop settings file, the host tools over the real services.
// An add-on that existed before add-ons (the assistant's data folder <data>/assistant) adopts that folder once into its own data folder: the files are copied, never moved.

using System.Collections.Concurrent;
using Barbican.Core.Settings.Prefs;
using Barbican.Core.Storage;
using Barbican.Plugins;

namespace Barbican.Core.Plugins.Native.Host;

/// <summary>The default <see cref="IPluginHostServices"/>.</summary>
public sealed class PluginHostServices : IPluginHostServices, IDisposable
{
    private readonly IProfilePaths _paths;
    private readonly IPluginSecretStore _secrets;
    private readonly Func<IPluginHostTools> _tools;
    private readonly IPluginDialogs? _dialogs;
    private readonly SettingWriteGate? _gate;
    private readonly ConcurrentDictionary<string, PluginSettingsStore> _settings = new(StringComparer.Ordinal);
    private readonly HttpClient _http;

    /// <summary>Creates the services; <paramref name="tools"/> is asked when an add-on starts (the tools need most of the app's services); <paramref name="httpHandler"/> is a test seam (null: a handler without redirects and cookies).</summary>
    public PluginHostServices(IProfilePaths paths, IPluginSecretStore secrets, Func<IPluginHostTools> tools, IPluginDialogs? dialogs = null, SettingWriteGate? gate = null, HttpMessageHandler? httpHandler = null)
    {
        _paths = paths;
        _secrets = secrets;
        _tools = tools;
        _dialogs = dialogs;
        _gate = gate;
#pragma warning disable RS0030 // Add-on providers use a separate origin and must not share JMAP credentials or redirects.
        _http = httpHandler is null
            ? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }, disposeHandler: true) { Timeout = System.Threading.Timeout.InfiniteTimeSpan }
            : new HttpClient(httpHandler, disposeHandler: false) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
#pragma warning restore RS0030
    }

    /// <inheritdoc/>
    public PluginContext Apply(PluginContext context, string pluginId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(pluginId);
        AdoptLegacyData(context.DataDirectory, pluginId);
        return context with
        {
            Settings = SettingsOf(pluginId),
            Secrets = SecretsOf(pluginId),
            Http = _http,
            Tools = _tools(),
            Dialogs = _dialogs,
        };
    }

    /// <inheritdoc/>
    public IPluginSettings SettingsOf(string pluginId) =>
        _settings.GetOrAdd(pluginId, id => new PluginSettingsStore(_paths, id, _gate));

    /// <inheritdoc/>
    public IPluginSecrets SecretsOf(string pluginId) => new ScopedPluginSecrets(pluginId, _secrets);

    /// <summary>Disposes the HTTP client and the settings stores.</summary>
    public void Dispose()
    {
        _http.Dispose();
        foreach (var store in _settings.Values) store.Dispose();
    }

    private void AdoptLegacyData(string dataDirectory, string pluginId)
    {
        if (dataDirectory.Length == 0 || pluginId.IndexOfAny(['/', '\\', ':']) >= 0)
        {
            return;
        }

        var legacy = Path.Combine(_paths.DataDir, pluginId);
        try
        {
            if (!Directory.Exists(legacy) || Directory.Exists(dataDirectory))
            {
                return;
            }

            _ = Directory.CreateDirectory(dataDirectory);
            foreach (var file in Directory.GetFiles(legacy, "*.json"))
            {
                File.Copy(file, Path.Combine(dataDirectory, Path.GetFileName(file)), overwrite: false);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // adoption is best effort: an add-on without its old files starts with an empty history
        }
    }
}

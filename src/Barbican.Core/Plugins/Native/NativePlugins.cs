// Mirrors: Rust main.rs:450-460 (plugins loaded once at start-up, disabledPlugins from prefs), msgload.rs:259 (the plugin's rows replace the built-in spam
// verdict), plugins.rs:107 email_details; spec B-SPAM-004 (a clean verdict shows only with "show for clean mail too"); M2 plan 6.5 and 8 (the bundled spam
// score loaded off the UI thread, its start-up cost measured). Owner unified (M2 wave 2).

using System.Diagnostics;
using System.Text.Json.Nodes;
using Barbican.Core.Mail.Model;
using Barbican.Plugins;

namespace Barbican.Core.Plugins.Native;

/// <summary>
/// The app's plugin host: loads the plugins once, on the thread pool, the first time anything asks (or at start-up), and feeds the rows below the
/// subject. Until the load finished the plugin list is empty and a message gets its rows when the load is done.
/// </summary>
public sealed class NativePlugins : INativePluginHost, IEmailDetailsSource, IDisposable
{
    private readonly Func<NativePluginHostOptions> _options;
    private readonly Func<bool> _showClean;
    private readonly Lock _gate = new();
    private Task<NativePluginHost>? _host;
    private NativePluginHost? _loaded;

    /// <summary>Creates the service; <paramref name="options"/> is read when the load starts.</summary>
    /// <param name="options">Where the plugins are and what they are wired to.</param>
    /// <param name="showClean">Whether a clean spam verdict shows (B-SPAM-004); false hides the spam-score plugin's "success" row.</param>
    public NativePlugins(Func<NativePluginHostOptions> options, Func<bool>? showClean = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _showClean = showClean ?? (() => false);
    }

    /// <summary>A plugin failed to load, threw, or ran past its budget.</summary>
    public event EventHandler<PluginFailedEventArgs>? PluginFailed;

    /// <summary>How long loading took (the start-up cost of the plugin host, M2 plan 8), once loaded.</summary>
    public TimeSpan? LoadTime { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<PluginInfo> Plugins => _loaded?.Plugins ?? [];

    /// <inheritdoc />
    public IReadOnlyList<string> LoadErrors => _loaded?.LoadErrors ?? [];

    /// <summary>Starts loading on the thread pool (once) and returns the host when it is loaded.</summary>
    public Task<NativePluginHost> LoadAsync()
    {
        lock (_gate)
        {
            return _host ??= Task.Run(() =>
            {
                var watch = Stopwatch.StartNew();
                var host = NativePluginHost.Load(_options());
                host.PluginFailed += (s, e) => PluginFailed?.Invoke(this, e);
                LoadTime = watch.Elapsed;
                _loaded = host;
                return host;
            });
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmailDetailRow>> EmailDetailsAsync(JsonNode email, CancellationToken ct)
    {
        var host = await LoadAsync().ConfigureAwait(false);
        return Filter(await host.EmailDetailsAsync(email, ct).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmailDetailRow>> RowsForAsync(EmailBody email, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(email);
        var host = await LoadAsync().ConfigureAwait(false);
        var view = new EmailDetailsView(email.Headers.Select(h => new HeaderView(h.Name, h.Value)).ToList());
        return Filter(await host.EmailDetailsAsync(view, ct).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _loaded?.Dispose();
    }

    private IReadOnlyList<EmailDetailRow> Filter(IReadOnlyList<EmailDetailRow> rows) =>
        _showClean() ? rows : rows.Where(r => !(string.Equals(r.Label, "Spam analysis", StringComparison.Ordinal) && string.Equals(r.Tone, "success", StringComparison.Ordinal))).ToList();
}

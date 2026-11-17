// Mirrors: Rust plugins.rs:64 load_all (every library in the plugins folder; a duplicate id, a missing or empty manifest, or a newer API is skipped and
// reported), :73 load, :107 email_details (rows of every enabled plugin that handles the hook), :48 call (a panicking plugin must not take the app down);
// the C ABI is dropped (D-234): a native .NET plugin (Barbican.Plugins.Abstractions.IBarbicanPlugin) per folder, each in its own collectible
// AssemblyLoadContext (PluginLoadContext). Bulwark lib/plugin-hooks.ts:62-66 and :186-188 (budgets, fail closed; HookPolicies), lib/plugin-loader.ts
// (manifest checks), lib/plugin-runtime.ts (a plugin's handlers are contained). R2-10: every plugin call starts on the thread pool, so a handler that blocks
// before its first await is bounded by the budget too. Owner unified (M2 wave 2). Nothing here assumes the JS host (M6b).

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Hooks;
using Barbican.Plugins;

namespace Barbican.Core.Plugins.Native;

/// <summary>Loads native .NET plugins from plugin folders (one subfolder per plugin) and calls them within their budgets.</summary>
public sealed class NativePluginHost : INativePluginHost, IDisposable
{
    /// <summary>The plugin API version this app implements (Rust plugins.rs:25 API_VERSION).</summary>
    public const int ApiVersion = 1;

    /// <summary>The native hook of the rows below the subject (Rust email.details; Bulwark's ui:email-details slot).</summary>
    public const string EmailDetailsHook = "email.details";

    private static JsonSerializerOptions? _payloadJson;

    private readonly NativePluginHostOptions _options;
    private readonly List<LoadedPlugin> _loaded = [];
    private readonly List<string> _errors = [];
    private bool _disposed;

    private NativePluginHost(NativePluginHostOptions options)
    {
        _options = options;
    }

    /// <summary>A plugin failed to load, threw, or ran past its budget; the app carried on.</summary>
    public event EventHandler<PluginFailedEventArgs>? PluginFailed;

    /// <inheritdoc />
    public IReadOnlyList<PluginInfo> Plugins => _loaded.Select(p => p.Info).ToList();

    /// <inheritdoc />
    public IReadOnlyList<string> LoadErrors => _errors;

    /// <summary>Loads every plugin under <paramref name="pluginsRoot"/>; a plugin that fails to load is skipped and reported in <see cref="LoadErrors"/>.</summary>
    public static Result<NativePluginHost> LoadDirectory(string pluginsRoot)
    {
        ArgumentNullException.ThrowIfNull(pluginsRoot);
        return Directory.Exists(pluginsRoot)
            ? Result<NativePluginHost>.Ok(Load(new NativePluginHostOptions { BundledRoot = pluginsRoot }))
            : Result<NativePluginHost>.Fail(new ValidationError($"No plugins folder at {pluginsRoot}."));
    }

    /// <summary>
    /// Loads the bundled plugins, then the user's (an id already loaded is skipped). Disabled plugins are listed and never started. Each enabled plugin is
    /// initialized on the thread pool at once; its calls wait for that. Never throws for a bad plugin.
    /// </summary>
    public static NativePluginHost Load(NativePluginHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var host = new NativePluginHost(options);
        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            host._errors.Add("Native plugins need the JIT runtime; this build cannot load them.");
            return host;
        }

        foreach (var (root, bundled) in new[] { (options.BundledRoot, true), (options.UserRoot, false) })
        {
            if (root is null || !Directory.Exists(root))
            {
                continue;
            }

            foreach (var folder in Directory.GetDirectories(root).Order(StringComparer.Ordinal))
            {
                host.LoadFolder(folder, bundled);
            }
        }

        return host;
    }

    /// <summary>A host over plugin instances that live in the app's own load context (tests: a throwing or sleeping plugin).</summary>
    internal static NativePluginHost FromInstances(NativePluginHostOptions options, IEnumerable<(IBarbicanPlugin Plugin, string Folder)> plugins)
    {
        var host = new NativePluginHost(options);
        foreach (var (plugin, folder) in plugins)
        {
            host.Register(folder, bundled: false, plugin, context: null);
        }

        return host;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmailDetailRow>> EmailDetailsAsync(JsonNode email, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(email);
        var view = new EmailDetailsView(Headers(email));
        return await EmailDetailsAsync(view, ct).ConfigureAwait(false);
    }

    /// <summary>The rows every enabled plugin that handles email.details adds; they run side by side, a late or failing one adds none.</summary>
    public async Task<IReadOnlyList<EmailDetailRow>> EmailDetailsAsync(EmailDetailsView view, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(view);
        var calls = _loaded
            .Where(p => p.Info.Enabled && !p.Ready.IsFaulted && p.Info.Handles(EmailDetailsHook))
            .Select(p => RunAsync(p, EmailDetailsHook, _options.DetailsBudget, t => p.Plugin.OnEmailDetailsAsync(view, t), ct))
            .ToList();
        var results = await Task.WhenAll(calls).ConfigureAwait(false);
        var rows = new List<EmailDetailRow>();
        foreach (var (done, value) in results)
        {
            if (done && value is not null)
            {
                rows.AddRange(value.Where(r => r is not null).Select(r => r with { Tone = Tone(r.Tone) }));
            }
        }

        return rows;
    }

    /// <summary>Unsubscribes every plugin from the hook bus and unloads their load contexts.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var p in _loaded)
        {
            p.Bridge.Dispose();
            if (p.Plugin is IDisposable d)
            {
                try
                {
                    d.Dispose();
                }
                catch (Exception e)
                {
                    Report(p.Info.Id, "dispose", e);
                }
            }

            p.Context?.Unload();
        }

        _loaded.Clear();
    }

    /// <summary>A hook payload as a plugin sees it: JSON with camel-case names (the plugin cannot see the app's types).</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The plugin host runs only in the JIT app (RuntimeFeature.IsDynamicCodeSupported); payloads are app records kept by their call sites.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "As above: no plugin is ever loaded without dynamic code.")]
    internal static string PayloadAsJson(object? payload)
    {
        if (payload is null)
        {
            return "{}";
        }

        _payloadJson ??= new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver(), PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        return JsonSerializer.Serialize(payload, payload.GetType(), _payloadJson);
    }

    internal void Report(string pluginId, string where, Exception error) => PluginFailed?.Invoke(this, new PluginFailedEventArgs(pluginId, where, error));

    /// <summary>
    /// Runs one plugin call on the thread pool, bounded by <paramref name="budget"/>. A call that throws or runs late is reported and counts as not done;
    /// a late call is told to cancel and left behind (its result and error are observed, never thrown).
    /// </summary>
    internal async Task<(bool Done, T? Value)> RunAsync<T>(
        LoadedPlugin plugin, string where, TimeSpan budget, Func<CancellationToken, Task<T>> call, CancellationToken ct, bool afterStart = true)
    {
        using var callCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = callCts.Token;
        var work = Task.Run(
            async () =>
            {
                if (afterStart)
                {
#pragma warning disable VSTHRD003 // the plugin's start was begun by this host on the pool; no UI context is involved
                    await plugin.Ready.ConfigureAwait(false);
#pragma warning restore VSTHRD003
                }

                return await call(token).ConfigureAwait(false);
            },
            CancellationToken.None);
        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var delay = Task.Delay(budget, _options.Time, delayCts.Token);
        var first = await Task.WhenAny(work, delay).ConfigureAwait(false);
        if (ReferenceEquals(first, work))
        {
            await delayCts.CancelAsync().ConfigureAwait(false);
            try
            {
                return (true, await work.ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                Report(plugin.Info.Id, where, e);
                return (false, default);
            }
        }

        ct.ThrowIfCancellationRequested();
        await callCts.CancelAsync().ConfigureAwait(false);
        _ = work.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        Report(plugin.Info.Id, where, new TimeoutException($"Plugin '{plugin.Info.Id}' did not answer {where} within {budget.TotalSeconds:0.#} s."));
        return (false, default);
    }

    private static string Tone(string? tone) => tone is "danger" or "warning" or "success" ? tone : "neutral";

    private static List<HeaderView> Headers(JsonNode email)
    {
        var list = new List<HeaderView>();
        if (email["headers"] is JsonArray headers)
        {
            foreach (var h in headers)
            {
                if (h?["name"] is JsonValue n && n.TryGetValue<string>(out var name) && h["value"] is JsonValue v && v.TryGetValue<string>(out var value))
                {
                    list.Add(new HeaderView(name, value));
                }
            }
        }

        return list;
    }

    private void LoadFolder(string folder, bool bundled)
    {
        var label = Path.GetFileName(folder);
        PluginLoadContext? context = null;
        try
        {
            var dlls = Directory.GetFiles(folder, "*.dll")
                .Where(f => !string.Equals(Path.GetFileNameWithoutExtension(f), "Barbican.Plugins.Abstractions", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal)
                .ToList();
            if (dlls.Count == 0)
            {
                _errors.Add($"{label}: no plugin library");
                return;
            }

            context = new PluginLoadContext(dlls[0]);
            var plugin = CreatePlugin(context, dlls);
            if (plugin is null)
            {
                _errors.Add($"{label}: no IBarbicanPlugin in the folder");
                context.Unload();
                return;
            }

            Register(folder, bundled, plugin, context);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or BadImageFormatException or FileLoadException or ReflectionTypeLoadException
                                       or TypeLoadException or MissingMethodException or TargetInvocationException or InvalidCastException)
        {
            _errors.Add($"{label}: {e.Message}");
            Report(label, "load", e);
            context?.Unload();
        }
    }

    private void Register(string folder, bool bundled, IBarbicanPlugin plugin, PluginLoadContext? context)
    {
        var label = Path.GetFileName(folder);
        var m = plugin.Manifest;
        var why = Refusal(m) ?? (_loaded.Any(p => string.Equals(p.Info.Id, m.Id, StringComparison.Ordinal)) ? $"duplicate id {m.Id}" : null);
        if (why is not null)
        {
            _errors.Add($"{label}: {why}");
            context?.Unload();
            return;
        }

        var enabled = !_options.Disabled.Contains(m.Id);
        var info = new PluginInfo(m.Id, string.IsNullOrEmpty(m.Name) ? "Plugin" : m.Name, m.Version ?? string.Empty, m.Hooks ?? [])
        {
            Description = m.Description ?? string.Empty,
            Directory = folder,
            Enabled = enabled,
            Bundled = bundled,
        };
        var loaded = new LoadedPlugin(info, plugin, context);
        loaded.Bridge = new PluginHookBridge(this, loaded, _options.Bus);
        _loaded.Add(loaded);
        if (enabled)
        {
            loaded.Ready = InitializeAsync(loaded);
        }
    }

    private static string? Refusal(PluginManifest m)
    {
        if (m.Api > ApiVersion)
        {
            return $"needs plugin API {m.Api} (this app has {ApiVersion})";
        }

        if (string.IsNullOrWhiteSpace(m.Id) || m.Id.IndexOfAny(['/', '\\', ':']) >= 0 || m.Id.Contains("..", StringComparison.Ordinal))
        {
            return "manifest has no usable id";
        }

        var unknown = (m.Hooks ?? []).Where(h => !string.Equals(h, EmailDetailsHook, StringComparison.Ordinal) && !HookPolicies.IsCatalogued(h)).ToList();
        return unknown.Count > 0 ? $"unknown hook {string.Join(", ", unknown)} (not in the catalogue)" : null;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Loading a plugin is reflection by nature; the host runs only with dynamic code (checked in Load).")]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "As above: the plugin type is found at run time and needs its public parameterless constructor.")]
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "As above.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "As above.")]
    private static IBarbicanPlugin? CreatePlugin(PluginLoadContext context, List<string> dlls)
    {
        foreach (var dll in dlls)
        {
            var assembly = context.LoadFromAssemblyPath(dll);
            foreach (var type in assembly.GetExportedTypes())
            {
                if (type is { IsClass: true, IsAbstract: false } && typeof(IBarbicanPlugin).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) is not null)
                {
                    return (IBarbicanPlugin)Activator.CreateInstance(type)!;
                }
            }
        }

        return null;
    }

    private async Task InitializeAsync(LoadedPlugin plugin)
    {
        var data = Path.Combine(_options.DataRoot ?? Path.Combine(plugin.Info.Directory, ".data"), plugin.Info.Id);
        var context = new PluginContext(plugin.Info.Directory, data) { Hooks = plugin.Bridge };

        // initialization gets the intercept budget: a plugin may read its own files first
        var (done, _) = await RunAsync(
            plugin,
            "initialize",
            TimeSpan.FromSeconds(60),
            async t =>
            {
                await plugin.Plugin.InitializeAsync(context, t).ConfigureAwait(false);
                return true;
            },
            CancellationToken.None,
            afterStart: false).ConfigureAwait(false);
        if (!done)
        {
            // a plugin that could not start is listed but never called
            plugin.Bridge.Dispose();
            throw new InvalidOperationException($"Plugin '{plugin.Info.Id}' did not start.");
        }
    }
}

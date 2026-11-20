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
using Barbican.Core.Plugins.Packages.Inspection;
using Barbican.Core.Plugins.Runtime;
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
    private readonly Lock _lifecycle = new();
    private readonly List<LoadedPlugin> _loaded = [];
    private readonly List<string> _errors = [];
    private bool _disposed;

    private NativePluginHost(NativePluginHostOptions options)
    {
        _options = options;
    }

    internal static NativePluginHost CreateArchiveHost(NativePluginHostOptions options) =>
        new(options with
        {
            DataRoot = Path.Combine(options.DataRoot ?? throw new InvalidOperationException("Native add-on storage is unavailable."), "native-packages"),
        });

    /// <summary>A plugin failed to load, threw, or ran past its budget; the app carried on.</summary>
    public event EventHandler<PluginFailedEventArgs>? PluginFailed;

    /// <inheritdoc />
    public IReadOnlyList<PluginInfo> Plugins => Snapshot().Where(p => p.AcceptsCalls).Select(p => p.Info).ToList();

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
        var calls = Snapshot()
            .Where(p => p.AcceptsCalls && p.Info.Enabled && !p.Ready.IsFaulted && p.Info.Handles(EmailDetailsHook))
            .Select(p => p.IsArchive
                ? RunArchiveAsync<IReadOnlyList<EmailDetailRow>>(p, EmailDetailsHook, _options.DetailsBudget,
                    async t => (await p.Plugin.OnEmailDetailsAsync(view, t).ConfigureAwait(false)).ToArray(), ct)
                : RunAsync(p, EmailDetailsHook, _options.DetailsBudget, t => p.Plugin.OnEmailDetailsAsync(view, t), ct))
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
        LoadedPlugin[] plugins;
        lock (_lifecycle)
        {
            if (_disposed) return;
            _disposed = true;
            plugins = [.. _loaded];
            _loaded.Clear();
        }

        foreach (var p in plugins)
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
    }

    private LoadedPlugin[] Snapshot()
    {
        lock (_lifecycle) return [.. _loaded];
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Only approved packages run in the Windows JIT application.")]
    internal async Task InitializeApprovedArchiveAsync(InspectedPluginPackage package, PluginRuntimeBinding binding, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!binding.IsAlive || !RuntimeFeature.IsDynamicCodeSupported) throw new InvalidOperationException("Native startup is unavailable.");
        var loadContext = new NativeArchiveLoadContext(package);
        IBarbicanPlugin? plugin = null;
        try
        {
            plugin = CreatePlugin(loadContext.LoadMain(package.EntryPoint), requireSingle: true)
                ?? throw new InvalidOperationException("The reviewed assembly needs one public native plug-in type.");
            using var document = JsonDocument.Parse(package.ManifestJson);
            var approvedHooks = document.RootElement.GetProperty("hooks").EnumerateArray().Select(hook => hook.GetString()!).ToArray();
            var manifest = plugin.Manifest;
            if (manifest is null || manifest.Api != ApiVersion ||
                !string.Equals(manifest.Id, package.Id, StringComparison.Ordinal) ||
                !string.Equals(manifest.Version, package.Version, StringComparison.Ordinal) ||
                !(manifest.Hooks ?? []).ToHashSet(StringComparer.Ordinal).SetEquals(approvedHooks))
                throw new InvalidOperationException("The native manifest differs from the reviewed package.");

            var info = new PluginInfo(package.Id, document.RootElement.GetProperty("name").GetString()!, package.Version, approvedHooks)
            {
                Description = document.RootElement.GetProperty("description").GetString()!,
            };
            var loaded = new LoadedPlugin(info, plugin, loadContext, binding);
            loaded.Bridge = new PluginHookBridge(this, loaded, _options.Bus);
            lock (_lifecycle) _loaded.Add(loaded);
        }
        catch
        {
            try { (plugin as IDisposable)?.Dispose(); }
            finally { loadContext.Unload(); }
            throw;
        }

        var registered = Snapshot().Single();
        ct.ThrowIfCancellationRequested();
        if (!binding.IsAlive) throw new InvalidOperationException("Native add-on approval expired during startup.");
        var context = new PluginContext(string.Empty, Path.Combine(_options.DataRoot!, package.Id)) { Hooks = registered.Bridge };
        await registered.Plugin.InitializeAsync(context, ct).ConfigureAwait(false);
    }

    internal void PublishApprovedArchive() => Snapshot().Single().Publish();
    internal Task DrainArchiveCallsAsync() => Task.WhenAll(Snapshot().Select(plugin => plugin.DrainCallsAsync()));

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

    internal async Task<(bool Done, T? Value)> RunArchiveAsync<T>(
        LoadedPlugin plugin, string where, TimeSpan budget, Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var deadlineSignal = CancellationTokenSource.CreateLinkedTokenSource(ct, plugin.Lifetime);
        if (deadlineSignal.IsCancellationRequested || !plugin.TryEnterCall()) return (false, default);

        var callStop = new CancellationTokenSource();
        var work = Task.Run(async () =>
        {
            if (!plugin.AcceptsCalls || callStop.IsCancellationRequested) return default;
            return await call(callStop.Token).ConfigureAwait(false);
        });
        var deadline = Task.Delay(budget, _options.Time, deadlineSignal.Token);
        if (!ReferenceEquals(await Task.WhenAny(work, deadline).ConfigureAwait(false), work))
        {
            // Foreign cancellation callbacks are work too. Returning the deadline to the
            // reader or bus does not release their place in the runtime's teardown drain.
            _ = StopArchiveCallAsync(plugin, callStop, work, where);
            ct.ThrowIfCancellationRequested();
            if (plugin.AcceptsCalls) Report(plugin.Info.Id, where, new TimeoutException("The native add-on missed the hook deadline."));
            return (false, default);
        }

        try
        {
            var value = await work.ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return plugin.AcceptsCalls ? (true, value) : (false, default);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            if (plugin.AcceptsCalls) Report(plugin.Info.Id, where, error);
            return (false, default);
        }
        finally
        {
            // The deadline token has no foreign registrations.
            await deadlineSignal.CancelAsync().ConfigureAwait(false);
            callStop.Dispose();
            plugin.ExitCall();
        }
    }

    private async Task StopArchiveCallAsync(LoadedPlugin plugin, CancellationTokenSource callStop, Task work, string where)
    {
        try
        {
#pragma warning disable VSTHRD003 // The foreign call and cancellation callbacks run on the pool.
            await Task.WhenAll(callStop.CancelAsync(), work).ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            if (error is not OperationCanceledException && plugin.AcceptsCalls) Report(plugin.Info.Id, where, error);
        }
        finally
        {
            callStop.Dispose();
            plugin.ExitCall();
        }
    }

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
#pragma warning disable CA1031 // a plugin is foreign code: whatever its constructor, static constructor or manifest throws (not only load errors) skips that plugin and never the host (M2 unified review)
        catch (Exception e)
#pragma warning restore CA1031
        {
            _errors.Add($"{label}: {e.Message}");
            Report(label, "load", e);
            context?.Unload();
        }
    }

    private void Register(string folder, bool bundled, IBarbicanPlugin plugin, PluginLoadContext? context)
    {
        var label = Path.GetFileName(folder);
        PluginManifest? m;
        try
        {
            // read once: the manifest is the plugin's own code (a getter may throw or answer differently each time)
            m = plugin.Manifest;
        }
#pragma warning disable CA1031 // foreign code: a throwing manifest refuses that plugin, never the host
        catch (Exception e)
#pragma warning restore CA1031
        {
            _errors.Add($"{label}: the manifest could not be read ({e.Message})");
            Report(label, "manifest", e);
            context?.Unload();
            return;
        }

        var why = m is null ? "no manifest" : Refusal(m) ?? (_loaded.Any(p => string.Equals(p.Info.Id, m.Id, StringComparison.Ordinal)) ? $"duplicate id {m.Id}" : null);
        if (why is not null || m is null)
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

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The bundled native path requires a JIT build.")]
    private static IBarbicanPlugin? CreatePlugin(PluginLoadContext context, List<string> dlls)
    {
        foreach (var dll in dlls)
        {
            if (CreatePlugin(context.LoadFromAssemblyPath(dll), requireSingle: false) is { } plugin) return plugin;
        }

        return null;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Loading a plugin is reflection by nature; both native paths require the JIT.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "The plugin type is found at run time and needs its public parameterless constructor.")]
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "The plug-in's type is found at run time.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "The plug-in's type is found at run time.")]
    private static IBarbicanPlugin? CreatePlugin(Assembly assembly, bool requireSingle)
    {
        var types = assembly.GetExportedTypes().Where(type => type is { IsClass: true, IsAbstract: false }
            && typeof(IBarbicanPlugin).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) is not null);
        var selected = requireSingle ? types.SingleOrDefault() : types.FirstOrDefault();
        return selected is null ? null : (IBarbicanPlugin)Activator.CreateInstance(selected)!;
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

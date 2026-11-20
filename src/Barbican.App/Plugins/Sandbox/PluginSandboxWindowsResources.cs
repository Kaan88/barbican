#if WINDOWS
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Runtime;
using Microsoft.Web.WebView2.Core;
using Windows.Storage.Streams;

namespace Barbican.App.Plugins.Sandbox;

/// <summary>Serves only the preloaded add-on bootstrap documents and immutable assets.</summary>
internal sealed class PluginSandboxWindowsResources(PluginRuntimeBinding binding) : IDisposable
{
    private const string CommonAssetHeaders = "Cache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n";
    private readonly Dictionary<string, Response> _assets = new(StringComparer.Ordinal);
    private readonly string _frameQuery = binding.Kind == PluginFrameKind.Slot ? "?frame=slot" : "";
    private Response? _host;
    private Response? _child;
    private PluginSandboxResourcePolicy? _policy;
    private sealed record Response(IRandomAccessStream Content, string Headers);

    public static async Task<PluginSandboxWindowsResources> PrepareAsync(PluginRuntimeBinding binding,
        IReadOnlyDictionary<string, PluginSandboxBootstrapAsset> runtimeAssets,
        ImmutableDictionary<string, ImmutableArray<byte>> hostScripts, CancellationToken ct)
    {
        var resources = new PluginSandboxWindowsResources(binding);
        try
        {
            resources._host = await EncodeAsync(PluginSandboxDocuments.Host(binding.Kind, binding.Tier), ct).ConfigureAwait(false);
            resources._child = await EncodeAsync(PluginSandboxDocuments.Child(binding.Tier), ct).ConfigureAwait(false);
            foreach (var (name, asset) in runtimeAssets)
            {
                if (string.Equals(asset.Purpose, "manifest", StringComparison.Ordinal)) continue;
                var type = string.Equals(asset.Purpose, "stylesheet", StringComparison.Ordinal) ? "text/css" : "text/javascript";
                await resources.AddAsync(name, type, asset.Content, ct).ConfigureAwait(false);
            }
            foreach (var (name, content) in hostScripts)
                await resources.AddAsync(name, "text/javascript", content, ct).ConfigureAwait(false);
            resources._policy = new(binding.Tier, resources._assets.Keys.Select(path => (path, path.EndsWith(".css", StringComparison.Ordinal) ? "style" : "script")));
            return resources;
        }
        catch
        {
            resources.Dispose();
            throw;
        }
    }

    public PluginSandboxResourcePolicy Attach(CoreWebView2 core)
    {
        // This deny-by-default listener stays installed until the dedicated control is closed.
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += OnRequested;
        return _policy!;
    }

    private async Task AddAsync(string name, string type, ImmutableArray<byte> bytes, CancellationToken ct)
    {
        var originHeaders = binding.Tier == PluginExecutionTier.Untrusted
            ? "Access-Control-Allow-Origin: *\r\n"
            : "Cross-Origin-Resource-Policy: same-origin\r\n";
        var headers = "Content-Type: " + type + "; charset=utf-8\r\n" + CommonAssetHeaders + originHeaders;
        _assets.Add("/assets/" + name, await CreateResponseAsync(ImmutableCollectionsMarshal.AsArray(bytes)!, headers, ct).ConfigureAwait(false));
    }

    private void OnRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        var environment = sender.Environment;
        var selected = environment.CreateWebResourceResponse(null, 403, "Blocked", "Cache-Control: no-store");
        try
        {
            if (!binding.IsAlive ||
                !string.Equals(args.Request.Method, "GET", StringComparison.Ordinal) ||
                !Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri) ||
                FindResponse(uri, args.ResourceContext) is not { } response)
                return;
            var clone = response.Content.CloneStream();
            try { selected = environment.CreateWebResourceResponse(clone, 200, "OK", response.Headers); }
            catch { clone.Dispose(); throw; }
        }
#pragma warning disable CA1031 // Resource preparation must not let a failed request fall through to the network.
        catch (Exception) { }
#pragma warning restore CA1031
        finally { args.Response = selected; }
    }

    private Response? FindResponse(Uri uri, CoreWebView2WebResourceContext context)
    {
        if (context == CoreWebView2WebResourceContext.Document)
        {
            if (_policy!.Allows(uri, "top-navigation")) return _host;
            if (string.Equals(uri.Query, _frameQuery, StringComparison.Ordinal) && _policy.Allows(uri, "frame-navigation")) return _child;
            return null;
        }

        var purpose = context switch
        {
            CoreWebView2WebResourceContext.Script => "script",
            CoreWebView2WebResourceContext.Stylesheet => "style",
            _ => "",
        };
        if (purpose.Length == 0 || !_policy!.Allows(uri, purpose)) return null;
        return _assets[uri.AbsolutePath];
    }

    private static Task<Response> EncodeAsync((string Html, string Headers) document, CancellationToken ct) =>
        CreateResponseAsync(Encoding.UTF8.GetBytes(document.Html), document.Headers, ct);

    private static async Task<Response> CreateResponseAsync(byte[] bytes, string headers, CancellationToken ct)
    {
        var stream = new InMemoryRandomAccessStream();
        try
        {
            using var writer = new DataWriter(stream);
            writer.WriteBytes(bytes);
            await writer.StoreAsync().AsTask(ct).ConfigureAwait(false);
            writer.DetachStream();
            stream.Seek(0);
            return new Response(stream, headers);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _host?.Content.Dispose();
        _host = null;
        _child?.Content.Dispose();
        _child = null;
        foreach (var asset in _assets.Values) asset.Content.Dispose();
        _assets.Clear();
    }
}
#endif

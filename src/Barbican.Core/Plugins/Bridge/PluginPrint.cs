// Mirrors Bulwark lib/plugin-sandbox/host-api-print.ts and host-api-file-viewer.ts.
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Bridge;

/// <summary>Validates a print request and supplies a per-package budget to the host presenter.</summary>
public sealed class PluginPrint(TimeProvider time,
    Func<PluginPrintRequest, IPluginInvocationContext, Func<bool>, CancellationToken, Task<PluginErrorCode?>> present) : IPluginApiMethod
{
    private const int MaxPdf = 25 * 1024 * 1024;
    private const int MaxHtml = 2 * 1024 * 1024;
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Profile, string Plugin), Queue<long>> _recent = [];

    // host-api-print.ts:24 allows MAX_HTML of HTML and 25 MiB of PDF; the PDF crosses as base64 and
    // the HTML counts two bytes per UTF-16 unit (PluginValueCodec.cs:224).
    private static readonly PluginValueLimits PageLimits = new(
        MaxWireBytes: (4 * MaxPdf / 3) + (1024 * 1024), MaxStringBytes: (2 * MaxHtml) + (64 * 1024), MaxBinaryBytes: MaxPdf);

    public PluginApiDescriptor Descriptor { get; } = new("ui.print", [], Timeout.InfiniteTimeSpan, mayMutate: true)
    {
        ArgumentLimits = PageLimits,
    };
    public PluginErrorCode? ValidateArguments(PluginValue arguments) => Read(arguments) is null ? PluginErrorCode.InvalidArguments : null;

    public async Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue arguments, CancellationToken ct)
    {
        if (Read(arguments) is not { } input) return PluginCallOutcome.Failed(PluginErrorCode.InvalidArguments);
        if (input.Pdf is { } pdf && pdf.BinarySpan()[..Math.Min(pdf.ByteLength, 1024)].IndexOf("%PDF-"u8) < 0)
            return PluginCallOutcome.Failed(PluginErrorCode.InvalidArguments);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
        var attempted = 0;
        var granted = false;
        bool TakeBudget() => Interlocked.Exchange(ref attempted, 1) == 0 && (granted = Take(context.Binding.Profile, context.Binding.Package.Id));
        var error = await present(new PluginPrintRequest(input.Title, input.Html, input.Pdf), context, TakeBudget, ct).ConfigureAwait(false);
        if (error is { } refused) return PluginCallOutcome.Failed(refused);
        if (!granted) return PluginCallOutcome.Failed(attempted == 0 ? PluginErrorCode.Unavailable : PluginErrorCode.RateLimited);
        var builder = new PluginValueBuilder();
        return PluginCallOutcome.Succeeded(builder.Build(builder.Boolean(true)));
    }

    private static (string? Title, string? Html, PluginValue? Pdf)? Read(PluginValue args)
    {
        if (args is not { Kind: PluginValueKind.Array, Count: > 0 } || args.Item(0) is not { Kind: PluginValueKind.Object } page ||
            page.PropertyNames.Any(name => name is not ("title" or "html" or "pdf"))) return null;
        var names = page.PropertyNames;
        var titleValue = names.Contains("title", StringComparer.Ordinal) ? page.Property("title") : null;
        string? title = null;
        if (titleValue is not null && titleValue.Kind != PluginValueKind.Undefined)
        {
            if (titleValue is not { Kind: PluginValueKind.String, StringValue.Length: <= 200 } ||
                titleValue.StringValue.Any(c => c is <= '\u001f' or '\u007f')) return null;
            title = titleValue.StringValue;
        }
        var html = names.Contains("html", StringComparer.Ordinal) ? page.Property("html") : null;
        var pdf = names.Contains("pdf", StringComparer.Ordinal) ? page.Property("pdf") : null;
        var hasHtml = html is not null && html.Kind != PluginValueKind.Undefined;
        var hasPdf = pdf is not null && pdf.Kind != PluginValueKind.Undefined;
        if (hasHtml == hasPdf) return null;
        if (hasHtml) return html is { Kind: PluginValueKind.String, StringValue.Length: <= MaxHtml } ? (title, html.StringValue, null) : null;
        return pdf is { Kind: PluginValueKind.ArrayBuffer or PluginValueKind.View or PluginValueKind.Blob, ByteLength: > 0 and <= MaxPdf }
            ? (title, null, pdf) : null;
    }

    private bool Take(string profile, string plugin)
    {
        lock (_gate)
        {
            var now = time.GetTimestamp();
            foreach (var (key, values) in _recent.ToArray())
            {
                while (values.TryPeek(out var start) && time.GetElapsedTime(start, now) >= TimeSpan.FromMinutes(1)) _ = values.Dequeue();
                if (values.Count == 0) _recent.Remove(key);
            }
            var owner = (profile, plugin);
            if (!_recent.TryGetValue(owner, out var queue))
            {
                if (_recent.Count >= 256) return false;
                _recent.Add(owner, queue = new Queue<long>());
            }
            if (queue.Count >= 3) return false;
            queue.Enqueue(now);
            return true;
        }
    }
}

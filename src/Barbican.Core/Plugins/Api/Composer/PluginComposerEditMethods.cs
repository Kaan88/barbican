// Mirrors: lib/plugin-sandbox/host-api-composer.ts:1-97,132-151 (getBody, getSelection, replaceSelection, replaceBody, attach; 512 KiB content, 60 edits a minute, expectedRevision),
// host-api.ts:358-367 (compose:read, compose:write, email:blob-write), host-api-input.ts (readObject/readString/readId). Edits are single editor transactions in the host.
// The argument budget of an edit is MAX_CONTENT (host-api-composer.ts:26) in characters, so it is this method's own cap and not the shared default.
using System.Globalization;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Composer;

/// <summary>The remaining source composer operations: read the own text or the selection, replace either, attach an uploaded blob.</summary>
public static class PluginComposerEditMethods
{
    private const int MaxContent = 512 * 1024;

    // Two bytes per UTF-16 unit (PluginValueCodec.cs:224) plus the composer id and options; the wire
    // budget doubles that for JSON escaping of the text.
    private static readonly PluginValueLimits ContentLimits = new(
        MaxWireBytes: (4 * MaxContent) + (1024 * 1024), MaxStringBytes: (2 * MaxContent) + (64 * 1024));

    public static IPluginApiMethod GetBody(IPluginComposerAccess access) => new Method(access, Op.GetBody, null);

    public static IPluginApiMethod GetSelection(IPluginComposerAccess access) => new Method(access, Op.GetSelection, null);

    public static IPluginApiMethod ReplaceBody(IPluginComposerAccess access, IPluginComposerEditRateLimiter limiter) => new Method(access, Op.ReplaceBody, limiter);

    public static IPluginApiMethod ReplaceSelection(IPluginComposerAccess access, IPluginComposerEditRateLimiter limiter) => new Method(access, Op.ReplaceSelection, limiter);

    public static IPluginApiMethod Attach(IPluginComposerAccess access, IPluginComposerEditRateLimiter limiter) => new Method(access, Op.Attach, limiter);

    private enum Op { GetBody, GetSelection, ReplaceBody, ReplaceSelection, Attach }

    private sealed record Parsed(PluginComposerContent? Content, bool Below, string? ExpectedRevision, PluginComposerBlob? Blob);

    private sealed class Method : IPluginApiMethod
    {
        private readonly IPluginComposerAccess _access;
        private readonly Op _op;
        private readonly IPluginComposerEditRateLimiter? _limiter;

        public Method(IPluginComposerAccess access, Op op, IPluginComposerEditRateLimiter? limiter)
        {
            _access = access ?? throw new ArgumentNullException(nameof(access));
            _op = op;
            _limiter = op is Op.GetBody or Op.GetSelection ? null : limiter ?? throw new ArgumentNullException(nameof(limiter));
            var (name, permission) = op switch
            {
                Op.GetBody => ("composer.getBody", "compose:read"),
                Op.GetSelection => ("composer.getSelection", "compose:read"),
                Op.ReplaceBody => ("composer.replaceBody", "compose:write"),
                Op.ReplaceSelection => ("composer.replaceSelection", "compose:write"),
                _ => ("composer.attach", "email:blob-write"),
            };
            Permission = permission;
            Descriptor = new(name, [[permission]], TimeSpan.FromSeconds(30), mayMutate: _limiter is not null)
            {
                ArgumentLimits = op is Op.ReplaceBody or Op.ReplaceSelection ? ContentLimits : null,
            };
        }

        public PluginApiDescriptor Descriptor { get; }

        private string Permission { get; }

        public PluginErrorCode? ValidateArguments(PluginValue args) => Parse(args) is null ? PluginErrorCode.InvalidArguments : null;

        private Parsed? Parse(PluginValue args)
        {
            if (args.Kind != PluginValueKind.Array || args.Count < 1 || !IsId(args.Item(0))) return null;
            switch (_op)
            {
                case Op.GetBody or Op.GetSelection:
                    return new(null, false, null, null);
                case Op.ReplaceBody or Op.ReplaceSelection:
                    if (args.Count < 2 || ReadContent(args.Item(1)) is not { } content) return null;
                    var options = args.Count > 2 ? args.Item(2) : null;
                    if (options is { Kind: PluginValueKind.Undefined or PluginValueKind.Null }) options = null;
                    if (options is not null && options.Kind != PluginValueKind.Object) return null;
                    var allowed = _op == Op.ReplaceBody ? "expectedRevision" : "below";
                    if (options is not null && options.PropertyNames.Any(name => !string.Equals(name, allowed, StringComparison.Ordinal))) return null;
                    var below = false;
                    string? expected = null;
                    if (options is not null && options.TryGetProperty(allowed, out var value) && value.Kind != PluginValueKind.Undefined)
                    {
                        if (_op == Op.ReplaceBody) { if (!IsId(value)) return null; expected = value.StringValue; }
                        else { if (value.Kind != PluginValueKind.Boolean) return null; below = value.BooleanValue; }
                    }
                    return new(content, below, expected, null);
                default:
                    return args.Count >= 2 && ReadBlob(args.Item(1)) is { } blob ? new(null, false, null, blob) : null;
            }
        }

        public async Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue args, CancellationToken ct)
        {
            if (Parse(args) is not { } parsed) return PluginCallOutcome.Failed(PluginErrorCode.InvalidArguments);
            var id = args.Item(0).StringValue;
            var handle = _access.Resolve(context.Binding, id);
            if (handle is null) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
            if (!context.CaptureOwner(handle.LocalAccountKey).IsOk) return PluginCallOutcome.Failed(PluginErrorCode.NoSession);
            if (!context.Demand(Permission, handle.LocalAccountKey).IsOk || !await context.CheckCurrentAsync(ct).ConfigureAwait(false))
                return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
            if (_op == Op.GetBody)
            {
                var body = await handle.ReadBodyAsync(ct).ConfigureAwait(false);
                return await IfCurrentAsync(context, handle, id, () => Pair(body.Html, body.Text, null), ct).ConfigureAwait(false);
            }
            if (_op == Op.GetSelection)
            {
                var selection = await handle.ReadSelectionAsync(ct).ConfigureAwait(false);
                return await IfCurrentAsync(context, handle, id, () => Pair(selection.Html, selection.Text, selection.Empty), ct).ConfigureAwait(false);
            }
            var snapshot = await handle.GetSnapshotAsync(ct).ConfigureAwait(false);
            if (!await CurrentAsync(context, handle, id, ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
            if (parsed.ExpectedRevision is { } expected && !string.Equals(expected, snapshot.Revision, StringComparison.Ordinal))
                return PluginCallOutcome.Failed(PluginErrorCode.ComposerChanged);
            if (!_limiter!.TryAcquire(context.Binding.Profile, context.Binding.Package.Id)) return PluginCallOutcome.Failed(PluginErrorCode.RateLimited);
            context.MarkMutationIssued();
            var done = _op switch
            {
                Op.ReplaceBody => await handle.ReplaceBodyAsync(parsed.Content!, snapshot.Revision, ct).ConfigureAwait(false),
                Op.ReplaceSelection => await handle.ReplaceSelectionAsync(parsed.Content!, parsed.Below, snapshot.Revision, ct).ConfigureAwait(false),
                _ => await handle.AttachAsync(parsed.Blob!, snapshot.Revision, ct).ConfigureAwait(false),
            };
            var result = new PluginValueBuilder();
            return PluginCallOutcome.Succeeded(result.Build(result.Boolean(done)));
        }

        private async ValueTask<PluginCallOutcome> IfCurrentAsync(IPluginInvocationContext context, IPluginComposerHandle handle, string id,
            Func<PluginCallOutcome> project, CancellationToken ct) =>
            await CurrentAsync(context, handle, id, ct).ConfigureAwait(false) ? project() : PluginCallOutcome.Failed(PluginErrorCode.StaleContext);

        private async ValueTask<bool> CurrentAsync(IPluginInvocationContext context, IPluginComposerHandle handle, string id, CancellationToken ct) =>
            await context.CheckCurrentAsync(ct).ConfigureAwait(false) && ReferenceEquals(_access.Resolve(context.Binding, id), handle);

        private static PluginCallOutcome Pair(string html, string text, bool? empty)
        {
            var b = new PluginValueBuilder();
            var root = empty is { } e
                ? b.Object(("html", b.String(html)), ("text", b.String(text)), ("empty", b.Boolean(e)))
                : b.Object(("html", b.String(html)), ("text", b.String(text)));
            return PluginCallOutcome.Succeeded(b.Build(root, new PluginValueLimits(MaxWireBytes: 100 * 1024 * 1024, MaxStringBytes: 52 * 1024 * 1024)));
        }

        private static bool IsId(PluginValue value) => value.Kind == PluginValueKind.String && value.StringValue.Length is > 0 and <= 256;

        private static PluginComposerContent? ReadContent(PluginValue value)
        {
            if (value.Kind != PluginValueKind.Object || value.PropertyNames.Any(name => name is not ("text" or "html"))) return null;
            var hasText = value.TryGetProperty("text", out var text) && text.Kind != PluginValueKind.Undefined;
            var hasHtml = value.TryGetProperty("html", out var html) && html.Kind != PluginValueKind.Undefined;
            if (hasText == hasHtml) return null;
            var source = hasHtml ? html! : text!;
            if (source.Kind != PluginValueKind.String || source.StringValue.Length > MaxContent) return null;
            if (hasHtml)
            {
                var clean = PluginBodyHtml.Sanitize(source.StringValue);
                return new(clean, Barbican.Core.Editor.DocHtml.HtmlText(clean));
            }
            return new(PluginBodyHtml.FromText(source.StringValue), source.StringValue);
        }

        private static PluginComposerBlob? ReadBlob(PluginValue value)
        {
            if (value.Kind != PluginValueKind.Object || value.PropertyNames.Any(name => name is not ("blobId" or "name" or "type" or "size"))) return null;
            if (!value.TryGetProperty("blobId", out var blob) || !IsId(blob) ||
                !value.TryGetProperty("name", out var name) || name.Kind != PluginValueKind.String || name.StringValue.Length is 0 or > 255) return null;
            var type = "application/octet-stream";
            if (value.TryGetProperty("type", out var kind) && kind.Kind != PluginValueKind.Undefined)
            {
                if (kind.Kind != PluginValueKind.String || kind.StringValue.Length is 0 or > 255) return null;
                type = kind.StringValue;
            }
            if (!value.TryGetProperty("size", out var size) || size.Kind != PluginValueKind.Number ||
                !long.TryParse(size.NumberText, NumberStyles.None, CultureInfo.InvariantCulture, out var bytes) || bytes > 9_007_199_254_740_991L) return null;
            return new(blob.StringValue, name.StringValue, type, bytes);
        }
    }
}

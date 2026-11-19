// Mirrors: lib/plugin-sandbox/host-api-file-viewer.ts:19-143 and host-api-print.ts:49-67.
using System.Text;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
namespace Barbican.Core.Plugins.Api.Preview;
/// <summary>Adapters for bounded in-window file preview and PDF print. HTML printing remains unavailable.</summary>
public static class PluginPreviewMethods
{
    public static IPluginApiMethod OpenFile(IPluginFilePreview preview, IPluginPresentationRateLimiter limiter) => new PreviewMethod(preview, limiter, null);
    public static IPluginApiMethod Print(IPluginFilePreview preview, IPluginPrintPolicy policy, IPluginPresentationRateLimiter limiter) => new PreviewMethod(preview, limiter, policy ?? throw new ArgumentNullException(nameof(policy)));
    private sealed class PreviewMethod : IPluginApiMethod
    {
        private const int MaxBytes = 25 * 1024 * 1024;
        private readonly IPluginFilePreview _preview;
        private readonly IPluginPresentationRateLimiter _limiter;
        private readonly IPluginPrintPolicy? _policy;
        public PreviewMethod(IPluginFilePreview preview, IPluginPresentationRateLimiter limiter, IPluginPrintPolicy? policy)
        {
            _preview = preview ?? throw new ArgumentNullException(nameof(preview)); _limiter = limiter ?? throw new ArgumentNullException(nameof(limiter)); _policy = policy;
            Descriptor = new(policy is null ? "ui.openFile" : "ui.print", [], TimeSpan.FromSeconds(60), mayMutate: true);
        }
        public PluginApiDescriptor Descriptor { get; }
        public PluginErrorCode? ValidateArguments(PluginValue args)
        {
            if (args.Kind != PluginValueKind.Array || args.Count < 1 || args.Item(0).Kind != PluginValueKind.Object) return PluginErrorCode.InvalidArguments;
            var x = args.Item(0); var allowed = _policy is null ? new[] { "content", "filename", "contentType" } : new[] { "title", "html", "pdf" };
            if (x.PropertyNames.Any(name => !allowed.Contains(name, StringComparer.Ordinal))) return PluginErrorCode.InvalidArguments;
            if (_policy is null) return Has(x, "content") && Text(x, "filename", 255) && Text(x, "contentType", 255) ? null : PluginErrorCode.InvalidArguments;
            if (Has(x, "title") && (!Text(x, "title", 200) || x.Property("title").StringValue.Any(c => c <= '\u001f' || c == '\u007f'))) return PluginErrorCode.InvalidArguments;
            if (Has(x, "pdf") == Has(x, "html")) return PluginErrorCode.InvalidArguments;
            if (Has(x, "html")) return Text(x, "html", 2 * 1024 * 1024) ? null : PluginErrorCode.InvalidArguments;
            return null;
        }
        public async Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue args, CancellationToken ct)
        {
            var x = args.Item(0); var printing = _policy is not null;
            if (printing && Has(x, "html")) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
            if (printing)
            {
                if (!await _policy!.CanPrintAsync(ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.PermissionDenied);
                if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
                if (!_limiter.TryAcquire(context.Binding.Profile, context.Binding.Package.Id, PluginPresentationOperation.Print)) return PluginCallOutcome.Failed(PluginErrorCode.RateLimited);
            }
            var content = x.Property(printing ? "pdf" : "content");
            var mime = printing ? "application/pdf" : x.Property("contentType").StringValue.Split(';')[0].Trim().ToLowerInvariant();
            var extension = mime switch { "application/pdf" => "pdf", "image/png" => "png", "image/jpeg" => "jpg", "image/gif" => "gif", "image/webp" => "webp", _ => null };
            if (extension is null || content.Kind is not (PluginValueKind.ArrayBuffer or PluginValueKind.View or PluginValueKind.Blob) || content.ByteLength is <= 0 or > MaxBytes) return PluginCallOutcome.Failed(PluginErrorCode.InvalidArguments);
            var bytes = content.BinaryBytes();
            if (!Matches(bytes, mime)) return PluginCallOutcome.Failed(PluginErrorCode.InvalidArguments);
            var title = printing ? (Has(x, "title") ? x.Property("title").StringValue : context.Binding.Package.Id) : x.Property("filename").StringValue;
            var file = new PluginPreviewFile(FileName(title, extension), mime, bytes);
            if (printing && !await _policy!.CanPrintAsync(ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.PermissionDenied);
            if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
            if (!printing && !_limiter.TryAcquire(context.Binding.Profile, context.Binding.Package.Id, PluginPresentationOperation.OpenFile)) return PluginCallOutcome.Failed(PluginErrorCode.RateLimited);
            if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
            context.MarkMutationIssued();
            if (!await _preview.ShowAsync(file, printing ? PluginPreviewPurpose.PrintPdf : PluginPreviewPurpose.Preview, context, ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
            var b = new PluginValueBuilder(); return PluginCallOutcome.Succeeded(b.Build(b.Boolean(true)));
        }
        private static bool Has(PluginValue x, string name) => x.PropertyNames.Contains(name, StringComparer.Ordinal) && x.Property(name).Kind != PluginValueKind.Undefined;
        private static bool Text(PluginValue x, string name, int max) => Has(x, name) && x.Property(name).Kind == PluginValueKind.String && x.Property(name).StringValue.Length <= max;
        private static bool Matches(byte[] bytes, string mime)
        {
            var span = bytes.AsSpan();
            return mime switch
            {
                "application/pdf" => span[..Math.Min(1024, span.Length)].IndexOf("%PDF-"u8) >= 0,
                "image/png" => span.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }),
                "image/jpeg" => span.StartsWith(new byte[] { 0xff, 0xd8, 0xff }),
                "image/gif" => span.StartsWith("GIF87a"u8) || span.StartsWith("GIF89a"u8),
                "image/webp" => span.Length >= 12 && span.StartsWith("RIFF"u8) && span[8..].StartsWith("WEBP"u8),
                _ => false,
            };
        }
        private static string FileName(string raw, string extension)
        {
            var b = new StringBuilder();
            foreach (var c in raw)
            {
                if (c <= '\u001f' || c is '\u007f' or '\u200e' or '\u200f' or >= '\u202a' and <= '\u202e' or >= '\u2066' and <= '\u2069') continue;
                b.Append(c is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : c);
            }
            var text = b.ToString(); var start = 0; var end = text.Length;
            while (start < end && TrimCharacter(text[start])) start++;
            while (end > start && TrimCharacter(text[end - 1])) end--;
            var name = text[start..end];
            if (name.Length > 120) name = name[..120];
            if (name.Length == 0) name = "file";
            if (!name.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase) && !(string.Equals(extension, "jpg", StringComparison.Ordinal) && name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))) name += "." + extension;
            return name;
        }
        private static bool TrimCharacter(char c) => c is '.' or '\u0009' or '\u000a' or '\u000b' or '\u000c' or '\u000d' or '\u0020' or '\u00a0' or '\u1680' or >= '\u2000' and <= '\u200a' or '\u2028' or '\u2029' or '\u202f' or '\u205f' or '\u3000' or '\ufeff';    }
}

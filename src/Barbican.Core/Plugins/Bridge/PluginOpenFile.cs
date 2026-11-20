// Mirrors Bulwark lib/plugin-sandbox/host-api-file-viewer.ts:19-118.
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Bridge;

/// <summary>Validates and rate-limits files presented by an add-on in the app's preview.</summary>
public sealed class PluginOpenFile(
    TimeProvider time,
    Func<PluginPreviewFile, IPluginInvocationContext, CancellationToken, Task<PluginErrorCode?>> present) : IPluginApiMethod
{
    private const int MaxBytes = 25 * 1024 * 1024;
    private const int MaxRateKeys = 256;
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Profile, string Plugin), Queue<long>> _opens = [];
    private int _presenting;

    public PluginApiDescriptor Descriptor { get; } = new("ui.openFile", [], TimeSpan.FromSeconds(30), mayMutate: true);

    public PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        Read(arguments) is null ? PluginErrorCode.InvalidArguments : null;

    public async Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue arguments, CancellationToken ct)
    {
        if (Read(arguments) is not { } input) return PluginCallOutcome.Failed(PluginErrorCode.InvalidArguments);
        if (!Matches(input.Content.BinarySpan(), input.Mime)) return PluginCallOutcome.Failed(PluginErrorCode.InvalidArguments);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
        if (Interlocked.CompareExchange(ref _presenting, 1, 0) != 0) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
        try { return await PresentAsync(input, context, ct).ConfigureAwait(false); }
        finally { Volatile.Write(ref _presenting, 0); }
    }

    private async Task<PluginCallOutcome> PresentAsync((PluginValue Content, string Name, string Mime, string Extension) input,
        IPluginInvocationContext context, CancellationToken ct)
    {
        if (!TryAcquire(context.Binding.Profile, context.Binding.Package.Id)) return PluginCallOutcome.Failed(PluginErrorCode.RateLimited);
        var file = new PluginPreviewFile(FileName(input.Name, input.Extension), input.Mime, input.Content.BinaryBytes());
        if (await present(file, context, ct).ConfigureAwait(false) is { } error) return PluginCallOutcome.Failed(error);
        var values = new PluginValueBuilder();
        return PluginCallOutcome.Succeeded(values.Build(values.Boolean(true)));
    }

    private bool TryAcquire(string profile, string plugin)
    {
        lock (_gate)
        {
            var now = time.GetTimestamp();
            foreach (var (key, stamps) in _opens.ToArray())
            {
                while (stamps.TryPeek(out var start) && time.GetElapsedTime(start, now) >= TimeSpan.FromMinutes(1))
                    _ = stamps.Dequeue();
                if (stamps.Count == 0) _ = _opens.Remove(key);
            }
            var owner = (profile, plugin);
            if (!_opens.TryGetValue(owner, out var opens))
            {
                if (_opens.Count == MaxRateKeys) return false;
                _opens.Add(owner, opens = new Queue<long>());
            }
            if (opens.Count == 5) return false;
            opens.Enqueue(now);
            return true;
        }
    }

    private static (PluginValue Content, string Name, string Mime, string Extension)? Read(PluginValue arguments)
    {
        if (arguments is not { Kind: PluginValueKind.Array, Count: 1 } ||
            arguments.Item(0) is not { Kind: PluginValueKind.Object } item ||
            item.PropertyNames is not { Count: 3 } names ||
            names.Any(name => name is not ("content" or "filename" or "contentType")) ||
            item.Property("filename") is not { Kind: PluginValueKind.String, StringValue.Length: <= 255 } file ||
            item.Property("contentType") is not { Kind: PluginValueKind.String, StringValue.Length: <= 255 } mimeValue ||
            item.Property("content") is not { Kind: PluginValueKind.View or PluginValueKind.ArrayBuffer or PluginValueKind.Blob,
                ByteLength: > 0 and <= MaxBytes } content) return null;
        var mime = mimeValue.StringValue.Split(';', 2)[0].Trim().ToLowerInvariant();
        var extension = mime switch
        {
            "application/pdf" => "pdf",
            "image/png" => "png",
            "image/jpeg" => "jpg",
            "image/gif" => "gif",
            "image/webp" => "webp",
            _ => null,
        };
        return extension is null ? null : (content, file.StringValue, mime, extension);
    }

    private static bool Matches(ReadOnlySpan<byte> bytes, string mime) => mime switch
    {
        "application/pdf" => bytes[..Math.Min(bytes.Length, 1024)].IndexOf("%PDF-"u8) >= 0,
        "image/png" => bytes.StartsWith<byte>([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
        "image/jpeg" => bytes.StartsWith<byte>([0xff, 0xd8, 0xff]),
        "image/gif" => bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8),
        "image/webp" => bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes[8..].StartsWith("WEBP"u8),
        _ => false,
    };

    private static string FileName(string raw, string extension)
    {
        var name = string.Concat(raw
            .Where(c => c is > '\u001f' and not ('\u007f' or '\u200e' or '\u200f' or >= '\u202a' and <= '\u202e' or >= '\u2066' and <= '\u2069'))
            .Select(c => c is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : c));
        var start = 0;
        var end = name.Length;
        while (start < end && IsNamePadding(name[start])) start++;
        while (end > start && IsNamePadding(name[end - 1])) end--;
        name = name.Substring(start, Math.Min(120, end - start));
        if (name.Length > 0 && char.IsHighSurrogate(name[^1])) name = name[..^1];
        if (name.Length == 0) name = "file";
        return name.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase) ||
            (string.Equals(extension, "jpg", StringComparison.Ordinal) && name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            ? name : name + "." + extension;
    }

    private static bool IsNamePadding(char value) => value is '.' or '\ufeff' || char.IsWhiteSpace(value);
}

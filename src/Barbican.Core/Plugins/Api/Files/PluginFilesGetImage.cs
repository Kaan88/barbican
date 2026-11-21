// Mirrors: Bulwark lib/plugin-sandbox/assistant/d.ts:338-378 doFilesGetImage and :51,683 (files:read, at most 30 reads a minute), assistant/b.types.ts:68-93 (AttachmentImageView: name, type, width, height, data as base64, status ok, tiny,
// tracker, too_big, unsupported or failed; maxSide 64 to 2 576, default 1 568). A file over 20 MB is not read; the download stops itself at that size (IFileTransfer.ReadAsync). Deviations: the exif option is accepted and answers nothing
// (Barbican has no EXIF reader), and the picture is shrunk by SkiaSharp (see PluginFileImage).
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Files;

internal sealed class PluginFilesGetImage(IPluginFilesHost host) : PluginFilesMethod("files.getImage", "files:read", host, false, content: true)
{
    private static readonly string[] Allowed = ["maxSide", "exif"];

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => TryParse(arguments, out _, out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginFilesCall call, PluginValue arguments, CancellationToken ct)
    {
        if (!TryParse(arguments, out var key, out var maxSide)) return Fail(PluginErrorCode.InvalidArguments);
        if (!Host.Limiter.TryAcquire(Descriptor.Name, call.Profile, call.PluginId, PluginFilesData.MaxReadsPerMinute)) return Fail(PluginErrorCode.RateLimited);
        var snapshot = await PluginFilesData.SnapshotAsync(Host, call.Session, ct).ConfigureAwait(false);
        if (snapshot is null) return Fail(PluginErrorCode.Unavailable);
        if (PluginFilesData.Find(PluginFilesData.Visible(snapshot), key) is not { } node) return Fail(PluginErrorCode.Unavailable);
        if (node.IsDirectory || node.BlobId is null) return Fail(PluginErrorCode.InvalidArguments);
        var type = string.IsNullOrEmpty(node.MediaType) ? "application/octet-stream" : node.MediaType;
        var b = new PluginValueBuilder();
        if (!PluginFileImage.IsRaster(node.Name, type)) return Success(b, View(b, node.Name, type, 0, 0, "", "unsupported"));
        if (node.Size > PluginFilesData.MaxReadBytes) return Success(b, View(b, node.Name, type, 0, 0, "", "too_big"));
        var read = await Host.Transfer.ReadAsync(call.AccountKey, node.Key, PluginFilesData.MaxReadBytes, ct).ConfigureAwait(false);
        if (!read.IsOk) return Fail(PluginErrorCode.Failed);
        var shrunk = PluginFileImage.Shrink(read.Value!, maxSide);
        return Success(b, shrunk is { Status: "ok", Bytes: { } bytes }
            ? View(b, node.Name, shrunk.Type ?? type, shrunk.Width, shrunk.Height, Convert.ToBase64String(bytes), "ok")
            : View(b, node.Name, type, shrunk.Width, shrunk.Height, "", shrunk.Status));
    }

    private static int View(PluginValueBuilder b, string name, string type, int width, int height, string data, string status) => b.Object(
        ("name", b.String(name)), ("type", b.String(type)), ("width", b.Number(width)), ("height", b.Number(height)), ("data", b.String(data)), ("status", b.String(status)));

    private static bool TryParse(PluginValue arguments, out string key, out int maxSide)
    {
        (key, maxSide) = ("", PluginFileImage.DefaultSide);
        if (!PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0))) return false;
        key = arguments.Item(0).StringValue;
        if (!PluginMailArgs.TryOptions(PluginMailArgs.At(arguments, 1), Allowed, false, out var options)) return false;
        if (PluginMailArgs.Prop(options, "exif") is { } exif && exif.Kind != PluginValueKind.Boolean) return false;
        return PluginMailArgs.TryInteger(PluginMailArgs.Prop(options, "maxSide"), PluginFileImage.MinSide, PluginFileImage.MaxSide, PluginFileImage.DefaultSide, out maxSide);
    }
}

// Mirrors: Bulwark lib/plugin-sandbox/assistant/d.ts:314-336 doFilesGetText and :50,682 (files:read, at most 30 reads a minute), lib/attachment-text/index.ts:21-96 and plain.ts:10-32 (text and CSV decoded as UTF-8 with or without a byte order
// mark, UTF-16 with one, else Latin-1; a file with NUL bytes is not text; cut at maxChars, 1 to 100 000, default 20 000), assistant/b.types.ts:44-59 (AttachmentTextView). A file over 20 MB is not read (status too_big); the download
// stops itself at that size (IFileTransfer.ReadAsync). Deviation: Barbican has no PDF, Office, HTML, calendar or address-card text reader, so those formats answer status "unsupported" (with their format named) where Bulwark
// answers ok; only plain text and CSV files give text. A file read that fails (or outgrows the limit mid-download) answers Failed.
using System.Text;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Files;

internal sealed class PluginFilesGetText(IPluginFilesHost host) : PluginFilesMethod("files.getText", "files:read", host, false, content: true)
{
    internal const int DefaultChars = 20_000;
    internal const int MaxChars = 100_000;
    private static readonly string[] Allowed = ["maxChars"];

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => TryParse(arguments, out _, out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginFilesCall call, PluginValue arguments, CancellationToken ct)
    {
        if (!TryParse(arguments, out var key, out var maxChars)) return Fail(PluginErrorCode.InvalidArguments);
        if (!Host.Limiter.TryAcquire(Descriptor.Name, call.Profile, call.PluginId, PluginFilesData.MaxReadsPerMinute)) return Fail(PluginErrorCode.RateLimited);
        var snapshot = await PluginFilesData.SnapshotAsync(Host, call.Session, ct).ConfigureAwait(false);
        if (snapshot is null) return Fail(PluginErrorCode.Unavailable);
        if (PluginFilesData.Find(PluginFilesData.Visible(snapshot), key) is not { } node) return Fail(PluginErrorCode.Unavailable);
        if (node.IsDirectory || node.BlobId is null) return Fail(PluginErrorCode.InvalidArguments);
        var type = string.IsNullOrEmpty(node.MediaType) ? "application/octet-stream" : node.MediaType;
        var format = Format(node.Name, type);
        var b = new PluginValueBuilder();
        if (node.Size > PluginFilesData.MaxReadBytes) return Success(b, View(b, node.Name, type, node.Size, format, "too_big", "", false));
        if (format is not ("text" or "csv")) return Success(b, View(b, node.Name, type, node.Size, format, "unsupported", "", false));
        var read = await Host.Transfer.ReadAsync(call.AccountKey, node.Key, PluginFilesData.MaxReadBytes, ct).ConfigureAwait(false);
        if (!read.IsOk) return Fail(PluginErrorCode.Failed);
        var bytes = read.Value!;
        if (!LooksLikeText(bytes)) return Success(b, View(b, node.Name, type, bytes.Length, format, "unsupported", "", false));
        var (text, truncated) = Clip(Decode(bytes).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'), maxChars);
        return Success(b, View(b, node.Name, type, bytes.Length, format, "ok", text, truncated));
    }

    private static int View(PluginValueBuilder b, string name, string type, long size, string format, string status, string text, bool truncated) => b.Object(
        ("name", b.String(name)), ("type", b.String(type)), ("size", b.Number(size)), ("format", b.String(format)), ("status", b.String(status)), ("text", b.String(text)), ("truncated", b.Boolean(truncated)));

    /// <summary>index.ts:21-49 textFormatOf, reduced to the formats this host names: by the name's extension, else the media type.</summary>
    internal static string Format(string name, string type)
    {
        switch (PluginFileKinds.Extension(name))
        {
            case "txt" or "text" or "md" or "markdown" or "log" or "json" or "xml" or "yaml" or "yml" or "ini": return "text";
            case "tsv" or "csv": return "csv";
            case "htm" or "html" or "xhtml": return "html";
            case "ics" or "ical" or "ifb": return "ics";
            case "vcf" or "vcard": return "vcf";
            case "pdf": return "pdf";
            case "docx" or "docm" or "dotx": return "docx";
            case "xlsx" or "xlsm" or "xltx": return "xlsx";
            case "pptx" or "pptm" or "potx": return "pptx";
            case "odt" or "ott": return "odt";
            case "ods" or "ots": return "ods";
            case "odp" or "otp": return "odp";
        }

        var media = type.Split(';')[0].Trim().ToLowerInvariant();
        return media switch
        {
            "text/plain" or "text/markdown" or "application/json" or "application/xml" or "text/xml" => "text",
            "text/csv" or "text/tab-separated-values" => "csv",
            "text/html" or "application/xhtml+xml" => "html",
            "text/calendar" or "application/ics" => "ics",
            "text/vcard" or "text/x-vcard" or "text/directory" => "vcf",
            "application/pdf" => "pdf",
            _ => media.StartsWith("text/", StringComparison.Ordinal) ? "text" : "other",
        };
    }

    /// <summary>plain.ts:23-28: no NUL bytes in the first 4 KB (a UTF-16 byte order mark counts as text).</summary>
    private static bool LooksLikeText(byte[] bytes)
    {
        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF))) return true;
        return Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, 4096)) < 0;
    }

    /// <summary>plain.ts:10-21 decodeText: UTF-16 with a byte order mark, UTF-8 (the mark stripped), else Latin-1, which never fails.</summary>
    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        try
        {
            var text = new UTF8Encoding(false, true).GetString(bytes);
            return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    /// <summary>index.ts:60-68 clipText: cut at maxChars, at a line or word end when one is near, never inside a surrogate pair.</summary>
    private static (string Text, bool Truncated) Clip(string text, int maxChars)
    {
        if (text.Length <= maxChars) return (text, false);
        var end = char.IsHighSurrogate(text[maxChars - 1]) ? maxChars - 1 : maxChars;
        var cut = text[..end];
        var at = Math.Max(cut.LastIndexOf('\n'), cut.LastIndexOf(' '));
        return ((at > maxChars * 0.9 ? cut[..at] : cut).TrimEnd(), true);
    }

    private static bool TryParse(PluginValue arguments, out string key, out int maxChars)
    {
        (key, maxChars) = ("", DefaultChars);
        if (!PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0))) return false;
        key = arguments.Item(0).StringValue;
        return PluginMailArgs.TryOptions(PluginMailArgs.At(arguments, 1), Allowed, false, out var options) && PluginMailArgs.TryInteger(PluginMailArgs.Prop(options, "maxChars"), 1, MaxChars, DefaultChars, out maxChars);
    }
}

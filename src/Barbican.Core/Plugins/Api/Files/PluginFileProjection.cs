// Mirrors: Bulwark lib/plugin-sandbox/assistant/d.ts:136-150 infoOf and d.types.ts:30-52 (FileInfo: id, name, path, parentId, kind, mimeType, size, modified, and for another person's drive shared: true). Every field is named here:
// nothing is copied by reflection, so blob ids, share lists and rights never reach a plugin. Deviations: the id of a node in a shared account is "account|node" (Bulwark: "account:node"), and a shared node carries no owner name
// (the store does not hold it).
using System.Globalization;
using Barbican.Core.Files;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Files;

internal static class PluginFileProjection
{
    /// <summary>A text read is at most 100 000 characters, a picture 1.5 MB as base64.</summary>
    internal static readonly PluginValueLimits Limits = new(MaxWireBytes: 16 * 1024 * 1024, MaxNodes: 20_000, MaxStringBytes: 4 * 1024 * 1024, MaxSequenceLength: 1000);

    /// <summary>The file or folder as a plugin sees it. <paramref name="all"/> is every node of the login, for the path.</summary>
    internal static int Info(PluginValueBuilder b, IReadOnlyList<FileEntry> all, FileEntry node)
    {
        var fields = new List<(string, int)>
        {
            ("id", b.String(node.Key)), ("name", b.String(node.Name)), ("path", b.String(PluginFilesData.PathOf(all, node))),
            ("parentId", node.ParentKey is null ? b.Null() : b.String(node.ParentKey)), ("kind", b.String(node.IsDirectory ? "folder" : "file")),
            ("mimeType", b.String(node.IsDirectory ? "" : string.IsNullOrEmpty(node.MediaType) ? "application/octet-stream" : node.MediaType)),
            ("size", b.Number(node.IsDirectory ? 0 : node.Size)), ("modified", b.String(Time(node.Modified))),
        };
        if (node.Shared) fields.Add(("shared", b.Boolean(true)));
        return b.Object([.. fields]);
    }

    /// <summary>ISO 8601 UTC, "" when the server does not say.</summary>
    internal static string Time(DateTimeOffset? value) => value is { } t ? t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) : "";
}

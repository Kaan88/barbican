// Mirrors: Bulwark lib/plugin-sandbox/assistant/d.ts:208-247 doFilesFind and :48,680 (files:read): files and folders that match every given field (text words of the name or path, folderId at any depth, kind, modifiedAfter,
// modifiedBefore, limit 1 to 50 default 20), newest first. The Files trash and what is in it are never searched.
using Barbican.Core.Files;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Files;

internal sealed class PluginFilesFind(IPluginFilesHost host) : PluginFilesMethod("files.find", "files:read", host, false)
{
    private static readonly string[] Allowed = ["text", "folderId", "kind", "modifiedAfter", "modifiedBefore", "limit"];

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => TryParse(arguments, out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginFilesCall call, PluginValue arguments, CancellationToken ct)
    {
        _ = TryParse(arguments, out var q);
        var snapshot = await PluginFilesData.SnapshotAsync(Host, call.Session, ct).ConfigureAwait(false);
        if (snapshot is null) return Fail(PluginErrorCode.Unavailable);
        if (!await call.Context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var nodes = PluginFilesData.Visible(snapshot);
        HashSet<string>? below = null;
        if (q.FolderId is not null)
        {
            if (PluginFilesData.Find(nodes, q.FolderId) is not { } folder) return Fail(PluginErrorCode.Unavailable);
            if (!folder.IsDirectory) return Fail(PluginErrorCode.InvalidArguments);
            below = Below(nodes, folder.Key);
        }

        var hits = nodes.Where(n =>
        {
            if (below is not null && !below.Contains(n.Key)) return false;
            if (q.Kind is not null && !PluginFileKinds.Matches(n, q.Kind)) return false;
            if (q.After is { } after && PluginFileKinds.ModifiedAt(n) < after) return false;
            if (q.Before is { } before && PluginFileKinds.ModifiedAt(n) >= before) return false;
            if (q.Words.Count == 0) return true;
            var hay = (n.Name + " " + PluginFilesData.PathOf(snapshot.Entries, n)).ToLowerInvariant();
            return q.Words.All(w => hay.Contains(w, StringComparison.Ordinal));
        }).OrderByDescending(PluginFileKinds.ModifiedAt).Take(q.Limit).ToList();
        var b = new PluginValueBuilder();
        return Success(b, b.Array([.. hits.Select(n => PluginFileProjection.Info(b, snapshot.Entries, n))]));
    }

    /// <summary>Every node below the folder, at any depth (the folder itself left out).</summary>
    private static HashSet<string> Below(IReadOnlyList<FileEntry> nodes, string folderKey)
    {
        var byParent = nodes.Where(n => n.ParentKey is not null).GroupBy(n => n.ParentKey!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var below = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(folderKey);
        while (queue.Count > 0)
        {
            foreach (var child in byParent.GetValueOrDefault(queue.Dequeue()) ?? [])
            {
                if (below.Add(child.Key)) queue.Enqueue(child.Key);
            }
        }

        return below;
    }

    private sealed record Query(IReadOnlyList<string> Words, string? FolderId, string? Kind, DateTimeOffset? After, DateTimeOffset? Before, int Limit);

    private static bool TryParse(PluginValue arguments, out Query query)
    {
        query = new([], null, null, null, null, PluginFilesData.DefaultFindResults);
        if (!PluginMailArgs.TryOptions(PluginMailArgs.At(arguments, 0), Allowed, false, out var options)) return false;
        IReadOnlyList<string> words = [];
        string? folder = null, kind = null;
        DateTimeOffset? after = null, before = null;
        if (PluginMailArgs.Prop(options, "text") is { } text)
        {
            if (!PluginMailArgs.TryString(text, 200, out var raw)) return false;
            words = [.. raw.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)];
        }

        if (PluginMailArgs.Prop(options, "folderId") is { } folderId)
        {
            if (!PluginMailArgs.IsId(folderId)) return false;
            folder = folderId.StringValue;
        }

        if (PluginMailArgs.Prop(options, "kind") is { } k)
        {
            if (k.Kind != PluginValueKind.String || !PluginFileKinds.Kinds.Contains(k.StringValue, StringComparer.Ordinal)) return false;
            kind = k.StringValue;
        }

        if (!Date(PluginMailArgs.Prop(options, "modifiedAfter"), out after) || !Date(PluginMailArgs.Prop(options, "modifiedBefore"), out before)) return false;
        if (!PluginMailArgs.TryInteger(PluginMailArgs.Prop(options, "limit"), 1, PluginFilesData.MaxFindResults, PluginFilesData.DefaultFindResults, out var limit)) return false;
        query = new(words, folder, kind, after, before, limit);
        return true;
    }

    private static bool Date(PluginValue? value, out DateTimeOffset? date)
    {
        date = null;
        if (value is null) return true;
        if (!PluginMailArgs.TryString(value, 40, out var raw) || raw.Length == 0 || !PluginFileKinds.TryDate(raw, out var parsed)) return false;
        date = parsed;
        return true;
    }
}

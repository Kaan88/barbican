// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1665-1795 doKeywordsList / Add / Update / Remove / Reorder and stores/settings-store.ts:1561 (a plugin's
// write makes the list "chosen"). The definitions are the user's own: the very `emailKeywords` setting the sidebar, the tag menus and Settings read, written
// through the same IPrefsEditor the UI's TagService writes through, so the sidebar updates when the add-on writes. Core cannot reference TagService
// (Presentation); TagService only parses with TagRules, edits and Puts, and this class does the same with the same rules and the same key.
// Ownership: Bulwark keeps `addedBy` inside each definition and only that plugin may update or remove it. TagRules.Parse/ToValue keep only
// id, label and colour, so Barbican keeps `addedBy` and `visibility` in a separate settings object `pluginKeywordMeta` ({ id: { addedBy, visibility } }).
// An entry whose tag is gone is ignored and dropped on the next write. `visibility` is stored and returned but the sidebar does not use it yet.
using System.Text.Json.Nodes;
using Barbican.Core.Mail.Tags;
using Barbican.Core.Storage;

namespace Barbican.Core.Plugins.Api.MailTags;

/// <summary>Add-on access to the user's tag definitions, serialized so two add-ons cannot overwrite each other's change.</summary>
public sealed class PluginTagDefinitions(IPrefsStore prefs, IPrefsEditor editor)
{
    internal const int MaxDefinitions = 500;
    internal const int MaxLabelLength = 255;
    private const string DefsKey = "emailKeywords";
    private const string MetaKey = "pluginKeywordMeta";
    private readonly Lock _gate = new();

    internal IReadOnlyList<PluginKeywordView> List()
    {
        lock (_gate) return Read();
    }

    internal IReadOnlyList<string> Ids()
    {
        lock (_gate) return [.. TagRules.Parse(prefs.Current.Values[DefsKey]).Select(d => d.Id)];
    }

    internal (IReadOnlyList<PluginKeywordView> Added, IReadOnlyList<string> Skipped) Add(string pluginId, IReadOnlyList<PluginKeywordInput> inputs)
    {
        lock (_gate)
        {
            var views = Read().ToList();
            var known = new HashSet<string>(views.Select(v => v.Def.Id.ToLowerInvariant()), StringComparer.Ordinal);
            var taken = new HashSet<string>(views.Select(v => v.Def.Color), StringComparer.Ordinal);
            var added = new List<PluginKeywordView>();
            var skipped = new List<string>();
            foreach (var input in inputs)
            {
                if (!known.Add(input.Id.ToLowerInvariant()))
                {
                    skipped.Add(input.Id);
                    continue;
                }

                var color = input.Color ?? SuggestColor(input.Id, taken);
                taken.Add(color);
                var view = new PluginKeywordView(new TagDef(input.Id, input.Label!, color), input.Visibility, pluginId);
                added.Add(view);
                views.Add(view);
            }

            if (added.Count > 0) Write(views);
            return (added, skipped);
        }
    }

    internal (IReadOnlyList<PluginKeywordView> Updated, IReadOnlyList<string> Skipped) Update(string pluginId, IReadOnlyList<PluginKeywordInput> patches)
    {
        lock (_gate)
        {
            var views = Read().ToList();
            var byId = patches.GroupBy(p => p.Id.ToLowerInvariant(), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
            var updated = new List<PluginKeywordView>();
            for (var i = 0; i < views.Count; i++)
            {
                var v = views[i];
                if (!string.Equals(v.AddedBy, pluginId, StringComparison.Ordinal) || !byId.TryGetValue(v.Def.Id.ToLowerInvariant(), out var patch)) continue;
                // the id is what the messages carry, so it never changes here
                var next = new PluginKeywordView(new TagDef(v.Def.Id, patch.Label ?? v.Def.Label, patch.Color ?? v.Def.Color), patch.Visibility ?? v.Visibility, v.AddedBy);
                views[i] = next;
                updated.Add(next);
            }

            var done = new HashSet<string>(updated.Select(u => u.Def.Id.ToLowerInvariant()), StringComparer.Ordinal);
            if (updated.Count > 0) Write(views);
            return (updated, [.. patches.Where(p => !done.Contains(p.Id.ToLowerInvariant())).Select(p => p.Id)]);
        }
    }

    internal (IReadOnlyList<string> Removed, IReadOnlyList<string> Skipped) Remove(string pluginId, IReadOnlyList<string> ids)
    {
        lock (_gate)
        {
            var views = Read().ToList();
            var wanted = new HashSet<string>(ids.Select(i => i.ToLowerInvariant()), StringComparer.Ordinal);
            var removed = new List<string>();
            views.RemoveAll(v =>
            {
                var drop = string.Equals(v.AddedBy, pluginId, StringComparison.Ordinal) && wanted.Contains(v.Def.Id.ToLowerInvariant());
                if (drop) removed.Add(v.Def.Id);
                return drop;
            });
            if (removed.Count > 0) Write(views);
            var done = new HashSet<string>(removed.Select(r => r.ToLowerInvariant()), StringComparer.Ordinal);
            return (removed, [.. ids.Where(i => !done.Contains(i.ToLowerInvariant()))]);
        }
    }

    /// <summary>Null when <paramref name="ids"/> is not every existing id exactly once (Bulwark throws); otherwise the new order.</summary>
    internal IReadOnlyList<PluginKeywordView>? Reorder(IReadOnlyList<string> ids, bool caseSensitive)
    {
        lock (_gate)
        {
            var views = Read();
            if (ids.Count != views.Count) return null;
            string Norm(string id) => caseSensitive ? id : id.ToLowerInvariant();
            var byId = new Dictionary<string, PluginKeywordView>(StringComparer.Ordinal);
            foreach (var v in views)
            {
                if (!byId.TryAdd(Norm(v.Def.Id), v)) return null;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var ordered = new List<PluginKeywordView>();
            foreach (var id in ids)
            {
                if (!seen.Add(Norm(id)) || !byId.TryGetValue(Norm(id), out var view)) return null;
                ordered.Add(view);
            }

            Write(ordered);
            return ordered;
        }
    }

    private List<PluginKeywordView> Read()
    {
        var defs = TagRules.Parse(prefs.Current.Values[DefsKey]);
        var meta = prefs.Current.Values[MetaKey] as JsonObject ?? [];
        return [.. defs.Select(d =>
        {
            var entry = meta[d.Id] as JsonObject;
            var owner = entry?["addedBy"] is JsonValue o && o.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;
            var visibility = entry?["visibility"] is JsonValue vis && vis.TryGetValue<string>(out var vs) && vs is "show" or "hide" or "unread" ? vs : null;
            return new PluginKeywordView(d, visibility, owner);
        })];
    }

    private void Write(IReadOnlyList<PluginKeywordView> views)
    {
        var meta = new JsonObject();
        foreach (var v in views.Where(v => v.AddedBy is not null || v.Visibility is not null))
        {
            var entry = new JsonObject();
            if (v.AddedBy is not null) entry["addedBy"] = v.AddedBy;
            if (v.Visibility is not null) entry["visibility"] = v.Visibility;
            meta[v.Def.Id] = entry;
        }

        editor.Put(DefsKey, TagRules.ToValue([.. views.Select(v => v.Def)]));
        editor.Put(MetaKey, meta.Count == 0 ? null : meta);
    }

    /// <summary>Bulwark lib/keyword-discovery.ts:85-120 suggestKeywordColor: the id's own last level when it names a hue, else a stable hash into the base hues, skipping taken ones.</summary>
    internal static string SuggestColor(string id, ISet<string> taken)
    {
        var level = id.Split(TagRules.Separator, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? id;
        if (PluginKeywordArgs.IsPaletteKey(level)) return level;
        var hues = TagRules.Palette;
        long hash = 0;
        foreach (var unit in id) hash = unchecked((int)((hash * 31) + unit));
        var start = (int)(Math.Abs(hash) % hues.Count);
        for (var step = 0; step < hues.Count; step++)
        {
            var candidate = hues[(start + step) % hues.Count];
            if (!taken.Contains(candidate)) return candidate;
        }

        return hues[start];
    }

}

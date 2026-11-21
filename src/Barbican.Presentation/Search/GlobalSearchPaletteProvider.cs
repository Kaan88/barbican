// Mirrors: Rust palette.rs:355 search (the query fanned out to each kind, ranked, five rows per kind) and gsearch.rs:445 score / :460 rank; Bulwark lib/global-search/{run-global-search,rank}.ts and providers/,
// components/global-search/global-search-palette.tsx:84-145 (kinds to show, MAX_ROWS_PER_KIND 5, a group per kind) and :215-225 + search-scope-chips.tsx (the scope chips, sugar over in:; an in: operator in the query wins),
// search-facets.tsx:60-78 (the count of each kind), search-preview.tsx (the preview of the selected hit). I-455.
// Mail comes from Email/query with SearchSnippet marks; contacts, calendar and files from the stores (GlobalLocalSearch). Plugins add entries through onProvideSearchResults (transform); an entry that names a message opens it,
// the others come back as onAction (M6b).

using Barbican.Core.Mail.Service;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Search;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Palette;

namespace Barbican.Presentation.Search;

/// <summary>The palette's "Search" scope: global search over the account, narrowed by scope chips, with a preview of the highlighted hit.</summary>
public sealed class GlobalSearchPaletteProvider(IActiveMail mail, IHookBus hooks, Func<string, string> folderName, Func<string> openFolderId, IGlobalSearchSources? sources = null)
    : IPaletteFacetProvider, IPalettePreviewProvider
{
    /// <summary>The scope's name (the palette's tab).</summary>
    public const string ScopeName = "Search";

    /// <summary>How many rows one kind shows while every kind does (Rust ROWS_PER_KIND, Bulwark MAX_ROWS_PER_KIND).</summary>
    public const int RowsPerKind = 5;

    /// <summary>How many rows the one kind shows while a scope chip narrows the list to it.</summary>
    public const int RowsForOneKind = 10;

    /// <summary>The row id prefix of a mail hit: <c>search-hit:mail:{emailId}:{folderId}</c>.</summary>
    public const string MailHitPrefix = "search-hit:mail:";

    /// <summary>The row id prefix of a contact hit: <c>search-hit:contact:{cardId}</c>.</summary>
    public const string ContactHitPrefix = "search-hit:contact:";

    /// <summary>The row id prefix of an event hit: <c>search-hit:event:{eventId}:{yyyy-MM-dd}</c>.</summary>
    public const string EventHitPrefix = "search-hit:event:";

    /// <summary>The row id prefix of a file hit: <c>search-hit:file:{d|f}:{folderKey};{fileKey}</c> (d: the hit is a folder).</summary>
    public const string FileHitPrefix = "search-hit:file:";

    private static readonly (SearchScope Kind, string Key, string Label, string Icon)[] Kinds =
    [
        (SearchScope.Mail, "mail", "Mail", "mail"),
        (SearchScope.Contacts, "contacts", "Contacts", "users"),
        (SearchScope.Calendar, "calendar", "Calendar", "cal"),
        (SearchScope.Files, "files", "Files", "file"),
    ];

    private readonly Dictionary<string, SearchHit> _byRow = new(StringComparer.Ordinal);
    private Found _found = Found.None;
    private string _foundFor = string.Empty;
    private SearchScope _chip = SearchScope.All;

    /// <inheritdoc />
    public string Scope => ScopeName;

    /// <summary>The scope chip that is on (an in: operator in the query overrides it).</summary>
    public SearchScope Chip => _chip;

    /// <inheritdoc />
    public IReadOnlyList<PaletteFacet> Facets
    {
        get
        {
            var shown = _lastEffective;
            var total = Kinds.Sum(k => _found.Hits(k.Kind).Count);
            var chips = new List<PaletteFacet> { new("all", "All", total, Kinds.Any(k => _found.More(k.Kind)), shown == SearchScope.All) };
            chips.AddRange(Kinds.Select(k => new PaletteFacet(k.Key, k.Label, _found.Hits(k.Kind).Count, _found.More(k.Kind), shown == k.Kind)));
            return chips;
        }
    }

    private SearchScope _lastEffective = SearchScope.All;

    /// <summary>The row id that opens a mail hit.</summary>
    public static string MailHitId(SearchHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        return $"{MailHitPrefix}{hit.Id}:{hit.Place}";
    }

    /// <summary>The row id that opens a hit of any kind.</summary>
    public static string HitId(SearchHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        return hit.Kind switch
        {
            SearchScope.Contacts => ContactHitPrefix + hit.Id,
            SearchScope.Calendar => $"{EventHitPrefix}{hit.Id}:{hit.Place}",
            SearchScope.Files => $"{FileHitPrefix}{(hit.IsFolder ? 'd' : 'f')}:{hit.Place};{hit.Id}",
            _ => MailHitId(hit),
        };
    }

    /// <inheritdoc />
    public void SetFacet(string key)
    {
        _chip = key switch
        {
            "mail" => SearchScope.Mail,
            "contacts" => SearchScope.Contacts,
            "calendar" => SearchScope.Calendar,
            "files" => SearchScope.Files,
            "all" => SearchScope.All,
            _ => _chip,
        };
    }

    /// <inheritdoc />
    public string NextFacetKey(int delta)
    {
        var keys = Facets.Select(f => f.Key).ToList();
        var at = keys.IndexOf(Facets.FirstOrDefault(f => f.Active)?.Key ?? "all");
        return keys[(((at + delta) % keys.Count) + keys.Count) % keys.Count];
    }

    /// <inheritdoc />
    public PalettePreview? PreviewFor(PaletteCommand row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return _byRow.TryGetValue(row.Id, out var hit) ? SearchPreviews.For(hit) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PaletteCommand>> RowsAsync(string text, PaletteContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        text ??= string.Empty;
        var parsed = GlobalSearch.Parse(text);
        // a chip changes what shows, not what was found: the same text keeps its answer
        if (text.Length == 0 || !string.Equals(text, _foundFor, StringComparison.Ordinal))
        {
            _found = text.Trim().Length == 0 ? Found.None : await SearchAsync(parsed, ct).ConfigureAwait(false);
            _foundFor = text;
        }

        var effective = parsed.Scope != SearchScope.All ? parsed.Scope : _chip;
        _lastEffective = effective;
        _byRow.Clear();
        var rows = new List<PaletteCommand>();
        foreach (var (kind, _, label, icon) in Kinds)
        {
            if (effective != SearchScope.All && effective != kind)
            {
                continue;
            }

            var limit = effective == SearchScope.All ? RowsPerKind : RowsForOneKind;
            foreach (var h in _found.Hits(kind).Take(limit))
            {
                var id = HitId(h);
                _byRow[id] = h;
                rows.Add(new PaletteCommand(id, label, icon, h.Subtitle.Length > 0 ? $"{h.Title} · {h.Subtitle}" : h.Title, [], string.Empty));
            }
        }

        if (hooks.HasHandlers(HookNames.OnProvideSearchResults) && (effective is SearchScope.All or SearchScope.Mail))
        {
            var extra = await hooks.TransformAsync(HookNames.OnProvideSearchResults, new ProvideSearchResultsPayload(text, []), ct).ConfigureAwait(false);
            foreach (var e in extra.Results)
            {
                var id = e.EmailId is { Length: > 0 } email ? $"{MailHitPrefix}{email}:" : $"search-hit:plugin:{e.Key}";
                rows.Add(new PaletteCommand(id, "Plugins", "plug", e.Snippet.Length > 0 ? $"{e.Title} · {e.Snippet}" : e.Title, [], string.Empty));
            }
        }

        return rows;
    }

    private async Task<Found> SearchAsync(ParsedSearch parsed, CancellationToken ct)
    {
        var mailTask = MailHitsAsync(parsed, ct);
        var dataTask = sources is not null && NeedsStores(parsed) ? sources.LoadAsync(ct) : Task.FromResult(GlobalSearchData.Empty);
        var mailHits = await mailTask.ConfigureAwait(false);
        var data = await dataTask.ConfigureAwait(false);
        IReadOnlyList<SearchHit> Pick(SearchScope kind, Func<IReadOnlyList<SearchHit>> make) =>
            WantsKind(parsed, kind) ? GlobalSearch.Rank(make(), parsed).Take(GlobalMailSearch.RemoteLimit).ToList() : [];
        return new Found(
            mailHits,
            Pick(SearchScope.Contacts, () => GlobalLocalSearch.Contacts(data.Cards, data.Books, parsed)),
            Pick(SearchScope.Calendar, () => GlobalLocalSearch.Events(data.Events, data.Calendars, parsed)),
            Pick(SearchScope.Files, () => GlobalLocalSearch.Files(data.Files, parsed)),
            mailHits.Count >= GlobalMailSearch.RemoteLimit);
    }

    private static bool WantsKind(ParsedSearch parsed, SearchScope kind) => parsed.Scope is SearchScope.All || parsed.Scope == kind;

    private static bool NeedsStores(ParsedSearch parsed) => parsed.Terms.Count > 0 && parsed.Scope != SearchScope.Mail;

    private async Task<IReadOnlyList<SearchHit>> MailHitsAsync(ParsedSearch parsed, CancellationToken ct)
    {
        if (!GlobalSearch.HasRemoteQuery(parsed, SearchScope.Mail) || !WantsKind(parsed, SearchScope.Mail) || mail.Mail is not MailService service)
        {
            return [];
        }

        var roles = await service.GetRoleIdsAsync(ct).ConfigureAwait(false);
        if (!roles.IsOk)
        {
            return [];
        }

        IReadOnlyList<string> prefer = [openFolderId(), roles.Value!.GetValueOrDefault("inbox", string.Empty)];
        var hits = await GlobalMailSearch.RunAsync(service.Client, parsed, roles.Value!, prefer, folderName, ct).ConfigureAwait(false);
        return hits.IsOk ? GlobalSearch.Rank(hits.Value!, parsed) : [];
    }

    /// <summary>The hits of the last search by kind.</summary>
    private sealed record Found(IReadOnlyList<SearchHit> Mail, IReadOnlyList<SearchHit> Contacts, IReadOnlyList<SearchHit> Calendar, IReadOnlyList<SearchHit> Files, bool MailMore)
    {
        public static Found None { get; } = new([], [], [], [], false);

        public IReadOnlyList<SearchHit> Hits(SearchScope kind) => kind switch
        {
            SearchScope.Mail => Mail,
            SearchScope.Contacts => Contacts,
            SearchScope.Calendar => Calendar,
            SearchScope.Files => Files,
            _ => [],
        };

        public bool More(SearchScope kind) => kind == SearchScope.Mail && MailMore;
    }
}

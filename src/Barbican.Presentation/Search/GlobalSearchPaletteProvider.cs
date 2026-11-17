// Mirrors: Rust palette.rs:355 search (the query fanned out to each kind, ranked, five rows per kind) and gsearch.rs:445 score / :460 rank; Bulwark lib/global-search/{run-global-search,rank}.ts and
// providers/. I-455. Mail is searched; the contacts, calendar and files providers return nothing until M4/M5 (their pure filters, GlobalSearch.FilterFiles, are ported and tested).
// Plugins add entries through onProvideSearchResults (transform); an entry that names a message opens it, the others come back as onAction (M6b).

using Barbican.Core.Mail.Service;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Search;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Palette;

namespace Barbican.Presentation.Search;

/// <summary>The palette's "Search" scope: global search over the account.</summary>
public sealed class GlobalSearchPaletteProvider(IActiveMail mail, IHookBus hooks, Func<string, string> folderName, Func<string> openFolderId) : IPaletteProvider
{
    /// <summary>The scope's name (the palette's tab).</summary>
    public const string ScopeName = "Search";

    /// <summary>How many rows one kind shows (Rust ROWS_PER_KIND).</summary>
    public const int RowsPerKind = 5;

    /// <summary>The row id prefix of a mail hit: <c>search-hit:mail:{emailId}:{folderId}</c>.</summary>
    public const string MailHitPrefix = "search-hit:mail:";

    /// <inheritdoc />
    public string Scope => ScopeName;

    /// <summary>The row id that opens a mail hit.</summary>
    public static string MailHitId(SearchHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        return $"{MailHitPrefix}{hit.Id}:{hit.Place}";
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PaletteCommand>> RowsAsync(string text, PaletteContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        var parsed = GlobalSearch.Parse(text ?? string.Empty);
        var rows = new List<PaletteCommand>();
        if (GlobalSearch.HasRemoteQuery(parsed, SearchScope.Mail) && (parsed.Scope is SearchScope.All or SearchScope.Mail) && mail.Mail is MailService service)
        {
            var roles = await service.GetRoleIdsAsync(ct).ConfigureAwait(false);
            if (roles.IsOk)
            {
                IReadOnlyList<string> prefer = [openFolderId(), roles.Value!.GetValueOrDefault("inbox", string.Empty)];
                var hits = await GlobalMailSearch.RunAsync(service.Client, parsed, roles.Value!, prefer, folderName, ct).ConfigureAwait(false);
                if (hits.IsOk)
                {
                    foreach (var h in GlobalSearch.Rank(hits.Value!, parsed).Take(RowsPerKind))
                    {
                        rows.Add(new PaletteCommand(MailHitId(h), "Mail", "mail", h.Subtitle.Length > 0 ? $"{h.Title} · {h.Subtitle}" : h.Title, [], string.Empty));
                    }
                }
            }
        }

        if (hooks.HasHandlers(HookNames.OnProvideSearchResults))
        {
            var extra = await hooks.TransformAsync(HookNames.OnProvideSearchResults, new ProvideSearchResultsPayload(text ?? string.Empty, []), ct).ConfigureAwait(false);
            foreach (var e in extra.Results)
            {
                var id = e.EmailId is { Length: > 0 } email ? $"{MailHitPrefix}{email}:" : $"search-hit:plugin:{e.Key}";
                rows.Add(new PaletteCommand(id, "Plugins", "plug", e.Snippet.Length > 0 ? $"{e.Title} · {e.Snippet}" : e.Title, [], string.Empty));
            }
        }

        return rows;
    }
}

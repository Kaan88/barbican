// Mirrors: Bulwark lib/plugin-sandbox/host-api-account.ts:100-212 doUserFindPeople (directory:read, host-api.ts:194): people from the server's directory, by principal id (1 to 20 ids) or by text in the name or address (3 to 200 characters,
// case ignored); at most 20 results; 10 lookups a minute per add-on; { people: [{ id, type, name, email }], complete }, complete false when the directory could not be listed in full (the server does not list it: only the people the
// login already shares an account with are known). An account the add-on names (accountId) must be one the captured login shows.
// Deviations: Barbican's directory service answers per login, so every account of the login sees the same listing; Barbican has no administrator directory scope to apply (none exists in its organisation policy); a login whose
// server has no directory, or whose Contacts setting "Show directory" is off, answers { people: [], complete: false } (Bulwark does the same without principals); a listing that fails is not kept for the next lookup.
using Barbican.Core.Contacts;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Account;

internal sealed class PluginUserFindPeople(IContactDirectory directory, PluginDirectoryCache cache, IPluginMailRateLimiter limiter)
    : PluginScopedMethod("user.findPeople", "directory:read", slow: true, limiter: limiter)
{
    /// <summary>Most people one call returns (host-api-account.ts:103).</summary>
    internal const int MaxPeople = 20;

    /// <summary>Most ids one call may ask for (host-api-account.ts:105).</summary>
    internal const int MaxIds = 20;

    /// <summary>The shortest search text (host-api-account.ts:108).</summary>
    internal const int MinText = 3;

    /// <summary>Lookups one add-on may make a minute (host-api-account.ts:110).</summary>
    internal const int PerMinute = 10;

    private const int MaxText = 200;
    private static readonly string[] Allowed = ["text", "ids", "accountId"];
    private readonly IContactDirectory _directory = directory ?? throw new ArgumentNullException(nameof(directory));
    private readonly PluginDirectoryCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));

    protected override string? RateKind => "directory";

    protected override int RateLimit => PerMinute;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => TryParse(arguments, out _, out _, out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        if (!TryParse(arguments, out var ids, out var text, out var accountId)) return PluginCallOutcome.Failed(PluginErrorCode.InvalidArguments);
        var session = capture!.Session;
        // "no mail account": only the login's own account or one it shows
        if (!PluginMailRouting.Resolve(session, accountId).Allowed) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
        var listing = _cache.Get(capture.LocalAccountKey);
        if (listing is null)
        {
            listing = await LoadAsync(capture.LocalAccountKey, ct).ConfigureAwait(false);
            if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        }

        var found = listing.People.Where(p => (ids is null || ids.Contains(p.Id)) && (text.Length == 0 || p.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || p.Email.Contains(text, StringComparison.Ordinal))).Take(MaxPeople);
        var b = new PluginValueBuilder();
        var people = found.Select(p => b.Object(("id", b.String(p.Id)), ("type", b.String(p.Type)), ("name", b.String(p.Name)), ("email", b.String(p.Email)))).ToArray();
        return Success(b, b.Object(("people", b.Array(people)), ("complete", b.Boolean(listing.Complete))));
    }

    private async Task<PluginDirectoryCache.Listing> LoadAsync(string loginKey, CancellationToken ct)
    {
        var empty = new PluginDirectoryCache.Listing(false, []);
        if (!_directory.IsAvailable(loginKey)) return empty;
        var loaded = await _directory.LoadAsync(loginKey, ct).ConfigureAwait(false);
        // a failed load is answered empty and incomplete and not kept: the next lookup tries again
        if (!loaded.IsOk) return empty;
        var listing = new PluginDirectoryCache.Listing(!loaded.Value.Fallback, [.. loaded.Value.Principals.Select(PluginDirectoryCache.Project).OfType<PluginDirectoryCache.Person>()]);
        _cache.Keep(loginKey, listing);
        return listing;
    }

    private static bool TryParse(PluginValue arguments, out HashSet<string>? ids, out string text, out string? accountId)
    {
        (ids, text, accountId) = (null, "", null);
        if (!PluginMailArgs.TryOptions(PluginMailArgs.At(arguments, 0), Allowed, true, out var options)) return false;
        if (PluginMailArgs.Prop(options, "ids") is { } list)
        {
            if (list.Kind != PluginValueKind.Array || list.Count is 0 or > MaxIds) return false;
            ids = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < list.Count; i++)
            {
                if (!PluginMailArgs.IsId(list.Item(i))) return false;
                _ = ids.Add(list.Item(i).StringValue);
            }
        }

        if (PluginMailArgs.Prop(options, "text") is { } raw)
        {
            if (!PluginMailArgs.TryString(raw, MaxText, out var typed)) return false;
            text = typed.Trim().ToLowerInvariant();
        }

        if (ids is null && text.Length == 0) return false;
        if (text.Length is > 0 and < MinText) return false;
        return PluginMailArgs.TryAccount(PluginMailArgs.Prop(options, "accountId"), out accountId);
    }
}

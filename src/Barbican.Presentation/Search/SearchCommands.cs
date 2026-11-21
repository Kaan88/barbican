// Mirrors: Bulwark components/layout/navigation-rail.tsx:671 (the rail's search button opens the global search); Rust palette.rs:550 open_hit; Bulwark lib/global-search/open-hit.ts. I-023, I-455.
// search.global opens the palette on the Search scope; search.open-hit opens a hit (argument "mail:{emailId}:{folderId}", "contact:{cardId}", "event:{eventId}:{yyyy-MM-dd}", "file:{d|f}:{folderKey};{fileKey}").

using System.Globalization;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Palette;

namespace Barbican.Presentation.Search;

/// <summary>The commands of global search.</summary>
public static class SearchCommands
{
    /// <summary>The commands (the palette is resolved when the command runs, so the palette can depend on the registry).</summary>
    public static IReadOnlyList<IAppCommand> Create(Lazy<PaletteController> palette, SearchHitOpener opener)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(opener);
        return
        [
            DelegateAppCommand.Sync(CommandIds.GlobalSearch, ctx => { _ = palette.Value.OpenAsync(GlobalSearchPaletteProvider.ScopeName); }),
            new DelegateAppCommand(CommandIds.OpenSearchHit, c => c.Argument is { Length: > 0 }, async (c, _) => await OpenAsync(opener, c.Argument!).ConfigureAwait(true)),
        ];
    }

    /// <summary>Opens the hit a row names; a failed result says why it could not be opened.</summary>
    public static async Task<Result<Unit>> OpenAsync(SearchHitOpener opener, string argument)
    {
        ArgumentNullException.ThrowIfNull(opener);
        ArgumentNullException.ThrowIfNull(argument);
        var cannot = Result<Unit>.Fail(new ValidationError("That result cannot be opened."));
        if (argument.StartsWith("contact:", StringComparison.Ordinal))
        {
            var id = argument["contact:".Length..];
            return id.Length > 0 && opener.OpenContact(id) ? Result<Unit>.Ok(default) : cannot;
        }

        if (argument.StartsWith("event:", StringComparison.Ordinal))
        {
            var rest = argument["event:".Length..];
            var colon = rest.LastIndexOf(':');
            return colon > 0 && DateOnly.TryParseExact(rest[(colon + 1)..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && opener.OpenEvent(rest[..colon], day)
                ? Result<Unit>.Ok(default)
                : cannot;
        }

        if (argument.StartsWith("file:", StringComparison.Ordinal))
        {
            var rest = argument["file:".Length..];
            var semi = rest.IndexOf(';', StringComparison.Ordinal);
            return rest.Length > 2 && semi > 1 && rest[1] == ':' && semi + 1 < rest.Length && opener.OpenFile(rest[(semi + 1)..], rest[2..semi], rest[0] == 'd')
                ? Result<Unit>.Ok(default)
                : cannot;
        }

        var (emailId, folderId) = Parse(argument);
        if (emailId.Length == 0)
        {
            return cannot;
        }

        var opened = await opener.OpenMailAsync(emailId, folderId).ConfigureAwait(true);
        return opened ? Result<Unit>.Ok(default) : Result<Unit>.Fail(new ValidationError("That message is not in the first page of its folder."));
    }

    /// <summary>The message and folder of a hit argument <c>mail:{emailId}:{folderId}</c>.</summary>
    public static (string EmailId, string FolderId) Parse(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        var rest = argument.StartsWith("mail:", StringComparison.Ordinal) ? argument["mail:".Length..] : string.Empty;
        var at = rest.IndexOf(':', StringComparison.Ordinal);
        return at < 0 ? (rest, string.Empty) : (rest[..at], rest[(at + 1)..]);
    }
}

// Mirrors: Bulwark components/layout/navigation-rail.tsx:671 (the rail's search button opens the global search); Rust palette.rs:550 open_hit. I-023, I-455.
// search.global opens the palette on the Search scope; search.open-hit opens a hit (argument "mail:{emailId}:{folderId}").

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
            new DelegateAppCommand(CommandIds.OpenSearchHit, c => c.Argument is { Length: > 0 }, async (c, _) =>
            {
                var (emailId, folderId) = Parse(c.Argument!);
                if (emailId.Length == 0)
                {
                    return Result<Unit>.Fail(new ValidationError("That result cannot be opened."));
                }

                var opened = await opener.OpenMailAsync(emailId, folderId).ConfigureAwait(true);
                return opened ? Result<Unit>.Ok(default) : Result<Unit>.Fail(new ValidationError("That message is not in the first page of its folder."));
            }),
        ];
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

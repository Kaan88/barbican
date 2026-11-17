// Mirrors: M2 plan section 3.3 (multi-registrations: IPaletteProvider) and 6.2 (providers: search adds the global-search scope in wave 2). Contract from stage F of M2; owner menus.

namespace Barbican.Presentation.Palette;

/// <summary>Adds scopes or rows to the command palette.</summary>
public interface IPaletteProvider
{
    /// <summary>The scope name (a tab in the palette), for example "Commands" or "Search".</summary>
    string Scope { get; }

    /// <summary>Rows for what was typed; app commands come from <see cref="PaletteModel"/>.</summary>
    Task<IReadOnlyList<PaletteCommand>> RowsAsync(string text, PaletteContext context, CancellationToken ct);
}

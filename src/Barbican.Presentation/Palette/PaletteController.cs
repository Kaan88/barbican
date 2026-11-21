// Mirrors: Rust palette.rs open :246 / rebuild :269 / move_active :423 / enter :437 / run_command :485; Bulwark components/command-palette (Ctrl+K and k open it, arrows move,
// Enter runs, Esc closes, Tab switches the scope; every word typed must match). A row whose command has no handler shows disabled (M2 plan 4.1). Owner menus.

using Barbican.Presentation.Actions;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Palette;

/// <summary>The command palette's state: what is typed, the scope, the rows and the highlighted one.</summary>
public sealed class PaletteController : ObservableObject, IModalSurface
{
    /// <summary>The scope of the app's own commands.</summary>
    public const string CommandsScope = "Commands";

    private readonly ICommandRegistry _registry;
    private readonly IPaletteContextSource _source;
    private readonly IReadOnlyList<IPaletteProvider> _providers;
    private readonly CommandRunner _runner;
    private PaletteContext _context = new();
    private string _text = string.Empty;
    private string _scope = CommandsScope;
    private List<PaletteRow> _rows = [];
    private int _highlight = -1;
    private bool _isOpen;
    private int _generation;
    private PalettePreview? _preview;

    public PaletteController(ICommandRegistry registry, IPaletteContextSource source, IEnumerable<IPaletteProvider> providers, IToastService? toasts = null, CommandRunner? runner = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(providers);
        _registry = registry;
        _source = source;
        _providers = providers.ToList();
        _runner = runner ?? new CommandRunner(toasts);
    }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    public string Text => _text;

    public string Scope => _scope;

    /// <summary>The scopes (tabs): Commands, then one per provider.</summary>
    public IReadOnlyList<string> Scopes => [CommandsScope, .. _providers.Select(p => p.Scope)];

    public IReadOnlyList<PaletteRow> Rows => _rows;

    /// <summary>The highlighted line, -1 for none.</summary>
    public int Highlight => _highlight;

    /// <summary>The scope chips of the open scope's provider (global search: All, Mail, Contacts, Calendar, Files); empty for a scope without chips.</summary>
    public IReadOnlyList<PaletteFacet> Facets => (ScopeProvider as IPaletteFacetProvider)?.Facets ?? [];

    /// <summary>The preview of the highlighted row (the open scope's provider shows one); null for none.</summary>
    public PalettePreview? Preview => _preview;

    /// <summary>Whether the open scope previews its rows (the view makes room for the preview pane).</summary>
    public bool HasPreviewPane => ScopeProvider is IPalettePreviewProvider;

    private IPaletteProvider? ScopeProvider => _providers.FirstOrDefault(p => string.Equals(p.Scope, _scope, StringComparison.Ordinal));

    /// <summary>Turns a scope chip on and shows only that kind; the highlight goes to the first row of what is left.</summary>
    public async Task SetFacetAsync(string key)
    {
        if (ScopeProvider is IPaletteFacetProvider facets)
        {
            facets.SetFacet(key);
            await RebuildAsync().ConfigureAwait(true);
        }
    }

    /// <summary>The next (or previous) scope chip (Ctrl+Right, Ctrl+Left).</summary>
    public async Task CycleFacetAsync(int delta)
    {
        if (ScopeProvider is IPaletteFacetProvider facets)
        {
            await SetFacetAsync(facets.NextFacetKey(delta)).ConfigureAwait(true);
        }
    }

    /// <summary>Opens the palette (Ctrl+K, k) with the context of now.</summary>
    public async Task OpenAsync(string? scope = null)
    {
        _context = _source.Current();
        _text = string.Empty;
        _scope = scope ?? CommandsScope;
        IsOpen = true;
        await RebuildAsync().ConfigureAwait(true);
    }

    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        _rows = [];
        _highlight = -1;
        _preview = null;
        OnPropertyChanged(nameof(Rows));
    }

    /// <summary>The text changed: the rows follow.</summary>
    public async Task SetTextAsync(string text)
    {
        _text = text ?? string.Empty;
        OnPropertyChanged(nameof(Text));
        await RebuildAsync().ConfigureAwait(true);
    }

    /// <summary>Tab: the next scope.</summary>
    public async Task NextScopeAsync()
    {
        var scopes = Scopes;
        _scope = scopes[(scopes.ToList().IndexOf(_scope) + 1) % scopes.Count];
        await RebuildAsync().ConfigureAwait(true);
    }

    /// <summary>Moves the highlight over the command rows (wrapping), skipping headings.</summary>
    public void Move(int delta)
    {
        var n = _rows.Count;
        if (n == 0)
        {
            return;
        }

        var at = _highlight;
        for (var i = 0; i < n; i++)
        {
            at = ((at + delta) % n + n) % n;
            if (!_rows[at].IsHeader)
            {
                _highlight = at;
                OnPropertyChanged(nameof(Highlight));
                UpdatePreview();
                return;
            }
        }
    }

    /// <summary>Runs the n-th command row (headings do not count) and closes the palette; false when it is disabled or absent.</summary>
    public async Task<bool> PickAsync(int commandIndex)
    {
        var row = _rows.Where(r => !r.IsHeader).ElementAtOrDefault(commandIndex);
        return row is not null && await RunAsync(row).ConfigureAwait(true);
    }

    /// <summary>Runs the highlighted row.</summary>
    public async Task<bool> EnterAsync() =>
        _highlight >= 0 && _highlight < _rows.Count && !_rows[_highlight].IsHeader && await RunAsync(_rows[_highlight]).ConfigureAwait(true);

    /// <inheritdoc />
    public bool HandleKey(KeyChord chord)
    {
        if (!IsOpen)
        {
            return false;
        }

        if (chord.Control && ScopeProvider is IPaletteFacetProvider && chord.Key.ToLowerInvariant() is "arrowright" or "right" or "arrowleft" or "left")
        {
            _ = CycleFacetAsync(chord.Key.EndsWith("ight", StringComparison.OrdinalIgnoreCase) ? 1 : -1);
            return true;
        }

        switch (chord.Key.ToLowerInvariant())
        {
            case "escape" or "esc":
                Close();
                return true;
            case "arrowdown" or "down":
                Move(1);
                return true;
            case "arrowup" or "up":
                Move(-1);
                return true;
            case "enter" or "return":
                _ = EnterAsync();
                return true;
            case "tab":
                _ = NextScopeAsync();
                return true;
            default:
                // typing goes to the text box
                return false;
        }
    }

    private async Task RebuildAsync()
    {
        var generation = ++_generation;
        IReadOnlyList<PaletteCommand> commands;
        if (string.Equals(_scope, CommandsScope, StringComparison.Ordinal))
        {
            commands = PaletteModel.Filter(PaletteModel.Commands(_context), _text);
        }
        else
        {
            var provider = _providers.FirstOrDefault(p => string.Equals(p.Scope, _scope, StringComparison.Ordinal));
            commands = provider is null ? [] : await provider.RowsAsync(_text, _context, CancellationToken.None).ConfigureAwait(true);
        }

        if (generation != _generation)
        {
            // a newer text arrived while a provider was answering: its answer wins
            return;
        }

        var rows = new List<PaletteRow>();
        var group = string.Empty;
        foreach (var c in commands)
        {
            if (!string.Equals(c.Group, group, StringComparison.Ordinal))
            {
                group = c.Group;
                rows.Add(new PaletteRow(group, null, true));
            }

            rows.Add(new PaletteRow(string.Empty, c, CanRun(c)));
        }

        _rows = rows;
        _highlight = rows.FindIndex(r => !r.IsHeader && r.Enabled);
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(Highlight));
        OnPropertyChanged(nameof(Scope));
        OnPropertyChanged(nameof(Facets));
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var row = _highlight >= 0 && _highlight < _rows.Count ? _rows[_highlight] : null;
        _preview = row?.Command is { } command && ScopeProvider is IPalettePreviewProvider provider ? provider.PreviewFor(command) : null;
        OnPropertyChanged(nameof(Preview));
    }

    private bool CanRun(PaletteCommand c)
    {
        var (id, argument) = PaletteCommandMap.Resolve(c, _context);
        return id is not null && _registry.TryGet(id, out var command) && command.CanExecute(Context(argument));
    }

    private async Task<bool> RunAsync(PaletteRow row)
    {
        var c = row.Command!;
        var (id, argument) = PaletteCommandMap.Resolve(c, _context);
        if (id is null || !row.Enabled || !_registry.TryGet(id, out var command))
        {
            return false;
        }

        Close();
        // a failed result and a thrown exception both end in an error toast (Fable review 3 R3-05)
        _ = await _runner.RunAsync(command, Context(argument), toastFailure: true).ConfigureAwait(true);

        return true;
    }

    private CommandContext Context(string? argument) => new(CommandSurface.Palette, _context.Targets, null, argument, _context.Targets.Count > 0 ? _context.Targets[0].AccountKey : null);
}

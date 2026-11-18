// Mirrors: see ISrvUiFiltersViewModel. Bulwark filter-settings.tsx (enable, reorder, delete; a failed save restores the list, B-SIEVE-023); Rust filters_x.rs save_rules :542; M6 plan D-M6-00 (the service completes off the UI thread and raises Changed from the pool: every state change is posted back before PropertyChanged).
// Owner srv-ui.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Filters.Rules;
using Barbican.Core.Settings.Folders;
using Barbican.Core.Sieve;
using Barbican.Presentation.Common;

namespace Barbican.Presentation.Settings.Server;

/// <summary>The default <see cref="ISrvUiFiltersViewModel"/>.</summary>
public sealed class SrvUiFiltersViewModel : SrvUiPageModel, ISrvUiFiltersViewModel
{
    private readonly IFilterService _service;
    private readonly ISrvUiFolderSource? _folders;
    private IReadOnlyList<SrvUiFilterRow> _rows = [];
    private FilterState? _state;
    private string? _saveError;

    public SrvUiFiltersViewModel(IFilterService service, IAccountManager? accounts = null, IDispatcher? dispatcher = null, ISrvUiFolderSource? folders = null)
        : base(accounts, dispatcher)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _folders = folders;
        _service.Changed += (_, e) =>
        {
            if (IsMine(e.AccountKey))
            {
                _ = ReloadQuietlyAsync();
            }
        };
    }

    public IReadOnlyList<SrvUiFilterRow> Rows => _rows;

    public bool Locked => _state?.Locked ?? false;

    public string? LoadError => Error;

    public string? SaveError
    {
        get => _saveError;
        private set => Set(ref _saveError, value);
    }

    public FilterState? State => _state;

    public IReadOnlyList<FilterFolder> Folders
    {
        get
        {
            var all = _folders?.Nodes() ?? [];
            return [.. all.Select(n => new FilterFolder(n.Id, PathOf(all, n)))];
        }
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        var result = await _service.LoadAsync(AccountKey, ct).ConfigureAwait(false);
        Apply(result);
        IsLoading = false;
    }

    public Task MoveAsync(string ruleId, int direction, CancellationToken ct) =>
        WriteAsync(rules => SrvUiFilterBridge.Moved(rules, ruleId, direction), ct);

    public Task SetEnabledAsync(string ruleId, bool enabled, CancellationToken ct) =>
        WriteAsync(
            rules =>
            {
                foreach (var r in rules.Where(r => string.Equals(IdOf(r), ruleId, StringComparison.Ordinal)))
                {
                    r["enabled"] = enabled;
                }

                return rules;
            },
            ct);

    public Task DeleteAsync(string ruleId, CancellationToken ct) =>
        WriteAsync(rules => [.. rules.Where(r => !string.Equals(IdOf(r), ruleId, StringComparison.Ordinal))], ct);

    public JsonNode? RuleOf(string ruleId) => _state?.Rules.FirstOrDefault(r => string.Equals(IdOf(r), ruleId, StringComparison.Ordinal))?.DeepClone();

    public Task<bool> UpsertAsync(JsonNode rule, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var id = IdOf(rule);
        return WriteAsync(
            rules =>
            {
                var at = rules.FindIndex(r => string.Equals(IdOf(r), id, StringComparison.Ordinal));
                if (at >= 0)
                {
                    rules[at] = rule.DeepClone();
                }
                else
                {
                    rules.Add(rule.DeepClone());
                }

                return rules;
            },
            ct);
    }

    public string SieveText()
    {
        if (_state is null)
        {
            return string.Empty;
        }

        try
        {
            return SieveScript.Generate(_state.Rules, _state.External);
        }
        catch (PortPendingException)
        {
            return _state.External;
        }
    }

    public Task<Result<FilterScan>> ScanAsync(JsonNode rule, long generation, CancellationToken ct) => _service.ScanAsync(AccountKey, rule, generation, ct);

    public Task<Result<FilterApplied>> ApplyAsync(FilterScan scan, CancellationToken ct) => _service.ApplyAsync(AccountKey, scan, ct);

    private static string IdOf(JsonNode rule) => SrvUiFilterBridge.Str(rule, "id");

    private static string PathOf(IReadOnlyList<FolderAdminNode> all, FolderAdminNode node)
    {
        var parts = new List<string> { node.Name };
        var at = node;
        while (at.ParentId is { } p && all.FirstOrDefault(n => string.Equals(n.Id, p, StringComparison.Ordinal)) is { } parent && parts.Count < 16)
        {
            parts.Insert(0, parent.Name);
            at = parent;
        }

        return string.Join('/', parts);
    }

    private async Task<bool> WriteAsync(Func<List<JsonNode>, IReadOnlyList<JsonNode>> change, CancellationToken ct)
    {
        if (_state is null || _state.Locked)
        {
            SaveError = "The script was edited by hand, so the rules are read-only here.";
            return false;
        }

        var next = change([.. _state.Rules.Select(r => r.DeepClone())]);
        SaveError = null;
        var result = await _service.SaveAsync(AccountKey, next, ct).ConfigureAwait(false);
        if (result.IsOk)
        {
            Apply(result);
            return true;
        }

        // a failed save restores the list the server has (B-SIEVE-023): the rows were never changed, the view redraws them
        SaveError = result.Error!.Message;
        Raise(nameof(Rows));
        return false;
    }

    private void Apply(Result<FilterState> result)
    {
        if (!result.IsOk)
        {
            Error = result.Error!.Message;
            return;
        }

        Error = null;
        _state = result.Value;
        _rows = [.. result.Value!.Rules.Select(r => new SrvUiFilterRow(IdOf(r), SrvUiFilterBridge.Str(r, "name"), SrvUiFilterBridge.Summary(r), r["enabled"]?.GetValue<bool>() ?? true))];
        Raise(nameof(Rows));
        Raise(nameof(Locked));
        Raise(nameof(State));
    }

    private async Task ReloadQuietlyAsync()
    {
        try
        {
            Apply(await _service.LoadAsync(AccountKey, CancellationToken.None).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // the page went away
        }
    }
}

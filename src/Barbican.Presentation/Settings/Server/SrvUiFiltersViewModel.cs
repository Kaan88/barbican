// Mirrors: see ISrvUiFiltersViewModel. Bulwark filter-settings.tsx (enable, reorder, delete; a failed save restores the list, B-SIEVE-023); Rust filters_x.rs save_rules :542; M6 plan D-M6-00 (the service completes off the UI thread and raises Changed from the pool: every state change is posted back before PropertyChanged).
// Owner srv-ui.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
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
    private string? _stateAccount;
    private string? _saveError;
    private IReadOnlyList<SrvUiFolderSnapshot> _folderSnapshot = [];
    private long _loadSequence;

    public SrvUiFiltersViewModel(IFilterService service, IAccountManager? accounts = null, IDispatcher? dispatcher = null, ISrvUiFolderSource? folders = null)
        : base(accounts, dispatcher)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _folders = folders;
        _service.Changed += (_, e) =>
        {
            _ = ReloadQuietlyAsync(e.AccountKey);
        };
    }

    public IReadOnlyList<SrvUiFilterRow> Rows => IsLoaded(CaptureAccount()) ? _rows : [];

    public bool Locked => State?.Locked ?? false;

    public string? LoadError => Error;

    public string? SaveError
    {
        get => _saveError;
        private set => Set(ref _saveError, value);
    }

    public FilterState? State => IsLoaded(CaptureAccount()) ? _state : null;

    public string? StateAccountKey => IsLoaded(CaptureAccount()) ? _stateAccount : null;

    public IReadOnlyList<FilterFolder> Folders
    {
        get
        {
            var all = IsLoaded(CaptureAccount()) ? _folderSnapshot.Select(x => x.Node).ToArray() : [];
            return [.. all.Select(n => new FilterFolder(n.Id, PathOf(all, n)))];
        }
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        var stamp = CaptureAccount();
        var sequence = Interlocked.Increment(ref _loadSequence);
        var result = await _service.LoadAsync(stamp.Key, ct).ConfigureAwait(false);
        var folders = _folders is null
            ? Result<IReadOnlyList<SrvUiFolderSnapshot>>.Ok([])
            : await _folders.LoadAsync(stamp.Key, ct).ConfigureAwait(false);
        await OnUiAsync(stamp, () =>
        {
            if (sequence != Interlocked.Read(ref _loadSequence)) return;
            if (result.IsOk && folders.IsOk)
            {
                _folderSnapshot = folders.Value!;
                MarkLoaded(stamp);
                Apply(result, stamp.Key);
            }
            else
            {
                Error = result.Error?.Message ?? folders.Error?.Message;
            }

            IsLoading = false;
        }).ConfigureAwait(false);
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

    public JsonNode? RuleOf(string ruleId) => State?.Rules.FirstOrDefault(r => string.Equals(IdOf(r), ruleId, StringComparison.Ordinal))?.DeepClone();

    public Task<bool> UpsertAsync(JsonNode rule, string expectedAccount, string? expectedState, long expectedGeneration, CancellationToken ct)
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
            ct, expectedAccount, expectedState, expectedGeneration);
    }

    public string SieveText()
    {
        var state = State;
        if (state is null)
        {
            return string.Empty;
        }

        try
        {
            return SieveScript.Generate(state.Rules, state.External);
        }
        catch (PortPendingException)
        {
            return state.External;
        }
    }

    public async Task<Result<FilterScan>> ScanAsync(JsonNode rule, long run, string account, long accountGeneration, CancellationToken ct)
    {
        if (!CanUse(account, accountGeneration)) return Result<FilterScan>.Fail(new ValidationError("Reload filters for this account."));
        var stamp = new AccountStamp(account, accountGeneration);
        var result = await _service.ScanAsync(account, rule, run, ct).ConfigureAwait(false);
        var valid = false;
        await OnUiAsync(stamp, () => valid = IsLoaded(stamp) && !IsLoading).ConfigureAwait(false);
        return valid ? result : Result<FilterScan>.Fail(new ValidationError("The account changed; scan again."));
    }

    public Task<Result<FilterApplied>> ApplyAsync(FilterScan scan, string account, long accountGeneration, CancellationToken ct) =>
        CanUse(account, accountGeneration)
            ? _service.ApplyAsync(account, scan, ct)
            : Task.FromResult(Result<FilterApplied>.Fail(new ValidationError("The account changed; scan again.")));

    private bool CanUse(string account, long generation) =>
        string.Equals(account, AccountKey, StringComparison.Ordinal) && IsCurrentGeneration(generation) && !IsLoading;

    protected override void OnAccountSwitched()
    {
        _ = Interlocked.Increment(ref _loadSequence);
        _state = null;
        _stateAccount = null;
        _rows = [];
        _folderSnapshot = [];
        SaveError = null;
        Raise(nameof(Rows));
        Raise(nameof(State));
        Raise(nameof(StateAccountKey));
        Raise(nameof(Locked));
        ReloadAfterSwitch(() => LoadAsync(CancellationToken.None));
    }

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

    private async Task<bool> WriteAsync(Func<List<JsonNode>, IReadOnlyList<JsonNode>> change, CancellationToken ct, string? expectedAccount = null, string? expectedState = null, long? expectedGeneration = null)
    {
        if (!RequireLoaded(out var stamp) || _state is null || !string.Equals(_stateAccount, stamp.Key, StringComparison.Ordinal))
        {
            SaveError = "Reload filters for this account before saving.";
            return false;
        }

        if (expectedAccount is not null && (!string.Equals(expectedAccount, stamp.Key, StringComparison.Ordinal) || expectedGeneration != stamp.Generation
            || string.IsNullOrEmpty(expectedState) || !string.Equals(expectedState, _state.State, StringComparison.Ordinal)))
        {
            SaveError = "Filters changed while this editor was open; reload and edit again.";
            return false;
        }

        if (_state.Locked)
        {
            SaveError = "The script was edited by hand, so the rules are read-only here.";
            return false;
        }

        var next = change([.. _state.Rules.Select(r => r.DeepClone())]);
        SaveError = null;
        var account = stamp.Key;
        var result = await _service.SaveAsync(account, next, _state.State, ct).ConfigureAwait(false);
        var applied = false;
        await OnUiAsync(stamp, () =>
        {
            if (result.IsOk)
            {
                Apply(result, account);
                applied = true;
            }
            else
            {
                SaveError = result.Error!.Message;
                Raise(nameof(Rows));
            }
        }).ConfigureAwait(false);
        return applied;
    }

    private void Apply(Result<FilterState> result, string account)
    {
        if (!result.IsOk)
        {
            Error = result.Error!.Message;
            return;
        }

        Error = null;
        _state = result.Value;
        _stateAccount = account;
        _rows = [.. result.Value!.Rules.Select(r => new SrvUiFilterRow(IdOf(r), SrvUiFilterBridge.Str(r, "name"), SrvUiFilterBridge.Summary(r), r["enabled"]?.GetValue<bool>() ?? true))];
        Raise(nameof(Rows));
        Raise(nameof(Locked));
        Raise(nameof(State));
        Raise(nameof(StateAccountKey));
    }

    private async Task ReloadQuietlyAsync(string account)
    {
        try
        {
            var stamp = CaptureAccount();
            if (!string.Equals(account, stamp.Key, StringComparison.Ordinal)) return;
            var result = await _service.LoadAsync(account, CancellationToken.None).ConfigureAwait(false);
            await OnUiAsync(stamp, () => { if (IsLoaded(stamp) && !IsLoading) Apply(result, account); }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // the page went away
        }
    }
}

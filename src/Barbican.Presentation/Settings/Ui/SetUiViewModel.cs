// Mirrors: Bulwark components/settings/settings-app.tsx (page list, search, Show advanced settings; I-395..I-398), settings-section.tsx. M6 plan section 4.10, decisions D-M6-B01, D-M6-B02, D-M6-B04.
// THREADING (D-M6-00): ISettingValues.Changed arrives on a pool thread; it is posted to the dispatcher before PropertyChanged is raised.

using System.ComponentModel;
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Settings.Pages;
using Barbican.Presentation.Common;

namespace Barbican.Presentation.Settings.Ui;

/// <summary>The default <see cref="ISetUiViewModel"/>.</summary>
public sealed class SetUiViewModel : ISetUiViewModel, IDisposable
{
    /// <summary>The account key of a sample-data run.</summary>
    public const string SampleKey = "sample";

    private readonly ISettingValues _values;
    private readonly ISettingInfo _info;
    private readonly IAccountManager? _accounts;
    private readonly IDispatcher? _dispatcher;
    private readonly IReadOnlyList<SettingPage> _pages;
    private readonly Dictionary<string, string> _infoText = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _compoundGate = new(1, 1);
    private string _search = string.Empty;
    private bool _advanced;
    private string _lastError = string.Empty;
    private IReadOnlyList<SettingSearchHit> _hits = [];

    public SetUiViewModel(ISettingValues values, ISettingInfo info, ISettingsNavigation navigation, IAccountManager? accounts = null, IDispatcher? dispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(navigation);
        _values = values;
        _info = info;
        _accounts = accounts;
        _dispatcher = dispatcher;
        _pages = SetUiCatalog.Load();
        _values.Changed += (_, _) => OnUi(() => Raise("Values"));
        if (_accounts is not null)
        {
            _accounts.ActiveChanged += (_, _) => OnUi(() => Raise(nameof(AccountKey)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Dispose() => _compoundGate.Dispose();

    public string SearchText
    {
        get => _search;
        set
        {
            value ??= string.Empty;
            if (string.Equals(value, _search, StringComparison.Ordinal))
            {
                return;
            }

            _search = value;
            _hits = string.IsNullOrWhiteSpace(value) ? [] : SetUiCatalog.Search(_pages, value);
            Raise(nameof(SearchText));
            Raise(nameof(SearchHits));
        }
    }

    public bool ShowAdvanced
    {
        get => _advanced;
        set
        {
            if (value == _advanced)
            {
                return;
            }

            _advanced = value;
            Raise(nameof(ShowAdvanced));
            Raise(nameof(VisiblePages));
            Raise("Values");
        }
    }

    public IReadOnlyList<SettingsPageInfo> VisiblePages => [.. SettingsNav.Pages.Where(p => p.Owner != SettingsPageOwner.Later && (_advanced || !p.Advanced))];

    public IReadOnlyList<SettingSearchHit> SearchHits => _hits;

    public IReadOnlyList<SettingPage> CatalogPages => _pages;

    public string AccountKey => _accounts?.Active?.Key ?? SampleKey;

    public string LastError => _lastError;

    public IReadOnlyList<SetUiRow> RowsOf(string pageId)
    {
        var page = _pages.FirstOrDefault(p => string.Equals(p.Id, pageId, StringComparison.Ordinal));
        if (page is null)
        {
            return [];
        }

        var visible = SetUiRowRules.Visible(page.Rows, _advanced).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var rows = new List<SetUiRow>(page.Rows.Count);
        var mountainView = SetUiRowRules.IsMountainView(Stored("interfaceLayout"));
        foreach (var row in page.Rows)
        {
            var stored = SetUiRowRules.KeyOf(row) is { } k ? Stored(k) : null;
            _ = _infoText.TryGetValue(row.Id, out var text);
            var value = string.Equals(row.Key, "tintListRowsByAccount", StringComparison.Ordinal)
                ? JsonValue.Create(Marker())
                : string.Equals(row.Key, "messageListOrderScope", StringComparison.Ordinal)
                    ? JsonValue.Create(stored is JsonValue scope && scope.TryGetValue<string>(out var textScope) && string.Equals(textScope, "all", StringComparison.Ordinal))
                    : SetUiRowRules.FileField(row.Id) is not null ? SetUiRowRules.FileValue(row.Id, stored) : SetUiRowRules.EffectiveValue(row, stored);
            rows.Add(new SetUiRow(row, value, text ?? string.Empty, visible.Contains(row.Id) && !SetUiRowRules.HiddenByInterfaceLayout(row, mountainView)));
        }

        return rows;
    }

    public JsonNode? ValueOf(string key) => Stored(key);

    public async Task SetValueAsync(string rowId, JsonNode? value, CancellationToken ct)
    {
        var row = _pages.SelectMany(p => p.Rows).FirstOrDefault(r => string.Equals(r.Id, rowId, StringComparison.Ordinal));
        if (row is null || SetUiRowRules.KeyOf(row) is not { } key)
        {
            return;
        }

        if (string.Equals(row.Key, "messageListOrderScope", StringComparison.Ordinal))
        {
            if (value is JsonValue scope && scope.TryGetValue<bool>(out var all))
            {
                await SetKeyAsync(key, JsonValue.Create(all ? "all" : "inbox"), ct).ConfigureAwait(true);
            }

            return;
        }

        if (string.Equals(row.Key, "tintListRowsByAccount", StringComparison.Ordinal))
        {
            await SetMarkerAsync(value, ct).ConfigureAwait(true);
            return;
        }

        if (SetUiRowRules.FileField(rowId) is not { } field)
        {
            if (row.Key is null)
            {
                var keylessCheck = SettingRules.Coerce(row, value);
                if (!keylessCheck.Ok)
                {
                    _lastError = keylessCheck.Error ?? "That value is not allowed.";
                    Raise(nameof(LastError));
                    Raise("Values");
                    return;
                }

                value = keylessCheck.Value;
            }

            await SetKeyAsync(key, value, ct).ConfigureAwait(true);
            return;
        }

        await SetFileFieldAsync(row, key, field, value, ct).ConfigureAwait(true);
    }

    private async Task SetFileFieldAsync(SettingRow row, string key, string field, JsonNode? value, CancellationToken ct)
    {
        var check = SettingRules.Coerce(row, value);
        if (!check.Ok)
        {
            _lastError = check.Error ?? "That value is not allowed.";
            Raise(nameof(LastError));
            Raise("Values");
            return;
        }

        await _compoundGate.WaitAsync(ct).ConfigureAwait(true);
        try
        {
            var files = Stored(key) as JsonObject ?? new JsonObject();
            files[field] = check.Value?.DeepClone();
            await SetKeyAsync(key, files, ct).ConfigureAwait(true);
        }
        finally
        {
            _ = _compoundGate.Release();
        }
    }

    private string Marker()
    {
        var tint = Stored("tintListRowsByAccount") is JsonValue t && t.TryGetValue<bool>(out var on) && on;
        var dot = Stored("showAccountDot") is not JsonValue d || !d.TryGetValue<bool>(out var visible) || visible;
        return tint ? "tint" : dot ? "dot" : "none";
    }

    private async Task SetMarkerAsync(JsonNode? value, CancellationToken ct)
    {
        var mode = value is JsonValue v && v.TryGetValue<string>(out var text) ? text : string.Empty;
        if (mode is not ("dot" or "tint" or "none"))
        {
            _lastError = "Choose an account marker.";
            Raise(nameof(LastError));
            Raise("Values");
            return;
        }

        await _compoundGate.WaitAsync(ct).ConfigureAwait(true);
        try
        {
            await SetKeyAsync("tintListRowsByAccount", JsonValue.Create(string.Equals(mode, "tint", StringComparison.Ordinal)), ct).ConfigureAwait(true);
            if (_lastError.Length == 0)
            {
                await SetKeyAsync("showAccountDot", JsonValue.Create(string.Equals(mode, "dot", StringComparison.Ordinal)), ct).ConfigureAwait(true);
            }
        }
        finally { _ = _compoundGate.Release(); }
    }

    public async Task SetCalendarHourAsync(bool start, int hour, CancellationToken ct)
    {
        if ((start && hour is < 0 or > 23) || (!start && hour is < 1 or > 24))
        {
            _lastError = "Enter a valid hour.";
            Raise(nameof(LastError));
            Raise("Values");
            return;
        }

        await _compoundGate.WaitAsync(ct).ConfigureAwait(true);
        try
        {
            _lastError = string.Empty;
            int ReadHour(string key, int fallback) => int.TryParse(Stored(key)?.ToJsonString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : fallback;
            var from = Math.Clamp(ReadHour("calendarDayStartHour", 8), 0, 23);
            var to = Math.Clamp(ReadHour("calendarDayEndHour", 20), 1, 24);
            if (start && hour >= to) { await SetKeyAsync("calendarDayEndHour", JsonValue.Create(hour + 1), ct).ConfigureAwait(true); }
            if (!start && hour <= from) { await SetKeyAsync("calendarDayStartHour", JsonValue.Create(hour - 1), ct).ConfigureAwait(true); }
            if (_lastError.Length == 0) { await SetKeyAsync(start ? "calendarDayStartHour" : "calendarDayEndHour", JsonValue.Create(hour), ct).ConfigureAwait(true); }
        }
        finally { _ = _compoundGate.Release(); }
    }

    public async Task SetWorkingDayAsync(int day, bool enabled, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(day);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(day, 6);
        await _compoundGate.WaitAsync(ct).ConfigureAwait(true);
        try
        {
            var existing = Stored("calendarWorkingDays") as JsonArray;
            var days = existing is null
                ? new HashSet<int>([1, 2, 3, 4, 5])
                : existing.OfType<JsonValue>().Select(v => v.TryGetValue<int>(out var d) ? d : -1).Where(d => d is >= 0 and <= 6).ToHashSet();
            if (!enabled && days.Contains(day) && days.Count == 1)
            {
                _lastError = "Choose at least one working day.";
                Raise(nameof(LastError));
                Raise("Values");
                return;
            }

            if (enabled) { _ = days.Add(day); } else { _ = days.Remove(day); }
            var next = new JsonArray([.. days.Order().Select(d => (JsonNode?)JsonValue.Create(d))]);
            await SetKeyAsync("calendarWorkingDays", next, ct).ConfigureAwait(true);
        }
        finally
        {
            _ = _compoundGate.Release();
        }
    }

    public async Task SetKeyAsync(string key, JsonNode? value, CancellationToken ct)
    {
        try
        {
            var result = await _values.SetAsync(key, value, ct).ConfigureAwait(true);
            _lastError = result.IsOk ? string.Empty : result.Error?.Message ?? "The setting was not saved.";
        }
        catch (PortPendingException)
        {
            _lastError = "Saving settings is not available yet.";
        }

        Raise(nameof(LastError));
        Raise("Values");
    }

    public async Task RefreshInfoAsync(string pageId, CancellationToken ct)
    {
        var page = _pages.FirstOrDefault(p => string.Equals(p.Id, pageId, StringComparison.Ordinal));
        if (page is null)
        {
            return;
        }

        foreach (var row in page.Rows.Where(r => r.Kind == SettingRowKind.Info && r.Key is null))
        {
            try
            {
                if (!_info.Provides(row.Id))
                {
                    continue;
                }

                var text = await _info.TextAsync(row.Id, ct).ConfigureAwait(true);
                _infoText[row.Id] = text;
            }
            catch (PortPendingException)
            {
                // set-data has not landed the info provider: the row stays empty
            }
        }

        Raise("Info");
    }

    private JsonNode? Stored(string key)
    {
        try
        {
            return _values.Stored(key);
        }
        catch (PortPendingException)
        {
            return null;
        }
    }

    private void OnUi(Action action)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcher.Post(action);
        }
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

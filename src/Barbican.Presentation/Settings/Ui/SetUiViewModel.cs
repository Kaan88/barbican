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
public sealed class SetUiViewModel : ISetUiViewModel
{
    /// <summary>The account key of a sample-data run.</summary>
    public const string SampleKey = "sample";

    private readonly ISettingValues _values;
    private readonly ISettingInfo _info;
    private readonly IAccountManager? _accounts;
    private readonly IDispatcher? _dispatcher;
    private readonly IReadOnlyList<SettingPage> _pages;
    private readonly Dictionary<string, string> _infoText = new(StringComparer.Ordinal);
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
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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
        foreach (var row in page.Rows)
        {
            var stored = SetUiRowRules.KeyOf(row) is { } k ? Stored(k) : null;
            _ = _infoText.TryGetValue(row.Id, out var text);
            rows.Add(new SetUiRow(row, SetUiRowRules.EffectiveValue(row, stored), text ?? string.Empty, visible.Contains(row.Id)));
        }

        return rows;
    }

    public JsonNode? ValueOf(string key) => Stored(key);

    public Task SetValueAsync(string rowId, JsonNode? value, CancellationToken ct)
    {
        var row = _pages.SelectMany(p => p.Rows).FirstOrDefault(r => string.Equals(r.Id, rowId, StringComparison.Ordinal));
        return row is null || SetUiRowRules.KeyOf(row) is not { } key ? Task.CompletedTask : SetKeyAsync(key, value, ct);
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

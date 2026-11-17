// Mirrors: Bulwark components/layout/sidebar.tsx:2133-2566 (the shared accounts section, shown when the login reaches shared accounts and "include in unified" reads them),
// Rust unified.rs:251 sources (includeGroupInUnified), RFC 8620 section 2 (Session accounts: name, isPersonal, accountCapabilities). spec I-054. Owner unified (m2-close-a).
// Shared accounts are read into All inboxes; this section lists them. Opening one account's own folders, right-click, drag reorder and the tags gear are not built (I-054 stays partial).

using System.Collections.ObjectModel;
using System.Text.Json;
using Barbican.Core.Mail.Unified;
using Barbican.Core.Storage;
using Barbican.Presentation.Common;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Unified;

/// <summary>The shared accounts section of the sidebar.</summary>
public sealed partial class SharedSectionViewModel : ObservableObject, IDisposable
{
    private readonly UnifiedInbox _unified;
    private readonly IPrefsStore _prefs;
    private readonly IDispatcher _dispatcher;

    [ObservableProperty]
    private bool _isVisible;

    public SharedSectionViewModel(UnifiedInbox unified, IPrefsStore prefs, IDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(unified);
        ArgumentNullException.ThrowIfNull(prefs);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _unified = unified;
        _prefs = prefs;
        _dispatcher = dispatcher;
        _prefs.Changed += OnChanged;
        _unified.Refreshed += OnChanged;
        Update();
    }

    /// <summary>The section's title.</summary>
    public static string Title => "Shared";

    /// <summary>The shared accounts, in the order the unified inbox reads them.</summary>
    public ObservableCollection<SharedAccountRow> Rows { get; } = [];

    /// <summary>Reads the sources again; rows that stay are left alone.</summary>
    public void Update()
    {
        var settings = _unified.Settings;
        var rows = settings.Enabled && settings.IncludeGroup
            ? _unified.Sources().Where(s => s.Shared).Select(s => new SharedAccountRow(s.Login, s.AccountId, NameOf(s))).ToList()
            : [];
        Sync(rows);
        IsVisible = Rows.Count > 0;
    }

    public void Dispose()
    {
        _prefs.Changed -= OnChanged;
        _unified.Refreshed -= OnChanged;
    }

    /// <summary>The name the Session gives the account (RFC 8620 section 2), else its id.</summary>
    public static string NameOf(UnifiedSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var raw = source.Client.Session.Raw;
        return raw.ValueKind == JsonValueKind.Object
            && raw.TryGetProperty("accounts", out var accounts) && accounts.ValueKind == JsonValueKind.Object
            && accounts.TryGetProperty(source.AccountId, out var account) && account.ValueKind == JsonValueKind.Object
            && account.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString())
            ? name.GetString()!
            : source.AccountId;
    }

    private void Sync(List<SharedAccountRow> rows)
    {
        if (Rows.SequenceEqual(rows))
        {
            return;
        }

        // in place: a row that stays keeps its place, so the section does not blink on a refresh
        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            if (!rows.Contains(Rows[i]))
            {
                Rows.RemoveAt(i);
            }
        }

        for (var i = 0; i < rows.Count; i++)
        {
            if (i >= Rows.Count)
            {
                Rows.Add(rows[i]);
            }
            else if (!Equals(Rows[i], rows[i]))
            {
                Rows.Insert(i, rows[i]);
            }
        }
    }

    private void OnChanged(object? sender, EventArgs e) => _dispatcher.Post(Update);
}

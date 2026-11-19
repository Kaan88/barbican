// Mirrors: Bulwark components/settings/calendar-settings.tsx (participant identities and default organizer).
using System.ComponentModel;
using Barbican.App.Shell;
using Barbican.Core.Calendar;
using Barbican.Presentation.Settings.Ui;

namespace Barbican.App.Views.Settings;

/// <summary>Selects the active account's default calendar participant identity.</summary>
internal sealed class SetUiCalendarIdentityEditor : StackPanel, IDisposable
{
    private readonly ISetUiViewModel _screen;
    private readonly ICalendarParticipantIdentities? _service;
    private long _version;
    private bool _disposed;

    public SetUiCalendarIdentityEditor(ISetUiViewModel screen)
    {
        _screen = screen;
        _service = ViewHost.Find<ICalendarParticipantIdentities>();
        _screen.PropertyChanged += OnChanged;
        Loaded += (_, _) => SetUiUi.Run(LoadAsync);
        Children.Add(SetUiUi.Meta("Loading organizer identities..."));
    }

    void IDisposable.Dispose() { _disposed = true; _version++; _screen.PropertyChanged -= OnChanged; }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(ISetUiViewModel.AccountKey), StringComparison.Ordinal))
        {
            _version++;
            Children.Clear();
            Children.Add(SetUiUi.Meta("Loading organizer identities..."));
            SetUiUi.Run(LoadAsync);
        }
    }

    private async Task LoadAsync()
    {
        var account = _screen.AccountKey;
        var version = ++_version;
        if (_service is null)
        {
            Children.Clear(); Children.Add(SetUiUi.Meta("Organizer identities are unavailable.")); return;
        }

        var result = await _service.ListAsync(account, CancellationToken.None).ConfigureAwait(true);
        if (!Current(account, version)) { return; }
        Children.Clear();
        if (!result.IsOk)
        {
            Children.Add(SetUiUi.Meta(result.Error?.Message ?? "Could not load organizer identities."));
            return;
        }

        var items = result.Value ?? [];
        if (items.Count == 0)
        {
            Children.Add(SetUiUi.Meta("No organizer identities are available."));
            return;
        }

        var selected = items.ToList().FindIndex(i => i.IsDefault);
        var choice = new SetUiChoice(items.Select(Label), Math.Max(0, selected), i => SetUiUi.Run(() => SelectAsync(account, version, items[i].Id)), "set.calendar.organizer");
        Children.Add(choice);
    }

    private async Task SelectAsync(string account, long version, string id)
    {
        if (_service is null || !Current(account, version)) { return; }
        var result = await _service.SetDefaultAsync(account, id, CancellationToken.None).ConfigureAwait(true);
        if (!Current(account, version)) { return; }
        if (!result.IsOk)
        {
            var error = result.Error?.Message ?? "Could not change organizer identity.";
            await LoadAsync().ConfigureAwait(true);
            if (!_disposed && string.Equals(account, _screen.AccountKey, StringComparison.Ordinal)) { Children.Add(SetUiUi.Meta(error)); }
            return;
        }

        await LoadAsync().ConfigureAwait(true);
    }

    private bool Current(string account, long version) => !_disposed && _version == version && string.Equals(account, _screen.AccountKey, StringComparison.Ordinal);

    private static string Label(CalendarParticipantIdentity item)
    {
        var address = item.CalendarAddress.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? item.CalendarAddress[7..] : item.CalendarAddress;
        return string.IsNullOrWhiteSpace(item.Name) || string.Equals(item.Name, address, StringComparison.OrdinalIgnoreCase) ? address : item.Name + " <" + address + ">";
    }
}

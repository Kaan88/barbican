// Mirrors: Bulwark components/calendar/create-calendar-modal.tsx (name and colour), Rust settings name field; I-309; B-CAL-057 (the settings page is M6). The "Add calendar" card of the calendar sidebar.

using Barbican.Core.Calendar.Editing;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Calendar.Editors.Dialogs;

/// <summary>Create a calendar: a name and a colour.</summary>
public sealed class CreateCalendarViewModel(ICalendarSource source, ICalendarAdmin admin) : ObservableObject, ICloseableCard
{
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _name = string.Empty;
    private string _color = CalendarJson.Colors[0].Hex;
    private string _error = string.Empty;

#pragma warning disable VSTHRD003 // completed by the card itself
    public Task Closed => _closed.Task;
#pragma warning restore VSTHRD003

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? string.Empty);
    }

    public string Color
    {
        get => _color;
        set => SetProperty(ref _color, value ?? string.Empty);
    }

    public string Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public bool CanCreate => _name.Trim().Length > 0;

    public async Task<bool> CreateAsync(CancellationToken ct)
    {
        if (!CanCreate || source.AccountKey is not { } account)
        {
            Error = "Give the calendar a name.";
            return false;
        }

        var result = await admin.CreateCalendarAsync(account, _name.Trim(), _color, ct).ConfigureAwait(true);
        if (!result.IsOk)
        {
            Error = result.Error!.Message;
            return false;
        }

        Close();
        return true;
    }

    public void Close() => _closed.TrySetResult();
}

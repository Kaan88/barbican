// Mirrors: M4 cal-ui dialog view models (Error, Status, Close). Owner con-ui: the base of the contacts dialog models (import, merge, print, group, directory, form).

#pragma warning disable MA0048 // the event arguments sit beside the one class that raises them
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Contacts;

/// <summary>What every contacts dialog model has: an error line, a status line, and a way to end the dialog.</summary>
public abstract class ContactsDialogModel : ObservableObject
{
    private string _error = string.Empty;
    private string _status = string.Empty;
    private bool _busy;

    /// <summary>The dialog ends; the argument is what the dialog answers (true: something was done).</summary>
    public event EventHandler<ContactsDialogFinishedEventArgs>? Finished;

    public string Error
    {
        get => _error;
        protected set => SetProperty(ref _error, value);
    }

    public string Status
    {
        get => _status;
        protected set => SetProperty(ref _status, value);
    }

    /// <summary>A write is running (the buttons wait).</summary>
    public bool Busy
    {
        get => _busy;
        protected set => SetProperty(ref _busy, value);
    }

    public void Close(bool done = false) => Finished?.Invoke(this, new ContactsDialogFinishedEventArgs(done));

    /// <summary>The words of a failed result for the error line.</summary>
    protected static string Words(Barbican.Core.Jmap.Errors.JmapError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Message;
    }
}

/// <summary>How a contacts dialog ended: <see cref="Done"/> is true when something was written.</summary>
public sealed class ContactsDialogFinishedEventArgs(bool done) : EventArgs
{
    public bool Done { get; } = done;
}

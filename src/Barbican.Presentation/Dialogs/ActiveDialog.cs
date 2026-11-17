// Mirrors: PORT-PLAN section 3.7 (Scrim + PopIn, focus trapped and restored); M2 plan section 6.2 (dialog host). Owner menus.

using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Dialogs;

/// <summary>The dialog on screen: its request, the text of a prompt, and how it ends.</summary>
public sealed class ActiveDialog : ObservableObject
{
    private readonly Action<object?> _complete;
    private string _text;

    internal ActiveDialog(object request, string title, string initialText, Action<object?> complete)
    {
        Request = request;
        Title = title;
        _text = initialText;
        _complete = complete;
    }

    /// <summary>The request record (a <see cref="PromptDialogRequest"/>, a <see cref="ConfirmDialogRequest"/> or an owner's own).</summary>
    public object Request { get; }

    public string Title { get; }

    /// <summary>The prompt's text as typed.</summary>
    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    /// <summary>Ends the dialog with an answer (null cancels).</summary>
    public void Complete(object? result) => _complete(result);
}

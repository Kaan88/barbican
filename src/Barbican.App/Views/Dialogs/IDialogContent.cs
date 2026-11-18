// Mirrors: M2 plan section 4.2 ("content records per dialog live with their owner"); PORT-PLAN section 3.7. Owner menus.

using Barbican.Presentation.Dialogs;

namespace Barbican.App.Views.Dialogs;

/// <summary>Draws an owner's own dialog (the Move-to picker, the tag picker) inside the dialog host's card; register one per request type.</summary>
public interface IDialogContent
{
    /// <summary>This factory draws that request.</summary>
    bool CanDraw(object request);

    /// <summary>The content of the card; call <see cref="ActiveDialog.Complete"/> to answer.</summary>
    UIElement Create(ActiveDialog dialog);

    /// <summary>M5 (additive, con-ui): the content has its own buttons and answers through <see cref="ActiveDialog.Complete"/>; the host hides its OK and Cancel (Esc still cancels).</summary>
    bool OwnButtons => false;

    /// <summary>M5 (additive, con-ui): the widest the card may be (a form or a picker needs more than the 440 of a question).</summary>
    double MaxWidth => 440;
}

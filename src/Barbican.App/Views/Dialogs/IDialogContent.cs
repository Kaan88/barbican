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
}

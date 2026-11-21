// Mirrors: Bulwark components/contacts/contact-merge-dialog.tsx (one set of duplicates at a time: the cards, what is kept, Merge), Rust pim-contacts.slint ContactDupes; I-362, B-CON-017..019, L-032. Draws ContactsMergeViewModel (its own Close and Merge).
// Dialog shell: ui/dialog.tsx:131-134,181 through Views/Dialogs/DialogStyles; footer: the way back at the start (me-auto), Cancel, then Merge, focus on the action: contact-merge-dialog.tsx:185-197.

using System.Globalization;
using Barbican.Presentation.Compose;
using DialogStyles = Barbican.App.Views.Dialogs.DialogStyles;
using Barbican.Presentation.Contacts;
using Barbican.Presentation.Dialogs;
using Microsoft.UI.Xaml.Automation;
using static Barbican.App.Views.Calendar.Editors.CalendarCardKit;

namespace Barbican.App.Views.Contacts;

/// <summary>Draws <see cref="ContactsMergeRequest"/>.</summary>
public sealed class ContactsMergeDialogContent : Barbican.App.Views.Dialogs.IDialogContent
{
    public bool OwnButtons => true;

    public Control? InitialFocus(UIElement content) => DialogStyles.TaggedFocus(content);

    public double MaxWidth => 520;

    public bool CanDraw(object request) => request is ContactsMergeRequest;

    public UIElement Create(ActiveDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var m = ((ContactsMergeRequest)dialog.Request).Model;
        m.Finished += (_, e) => dialog.Complete(e.Done);
        var panel = Card("contacts.merge-dialog", "Duplicate contacts", 420);
        panel.Children.RemoveAt(0); // the dialog host shows the title
        var position = Body(string.Empty);
        var cards = new StackPanel { Spacing = 4 };
        var note = Body("The first card is kept (the most recently changed). What the others have and it lacks is added to it, then the others are deleted.");
        var error = ErrorLine(m.Error);
        var status = Body(m.Status);
        var previous = DialogStyles.FooterButton("Previous", DialogTone.Plain, "contacts.merge-dialog.prev", () => m.Index--);
        var next = DialogStyles.FooterButton("Next", DialogTone.Plain, "contacts.merge-dialog.next", () => m.Index++);
        var merge = DialogStyles.FooterButton("Merge these", DialogTone.Primary, "contacts.merge-dialog.ok", () => ContactsUi.Run(async () => _ = await m.MergeAsync(CancellationToken.None).ConfigureAwait(true)));

        void Refresh()
        {
            position.Text = m.Sets.Count == 0 ? "No duplicates left." : "Set " + (m.Index + 1).ToString(CultureInfo.InvariantCulture) + " of " + m.Sets.Count.ToString(CultureInfo.InvariantCulture);
            cards.Children.Clear();
            foreach (var label in m.Labels)
            {
                cards.Children.Add(new TextBlock { Text = label, FontSize = 13.5, Foreground = ContactsUi.Palette("Text"), TextTrimming = TextTrimming.CharacterEllipsis });
            }

            SetError(error, m.Error);
            status.Text = m.Status;
            previous.IsEnabled = m.Index > 0;
            next.IsEnabled = m.Index < m.Sets.Count - 1;
            merge.IsEnabled = m.Sets.Count > 0 && !m.Busy;
        }

        panel.Children.Add(position);
        panel.Children.Add(cards);
        panel.Children.Add(note);
        panel.Children.Add(error);
        panel.Children.Add(status);
        // contact-merge-dialog.tsx:185-197: the way back at the start (me-auto), Cancel, then the action; here the way back is Previous and Next
        var steps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = DialogStyles.ButtonGap };
        steps.Children.Add(previous);
        steps.Children.Add(next);
        panel.Children.Add(DialogStyles.Footer(steps, DialogStyles.FooterButton("Close", DialogTone.Plain, "contacts.merge-dialog.close", () => dialog.Complete(false)), merge));
        panel.Tag = merge;
        m.PropertyChanged += (_, _) => panel.DispatcherQueue.TryEnqueue(Refresh);
        Refresh();
        AutomationProperties.SetName(panel, "Duplicate contacts");
        return panel;
    }
}

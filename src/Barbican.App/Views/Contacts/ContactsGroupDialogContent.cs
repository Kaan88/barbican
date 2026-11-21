// Mirrors: Bulwark components/contacts/group-form.tsx (name, member search, a checkbox per contact), Rust contacts.rs groups; I-359, I-360. Draws ContactsGroupModel (its own Cancel and Save).
// Dialog shell: ui/dialog.tsx:131-134,181 through Views/Dialogs/DialogStyles; footer Cancel (outline) then Save: contact-group-form.tsx:203-210.

using Barbican.Presentation.Compose;
using DialogStyles = Barbican.App.Views.Dialogs.DialogStyles;
using Barbican.Presentation.Contacts;
using Barbican.Presentation.Dialogs;
using Microsoft.UI.Xaml.Automation;
using static Barbican.App.Views.Calendar.Editors.CalendarCardKit;

namespace Barbican.App.Views.Contacts;

/// <summary>Draws <see cref="ContactsGroupRequest"/>.</summary>
public sealed class ContactsGroupDialogContent : Barbican.App.Views.Dialogs.IDialogContent
{
    public bool OwnButtons => true;

    public double MaxWidth => 460;

    public bool CanDraw(object request) => request is ContactsGroupRequest;

    public UIElement Create(ActiveDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var m = ((ContactsGroupRequest)dialog.Request).Model;
        m.Finished += (_, e) => dialog.Complete(e.Done);
        var panel = Card("contacts.group-form", m.Heading, 380);
        panel.Children.RemoveAt(0); // the dialog host shows the title
        panel.Children.Add(Label("Name"));
        panel.Children.Add(Text("contacts.group-form.name", m.Name, v => m.Name = v, placeholder: "Group name"));
        panel.Children.Add(Label("Members"));
        var members = new StackPanel { Spacing = 2 };
        void Fill()
        {
            members.Children.Clear();
            foreach (var pick in m.Visible)
            {
                var p = pick;
                var box = Check("contacts.group-form.member." + p.Id, p.Name + (string.IsNullOrEmpty(p.Email) ? string.Empty : "  " + p.Email), p.Selected, v => p.Selected = v);
                members.Children.Add(box);
            }

            if (members.Children.Count == 0)
            {
                members.Children.Add(Body("No contacts."));
            }
        }

        var search = Text("contacts.group-form.search", string.Empty, v =>
        {
            m.Search = v;
            Fill();
        }, placeholder: "Search contacts");
        panel.Children.Add(search);
        panel.Children.Add(new ScrollViewer { Content = members, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Fill();
        var error = ErrorLine(m.Error);
        m.PropertyChanged += (_, _) => panel.DispatcherQueue.TryEnqueue(() => SetError(error, m.Error));
        panel.Children.Add(error);
        panel.Children.Add(DialogStyles.Footer(
            DialogStyles.FooterButton("Cancel", DialogTone.Plain, "contacts.group-form.cancel", () => dialog.Complete(false)),
            DialogStyles.FooterButton("Save", DialogTone.Primary, "contacts.group-form.save", () => ContactsUi.Run(() => m.SaveAsync(CancellationToken.None)))));
        AutomationProperties.SetName(panel, m.Heading);
        return panel;
    }
}

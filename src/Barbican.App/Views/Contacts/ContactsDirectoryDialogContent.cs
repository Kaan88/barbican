// Mirrors: Bulwark components/contacts/directory-browser.tsx (the server's accounts and groups, search, pick), the composer's directory button; I-368, I-369, B-CON-025, D-M5-13. Draws ContactsDirectoryModel; the answer is the ticked addresses joined with commas (its own Cancel and Add).

using Barbican.Presentation.Compose;
using Barbican.Presentation.Contacts;
using Barbican.Presentation.Dialogs;
using Microsoft.UI.Xaml.Automation;
using static Barbican.App.Views.Calendar.Editors.CalendarCardKit;

namespace Barbican.App.Views.Contacts;

/// <summary>Draws <see cref="ContactsDirectoryRequest"/>.</summary>
public sealed class ContactsDirectoryDialogContent : Barbican.App.Views.Dialogs.IDialogContent
{
    public bool OwnButtons => true;

    public double MaxWidth => 480;

    public bool CanDraw(object request) => request is ContactsDirectoryRequest;

    public UIElement Create(ActiveDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var m = ((ContactsDirectoryRequest)dialog.Request).Model;
        var panel = Card("contacts.directory-dialog", "Directory", 400);
        panel.Children.RemoveAt(0); // the dialog host shows the title
        var list = new StackPanel { Spacing = 2 };
        var error = ErrorLine(m.Error);
        var status = Body(m.Status);

        void Fill()
        {
            list.Children.Clear();
            foreach (var pick in m.Visible)
            {
                var p = pick;
                list.Children.Add(Check("contacts.directory-dialog.pick." + p.Id, p.Name + "  " + p.Email, p.Selected, v => p.Selected = v));
            }

            if (list.Children.Count == 0)
            {
                list.Children.Add(Body(m.IsLoaded ? "No one found." : "Loading the directory..."));
            }

            SetError(error, m.Error);
            status.Text = m.Status;
        }

        panel.Children.Add(Text("contacts.directory-dialog.search", string.Empty, v =>
        {
            m.Search = v;
            Fill();
        }, placeholder: "Search the directory"));
        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        panel.Children.Add(error);
        panel.Children.Add(status);
        panel.Children.Add(Buttons(
            Btn("Cancel", DialogTone.Plain, "contacts.directory-dialog.cancel", () => dialog.Complete(null)),
            Btn("Add", DialogTone.Primary, "contacts.directory-dialog.ok", () => dialog.Complete(m.Chosen.Length > 0 ? m.Chosen : null))));
        m.PropertyChanged += (_, _) => panel.DispatcherQueue.TryEnqueue(Fill);
        Fill();
        AutomationProperties.SetName(panel, "Directory");
        return panel;
    }
}

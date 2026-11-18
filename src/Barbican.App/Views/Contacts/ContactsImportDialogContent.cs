// Mirrors: Bulwark components/contacts/contact-import-dialog.tsx (the file, a preview, the duplicates and a skip, the address book, progress), Rust contacts_x.rs import; I-361, B-CON-007, B-CON-008, L-031. Draws ContactsImportViewModel (its own Cancel and Import).

#pragma warning disable MA0051 // a dialog is built in one pass from the model
using System.Globalization;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Contacts;
using Barbican.Presentation.Dialogs;
using Microsoft.UI.Xaml.Automation;
using static Barbican.App.Views.Calendar.Editors.CalendarCardKit;

namespace Barbican.App.Views.Contacts;

/// <summary>Draws <see cref="ContactsImportRequest"/>.</summary>
public sealed class ContactsImportDialogContent : Barbican.App.Views.Dialogs.IDialogContent
{
    public bool OwnButtons => true;

    public double MaxWidth => 520;

    public bool CanDraw(object request) => request is ContactsImportRequest;

    public UIElement Create(ActiveDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var m = ((ContactsImportRequest)dialog.Request).Model;
        m.Finished += (_, e) => dialog.Complete(e.Done);
        var panel = Card("contacts.import-dialog", "Import contacts", 420);
        panel.Children.RemoveAt(0); // the dialog host shows the title
        panel.Children.Add(Btn("Choose .vcf file", DialogTone.Plain, "contacts.import-dialog.pick", () => ContactsUi.Run(() => m.PickAsync(CancellationToken.None))));
        var status = Body(m.Status);
        panel.Children.Add(status);
        var list = new StackPanel { Spacing = 2 };
        var books = new StackPanel { Spacing = 4 };
        Action refresh = () => { };
        var skip = Check("contacts.import-dialog.skip", "Skip contacts that are already in your address book", m.SkipDuplicates, v =>
        {
            m.SkipDuplicates = v;
            refresh();
        });
        var error = ErrorLine(m.Error);
        var go = Btn("Import", DialogTone.Primary, "contacts.import-dialog.ok", () => ContactsUi.Run(async () => _ = await m.ImportAsync(CancellationToken.None).ConfigureAwait(true)));

        void Refresh()
        {
            status.Text = m.Status;
            SetError(error, m.Error);
            list.Children.Clear();
            var dup = m.Duplicates.ToHashSet();
            foreach (var (card, index) in m.Cards.Take(100).Select((c, i) => (c, i)))
            {
                var name = ContactsListRules.FullName(card);
                list.Children.Add(new TextBlock { Text = name + (dup.Contains(index) ? "  (already there)" : string.Empty), FontSize = 12.5, Foreground = ContactsUi.Palette(dup.Contains(index) ? "Faint" : "Body"), TextTrimming = TextTrimming.CharacterEllipsis });
            }

            if (m.Cards.Count > 100)
            {
                list.Children.Add(Body("and " + (m.Cards.Count - 100).ToString(CultureInfo.InvariantCulture) + " more"));
            }

            skip.Visibility = m.Duplicates.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            books.Visibility = m.BookChoices.Count > 1 && m.Cards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            go.Content = "Import " + m.ImportCount.ToString(CultureInfo.InvariantCulture);
            go.IsEnabled = m.ImportCount > 0 && !m.Busy;
        }

        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        panel.Children.Add(skip);
        books.Children.Add(Label("Into address book"));
        var choices = m.BookChoices.ToList();
        books.Children.Add(Combo("contacts.import-dialog.book", choices.Select(b => b.Name), Math.Max(0, choices.FindIndex(b => string.Equals(b.Id, m.BookId, StringComparison.Ordinal))), i => m.BookId = choices[i].Id));
        panel.Children.Add(books);
        panel.Children.Add(error);
        panel.Children.Add(Buttons(Btn("Cancel", DialogTone.Plain, "contacts.import-dialog.cancel", () => dialog.Complete(false)), go));
        refresh = Refresh;
        m.PropertyChanged += (_, _) => panel.DispatcherQueue.TryEnqueue(Refresh);
        Refresh();
        AutomationProperties.SetName(panel, "Import contacts");
        return panel;
    }
}

// Mirrors: Bulwark components/contacts/contact-form.tsx (name block, structured entries with a type each, categories, address book, photo), Rust pim-contacts.slint editor (section captions, field label column, type select, Add buttons); I-363..I-367, B-CON-009..012, B-CON-015.
// Draws ContactsFormModel inside the card of the dialog host (it brings its own Cancel and Save). A type select is a button that steps through the options (a drop-down opens a popup, ADR 0007); nothing here writes: Save is the model's.

using Barbican.Presentation.Compose;
using Barbican.Presentation.Contacts;
using Barbican.Presentation.Dialogs;
using Microsoft.UI.Xaml.Automation;
using static Barbican.App.Views.Calendar.Editors.CalendarCardKit;

#pragma warning disable MA0051 // a form is built in one pass from the model
namespace Barbican.App.Views.Contacts;

/// <summary>Draws <see cref="ContactsFormRequest"/>.</summary>
public sealed class ContactsFormDialogContent : Barbican.App.Views.Dialogs.IDialogContent
{
    public bool OwnButtons => true;

    public double MaxWidth => 560;

    public bool CanDraw(object request) => request is ContactsFormRequest;

    public UIElement Create(ActiveDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var m = ((ContactsFormRequest)dialog.Request).Model;
        m.Finished += (_, e) => dialog.Complete(e.Done);
        var body = new StackPanel { Spacing = 6, MinWidth = 440 };
        var scroll = new ScrollViewer { Content = body, MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 12, 0) };
        var host = new StackPanel { Spacing = 8 };
        host.Children.Add(scroll);
        AutomationProperties.SetAutomationId(host, "contacts.form");
        AutomationProperties.SetName(host, m.Heading);

        void Rebuild()
        {
            body.Children.Clear();
            Fill(body, m, Rebuild);
        }

        Rebuild();
        var error = ErrorLine(m.Error);
        m.PropertyChanged += (_, _) => scroll.DispatcherQueue.TryEnqueue(() => SetError(error, m.Error));
        host.Children.Add(error);
        host.Children.Add(Buttons(
            Btn("Cancel", DialogTone.Plain, "contacts.form.cancel", () => dialog.Complete(false)),
            Btn("Save", DialogTone.Primary, "contacts.form.save", () => ContactsUi.Run(() => m.SaveAsync(CancellationToken.None)))));
        return host;
    }

    private static void Fill(StackPanel body, ContactsFormModel m, Action rebuild)
    {
        // photo
        var tint = ContactsAvatar.Tint(m.Form.Given + m.Form.Surname);
        var photo = ContactsAvatar.Photo(m.PhotoUri);
        var avatar = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(22), Background = photo is not null ? photo : tint.Background, Child = photo is null ? new TextBlock { Text = m.Initials, FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = tint.Foreground, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : null };
        var photoRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        photoRow.Children.Add(avatar);
        var photoButtons = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        photoButtons.Children.Add(Btn("Choose photo", DialogTone.Plain, "contacts.form.photo", () => ContactsUi.Run(async () =>
        {
            await m.PickPhotoAsync(CancellationToken.None).ConfigureAwait(true);
            rebuild();
        })));
        if (m.PhotoUri is not null)
        {
            photoButtons.Children.Add(Btn("Remove photo", DialogTone.Plain, "contacts.form.photo.remove", () =>
            {
                m.RemovePhoto();
                rebuild();
            }));
        }

        photoRow.Children.Add(photoButtons);
        body.Children.Add(photoRow);

        // name block
        var f = m.Form;
        body.Children.Add(Label("Name"));
        body.Children.Add(Pair("form.prefix", "Prefix", f.Prefix, v => m.Form = m.Form with { Prefix = v }, "form.given", "First name", f.Given, v => m.Form = m.Form with { Given = v }));
        body.Children.Add(Pair("form.middle", "Middle name", f.Middle, v => m.Form = m.Form with { Middle = v }, "form.surname", "Last name", f.Surname, v => m.Form = m.Form with { Surname = v }));
        body.Children.Add(Pair("form.suffix", "Suffix", f.Suffix, v => m.Form = m.Form with { Suffix = v }, "form.nickname", "Nickname", f.Nickname, v => m.Form = m.Form with { Nickname = v }));
        body.Children.Add(Label("Work"));
        body.Children.Add(Pair("form.org", "Organization", f.Org, v => m.Form = m.Form with { Org = v }, "form.dept", "Department", f.Dept, v => m.Form = m.Form with { Dept = v }));
        body.Children.Add(Pair("form.job", "Job title", f.JobTitle, v => m.Form = m.Form with { JobTitle = v }, "form.role", "Role", f.Role, v => m.Form = m.Form with { Role = v }));

        // typed entries
        foreach (var (section, title, add) in ContactsFormModel.Sections)
        {
            body.Children.Add(Label(title));
            for (var i = 0; i < m.Fields.Count; i++)
            {
                if (string.Equals(m.Fields[i].Section, section, StringComparison.Ordinal))
                {
                    body.Children.Add(Entry(m, i, rebuild));
                }
            }

            body.Children.Add(Btn(add, DialogTone.Plain, "contacts.form.add." + section, () =>
            {
                m.AddField(section);
                rebuild();
            }));
        }

        // categories, address book, notes
        body.Children.Add(Label("Categories (separated by commas)"));
        body.Children.Add(Text("contacts.form.categories", m.Categories, v => m.Categories = v));
        if (m.IsNew && m.BookChoices.Count > 1)
        {
            body.Children.Add(Label("Address book"));
            var books = m.BookChoices.ToList();
            body.Children.Add(Combo("contacts.form.book", books.Select(b => b.Name), Math.Max(0, books.FindIndex(b => string.Equals(b.Id, m.BookId, StringComparison.Ordinal))), i => m.BookId = books[i].Id));
        }

        body.Children.Add(Label("Notes"));
        body.Children.Add(Text("contacts.form.notes", f.Notes, v => m.Form = m.Form with { Notes = v }, multiline: true));
    }

    private static Grid Pair(string idA, string placeholderA, string valueA, Action<string> setA, string idB, string placeholderB, string valueB, Action<string> setB)
    {
        var grid = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } } };
        grid.Children.Add(Text("contacts." + idA, valueA, setA, placeholder: placeholderA));
        var right = Text("contacts." + idB, valueB, setB, placeholder: placeholderB);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private static StackPanel Entry(ContactsFormModel m, int index, Action rebuild)
    {
        var field = m.Fields[index];
        var section = field.Section;
        var kinds = ContactsFormModel.KindsOf(section);
        var row = new StackPanel { Spacing = 4 };
        var line = new Grid { ColumnSpacing = 6, ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } } };
        var kindButton = Btn(kinds[Math.Clamp(field.Kind, 0, kinds.Count - 1)], DialogTone.Plain, "contacts.form.kind." + section + "." + index, () =>
        {
            m.UpdateField(index, x => x with { Kind = (x.Kind + 1) % kinds.Count });
            rebuild();
        });
        kindButton.MinWidth = 96;
        line.Children.Add(kindButton);
        var first = Text("contacts.form." + section + "." + index, field.A, v => m.UpdateField(index, x => x with { A = v }), placeholder: Placeholder(section));
        Grid.SetColumn(first, 1);
        line.Children.Add(first);
        var remove = Btn("✕", DialogTone.Plain, "contacts.form.remove." + section + "." + index, () =>
        {
            m.RemoveField(index);
            rebuild();
        });
        remove.Width = 34;
        Grid.SetColumn(remove, 2);
        line.Children.Add(remove);
        row.Children.Add(line);
        if (string.Equals(section, "phone", StringComparison.Ordinal))
        {
            var contexts = Barbican.Core.Contacts.ContactOptions.PhoneContexts;
            row.Children.Add(Btn("For: " + contexts[Math.Clamp(field.Kind2, 0, contexts.Count - 1)], DialogTone.Plain, "contacts.form.phonectx." + index, () =>
            {
                m.UpdateField(index, x => x with { Kind2 = (x.Kind2 + 1) % contexts.Count });
                rebuild();
            }));
        }
        else if (string.Equals(section, "address", StringComparison.Ordinal))
        {
            row.Children.Add(Text("contacts.form.address.city." + index, field.B, v => m.UpdateField(index, x => x with { B = v }), placeholder: "City"));
            row.Children.Add(Pair("form.address.region." + index, "Region", field.C, v => m.UpdateField(index, x => x with { C = v }), "form.address.postcode." + index, "Postcode", field.D, v => m.UpdateField(index, x => x with { D = v })));
            row.Children.Add(Text("contacts.form.address.country." + index, field.E, v => m.UpdateField(index, x => x with { E = v }), placeholder: "Country"));
        }
        else if (string.Equals(section, "im", StringComparison.Ordinal))
        {
            row.Children.Add(Text("contacts.form.im.service." + index, field.B, v => m.UpdateField(index, x => x with { B = v }), placeholder: "Service (Signal, Matrix, ...)"));
        }

        return row;
    }

    private static string Placeholder(string section) => section switch
    {
        "email" => "name@example.org",
        "phone" => "Phone number",
        "address" => "Street",
        "url" => "https://",
        "im" => "User name",
        "date" => "YYYY-MM-DD or --MM-DD",
        "related" => "Name",
        _ => string.Empty,
    };
}

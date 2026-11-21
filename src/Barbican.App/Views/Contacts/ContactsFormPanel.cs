// Mirrors: Bulwark components/contacts/contact-form.tsx:237-262 (FormSection: py-5, a 16 px muted icon, the title text-xs semibold muted), :868-975 (the form is a column: header px-6 py-4 border-b with the title and close, a scrollable body with the photo row, the address book first, then the sections divided by a hairline, a footer px-6 py-4 border-t with Cancel and Save),
// :945-959 the address book, :962-1038 identity and name fields, :1041-1432 the sections; Rust pim-contacts.slint editor (section captions, label column, Add buttons); I-363..I-367, B-CON-009..012, B-CON-015.
// Draws ContactsFormModel as the panel of the contact pane (CT-02) or, when the Contacts app is not on screen (the reader's "Add to contacts"), inside the dialog card. It never writes: Save is the model's. A type select is a button that steps through the options (a drop-down opens a popup, ADR 0007).

using Barbican.App.Controls;
using Barbican.App.Theme;
using Barbican.Presentation.Contacts;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;

#pragma warning disable MA0051 // a form is built in one pass from the model
namespace Barbican.App.Views.Contacts;

/// <summary>The contact form: header, scrolling sections, footer.</summary>
public sealed class ContactsFormPanel : UserControl
{
    private readonly ContactsFormModel _m;
    private readonly Action<bool> _finish;
    private readonly StyleState _look = StyleState.Current;
    private readonly StackPanel _body = new() { Padding = new Thickness(24, 16, 24, 16), MaxWidth = 768, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _error = new() { FontSize = 14, Foreground = ContactsUi.Palette("RoseFg"), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly Border _errorBox = new() { Margin = new Thickness(24, 0, 24, 8), Padding = new Thickness(12, 8, 12, 8), Visibility = Visibility.Collapsed };

    public ContactsFormPanel(ContactsFormModel model, Action<bool> finish, bool header)
    {
        _m = model;
        _finish = finish;
        _errorBox.Background = new SolidColorBrush(ContactsUi.Palette("RoseBg").Color);
        _errorBox.CornerRadius = _look.ButtonRadius;
        _errorBox.Child = _error;
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body, MaxHeight = header ? double.PositiveInfinity : 520 };
        var root = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = header ? new GridLength(1, GridUnitType.Star) : GridLength.Auto }, new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Auto } } };
        if (header)
        {
            root.Children.Add(Header());
        }

        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        Grid.SetRow(_errorBox, 2);
        root.Children.Add(_errorBox);
        var footer = Footer(header);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);
        Content = root;
        AutomationProperties.SetAutomationId(this, "contacts.form");
        AutomationProperties.SetName(this, model.Heading);
        _m.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(ShowError);
        Rebuild();
    }

    private void ShowError()
    {
        _error.Text = _m.Error;
        _errorBox.Visibility = _m.Error.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _error.Visibility = _errorBox.Visibility;
    }

    private Border Header()
    {
        var row = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } } };
        row.Children.Add(new TextBlock { Text = _m.Heading, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = ContactsUi.Palette("Text"), VerticalAlignment = VerticalAlignment.Center });
        var close = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = 32, Height = 32, CornerRadius = _look.ButtonRadius, Content = new SIcon { Kind = "Close", Size = 16, StrokeWidth = 2 } };
        AutomationProperties.SetAutomationId(close, "contacts.form.close");
        AutomationProperties.SetName(close, "Close");
        close.Click += (_, _) => _finish(false);
        Grid.SetColumn(close, 1);
        row.Children.Add(close);
        return new Border { Padding = new Thickness(24, 16, 24, 16), BorderBrush = ContactsUi.Palette("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Child = row };
    }

    // :1439-1447 Cancel (outline) and Save (primary) at the end, 8 apart
    private Border Footer(bool border)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Style = (Style)Application.Current.Resources["Button.Action"], Height = 36, CornerRadius = _look.ButtonRadius, Padding = new Thickness(16, 0, 16, 0), FontSize = 14, FontWeight = FontWeights.Medium, Content = "Cancel" };
        AutomationProperties.SetAutomationId(cancel, "contacts.form.cancel");
        cancel.Click += (_, _) => _finish(false);
        var save = new Button { Style = (Style)Application.Current.Resources["Button.Primary"], Height = 36, CornerRadius = _look.ButtonRadius, Padding = new Thickness(16, 0, 16, 0), FontSize = 14, FontWeight = FontWeights.Medium, Content = "Save" };
        AutomationProperties.SetAutomationId(save, "contacts.form.save");
        save.Click += (_, _) => ContactsUi.Run(() => _m.SaveAsync(CancellationToken.None));
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        return new Border { Padding = new Thickness(border ? 24 : 0, border ? 16 : 12, border ? 24 : 0, border ? 16 : 0), BorderBrush = ContactsUi.Palette("Line"), BorderThickness = new Thickness(0, border ? 1 : 0, 0, 0), Child = buttons };
    }

    private void Rebuild()
    {
        _body.Children.Clear();
        _body.Children.Add(PhotoRow());
        var first = true;

        void Add(string icon, string title, UIElement content)
        {
            _body.Children.Add(Section(icon, title, content, first));
            first = false;
        }

        // :945 the address book comes first, only when there is a choice
        if (_m.IsNew && _m.BookChoices.Count > 1)
        {
            var books = _m.BookChoices.ToList();
            Add("Book", "Address book", Choice("contacts.form.book", books.Select(b => b.Name), Math.Max(0, books.FindIndex(b => string.Equals(b.Id, _m.BookId, StringComparison.Ordinal))), i => _m.BookId = books[i].Id));
        }

        var f = _m.Form;
        var identity = new StackPanel { Spacing = 8 };
        identity.Children.Add(Pair("form.prefix", "Prefix", f.Prefix, v => _m.Form = _m.Form with { Prefix = v }, "form.given", "First name", f.Given, v => _m.Form = _m.Form with { Given = v }));
        identity.Children.Add(Pair("form.middle", "Middle name", f.Middle, v => _m.Form = _m.Form with { Middle = v }, "form.surname", "Last name", f.Surname, v => _m.Form = _m.Form with { Surname = v }));
        identity.Children.Add(Pair("form.suffix", "Suffix", f.Suffix, v => _m.Form = _m.Form with { Suffix = v }, "form.nickname", "Nickname", f.Nickname, v => _m.Form = _m.Form with { Nickname = v }));
        Add("User", "Identity", identity);

        var work = new StackPanel { Spacing = 8 };
        work.Children.Add(Pair("form.org", "Organization", f.Org, v => _m.Form = _m.Form with { Org = v }, "form.dept", "Department", f.Dept, v => _m.Form = _m.Form with { Dept = v }));
        work.Children.Add(Pair("form.job", "Job title", f.JobTitle, v => _m.Form = _m.Form with { JobTitle = v }, "form.role", "Role", f.Role, v => _m.Form = _m.Form with { Role = v }));
        Add("Folder", "Work", work);

        foreach (var (section, title, add) in ContactsFormModel.Sections)
        {
            var entries = new StackPanel { Spacing = 8 };
            for (var i = 0; i < _m.Fields.Count; i++)
            {
                if (string.Equals(_m.Fields[i].Section, section, StringComparison.Ordinal))
                {
                    entries.Children.Add(Entry(section, i));
                }
            }

            var plus = Ghost("contacts.form.add." + section, add, () =>
            {
                _m.AddField(section);
                Rebuild();
            });
            entries.Children.Add(plus);
            Add(IconOf(section), title, entries);
        }

        var cats = new StackPanel { Spacing = 4 };
        cats.Children.Add(Field("contacts.form.categories", _m.Categories, v => _m.Categories = v, placeholder: "Separated by commas"));
        Add("Tag", "Categories", cats);
        Add("Pen", "Notes", Field("contacts.form.notes", f.Notes, v => _m.Form = _m.Form with { Notes = v }, multiline: true));
        ShowError();
    }

    private static string IconOf(string section) => section switch
    {
        "email" => "Mail",
        "phone" => "Phone",
        "address" => "Book",
        "url" => "External",
        "im" => "Send",
        "date" => "Clock",
        _ => "Users",
    };

    // :237-262 FormSection: py-5, the icon and the title in one row, a hairline above all but the first
    private static Border Section(string icon, string title, UIElement content, bool first)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        head.Children.Add(new SIcon { Kind = icon, Size = 16, StrokeWidth = 1.8, Foreground = ContactsUi.Palette("Dim"), VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = ContactsUi.Palette("Dim"), VerticalAlignment = VerticalAlignment.Center });
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(head);
        panel.Children.Add(content);
        return new Border { Padding = new Thickness(0, 20, 0, 20), BorderBrush = ContactsUi.Palette("Line"), BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0), Child = panel };
    }

    // :877-935 the photo row: an 80 px circle, a hint, and the remove link
    private StackPanel PhotoRow()
    {
        var name = (_m.Form.Given + " " + _m.Form.Surname).Trim();
        var email = _m.Fields.FirstOrDefault(x => string.Equals(x.Section, "email", StringComparison.Ordinal))?.A;
        var photo = ContactsAvatar.Photo(_m.PhotoUri);
        var typed = name.Length > 0 || !string.IsNullOrWhiteSpace(email);
        (Brush Background, Brush Foreground) tint = typed ? ContactsAvatar.BulwarkTint(name, email) : (ContactsUi.Palette("Dim"), ContactsUi.Palette("Bg"));
        var avatar = new Border { Width = 80, Height = 80, CornerRadius = new CornerRadius(40), Background = photo is not null ? photo : tint.Background, Child = photo is null ? new TextBlock { Text = _m.Initials, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = tint.Foreground, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : null };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Thickness(0, 0, 0, 4) };
        row.Children.Add(avatar);
        var side = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        side.Children.Add(new TextBlock { Text = "A square picture of at least 200 px works best.", FontSize = 12, Foreground = ContactsUi.Palette("Dim"), TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        buttons.Children.Add(Outline("contacts.form.photo", "Choose photo", () => ContactsUi.Run(async () =>
        {
            await _m.PickPhotoAsync(CancellationToken.None).ConfigureAwait(true);
            Rebuild();
        })));
        if (_m.PhotoUri is not null)
        {
            var remove = Ghost("contacts.form.photo.remove", "Remove photo", () =>
            {
                _m.RemovePhoto();
                Rebuild();
            });
            remove.Foreground = ContactsUi.Palette("RoseFg");
            buttons.Children.Add(remove);
        }

        side.Children.Add(buttons);
        row.Children.Add(side);
        var wrap = new StackPanel();
        wrap.Children.Add(row);
        return wrap;
    }

    private Button Outline(string id, string label, Action click)
    {
        var button = new Button { Style = (Style)Application.Current.Resources["Button.Action"], Height = 32, CornerRadius = _look.ButtonRadius, Padding = new Thickness(10, 0, 10, 0), FontSize = 14, FontWeight = FontWeights.Medium, Content = label };
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => click();
        return button;
    }

    // a ghost text button (button.tsx ghost xs): a plus and the label, muted until hovered
    private Button Ghost(string id, string label, Action click)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new SIcon { Kind = "Plus", Size = 14, StrokeWidth = 2 });
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Height = 28, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = _look.ButtonRadius, Padding = new Thickness(8, 0, 10, 0), FontSize = 12, FontWeight = FontWeights.Medium, Content = row };
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => click();
        return button;
    }

    private static TextBox Field(string id, string value, Action<string> set, bool multiline = false, string? placeholder = null)
    {
        var box = new TextBox { Text = value, PlaceholderText = placeholder ?? string.Empty, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 80 : 32, FontSize = 14, Padding = multiline ? new Thickness(10, 8, 10, 8) : new Thickness(10, 6, 10, 6), VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center };
        box.TextChanged += (_, _) => set(box.Text);
        AutomationProperties.SetAutomationId(box, id);
        return box;
    }

    private static Grid Pair(string idA, string labelA, string valueA, Action<string> setA, string idB, string labelB, string valueB, Action<string> setB)
    {
        var grid = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } } };
        grid.Children.Add(Labelled(labelA, Field("contacts." + idA, valueA, setA)));
        var right = Labelled(labelB, Field("contacts." + idB, valueB, setB));
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private static StackPanel Labelled(string label, UIElement field)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = ContactsUi.Palette("Dim") });
        panel.Children.Add(field);
        return panel;
    }

    /// <summary>A one-of-many choice as a row of chips, the chosen one filled (a drop-down opens a popup: ADR 0007).</summary>
    private StackPanel Choice(string id, IEnumerable<string> items, int selected, Action<int> set)
    {
        var names = items.ToList();
        var chips = new List<Button>();
        var current = selected;
        void Paint()
        {
            for (var i = 0; i < chips.Count; i++)
            {
                chips[i].Style = (Style)Application.Current.Resources[i == current ? "Button.Primary" : "Button.Action"];
                chips[i].FontSize = 13;
                chips[i].Height = 30;
                chips[i].Padding = new Thickness(12, 0, 12, 0);
                chips[i].CornerRadius = _look.ButtonRadius;
            }
        }

        var wrap = new WrapRow { Spacing = 6 };
        for (var i = 0; i < names.Count; i++)
        {
            var index = i;
            var chip = new Button { Content = names[i] };
            AutomationProperties.SetAutomationId(chip, id + "." + i);
            chip.Click += (_, _) =>
            {
                current = index;
                Paint();
                set(index);
            };
            chips.Add(chip);
            wrap.Children.Add(chip);
        }

        Paint();
        var host = new StackPanel();
        host.Children.Add(wrap);
        AutomationProperties.SetAutomationId(host, id);
        return host;
    }

    private StackPanel Entry(string section, int index)
    {
        var field = _m.Fields[index];
        var kinds = ContactsFormModel.KindsOf(section);
        var row = new StackPanel { Spacing = 6 };
        var line = new Grid { ColumnSpacing = 6, ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } } };
        var kindButton = new Button { Style = (Style)Application.Current.Resources["Button.Action"], Height = 32, MinWidth = 96, CornerRadius = _look.FieldRadius, Padding = new Thickness(10, 0, 10, 0), FontSize = 14, FontWeight = FontWeights.Normal, Content = kinds[Math.Clamp(field.Kind, 0, kinds.Count - 1)] };
        AutomationProperties.SetAutomationId(kindButton, "contacts.form.kind." + section + "." + index);
        kindButton.Click += (_, _) =>
        {
            _m.UpdateField(index, x => x with { Kind = (x.Kind + 1) % kinds.Count });
            Rebuild();
        };
        line.Children.Add(kindButton);
        var first = Field("contacts.form." + section + "." + index, field.A, v => _m.UpdateField(index, x => x with { A = v }), placeholder: Placeholder(section));
        Grid.SetColumn(first, 1);
        line.Children.Add(first);
        var remove = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = 32, Height = 32, CornerRadius = _look.ButtonRadius, Content = new SIcon { Kind = "Close", Size = 14, StrokeWidth = 2.2 } };
        AutomationProperties.SetAutomationId(remove, "contacts.form.remove." + section + "." + index);
        AutomationProperties.SetName(remove, "Remove");
        remove.Click += (_, _) =>
        {
            _m.RemoveField(index);
            Rebuild();
        };
        Grid.SetColumn(remove, 2);
        line.Children.Add(remove);
        row.Children.Add(line);
        if (string.Equals(section, "phone", StringComparison.Ordinal))
        {
            var contexts = Barbican.Core.Contacts.ContactOptions.PhoneContexts;
            var ctx = Outline("contacts.form.phonectx." + index, "For: " + contexts[Math.Clamp(field.Kind2, 0, contexts.Count - 1)], () =>
            {
                _m.UpdateField(index, x => x with { Kind2 = (x.Kind2 + 1) % contexts.Count });
                Rebuild();
            });
            ctx.HorizontalAlignment = HorizontalAlignment.Left;
            row.Children.Add(ctx);
        }
        else if (string.Equals(section, "address", StringComparison.Ordinal))
        {
            row.Children.Add(Field("contacts.form.address.city." + index, field.B, v => _m.UpdateField(index, x => x with { B = v }), placeholder: "City"));
            row.Children.Add(Pair("form.address.region." + index, "Region", field.C, v => _m.UpdateField(index, x => x with { C = v }), "form.address.postcode." + index, "Postcode", field.D, v => _m.UpdateField(index, x => x with { D = v })));
            row.Children.Add(Field("contacts.form.address.country." + index, field.E, v => _m.UpdateField(index, x => x with { E = v }), placeholder: "Country"));
        }
        else if (string.Equals(section, "im", StringComparison.Ordinal))
        {
            row.Children.Add(Field("contacts.form.im.service." + index, field.B, v => _m.UpdateField(index, x => x with { B = v }), placeholder: "Service (Signal, Matrix, ...)"));
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

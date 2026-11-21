// Mirrors: Bulwark components/contacts/contact-detail.tsx:112-122 (the empty pane: a 64 px book icon at 20 % opacity and "Select a contact to view details"), :157-235 (the header: border-b, px-6 py-6, a 48 px avatar, the name text-xl semibold, nickname and organization text-sm muted, outline xs buttons Send email, Call, Edit, then the print and more toolbar buttons),
// :237-520 (sections divided by a hairline, py-6, the title text-xs semibold muted, field rows with a 16 px muted icon, an xs muted label and the value text-sm, mail and phone values in the primary colour), :403-410 (keyword chips text-xs rounded-full bg-primary/10 text-primary), :531-550 Section, FieldRow.
// The Bulwark layout of the contact pane; Bento and Nordic keep ContactsDetailView.Render. Every button is a registered command or the contact menu.

using System.Text.Json.Nodes;
using Barbican.App.Controls;
using Barbican.App.Shell;
using Barbican.Core.Contacts;
using Barbican.Presentation.Contacts;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;

#pragma warning disable MA0051 // a drawing routine builds one frame in one pass (no flicker); splitting it would only pass the same locals around
namespace Barbican.App.Views.Contacts;

public sealed partial class ContactsDetailView
{
    private static StackPanel BulwarkEmpty()
    {
        var panel = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 160, 0, 0) };
        panel.Children.Add(new SIcon { Kind = "Book", Size = 64, StrokeWidth = 1.2, Opacity = 0.2, Foreground = ContactsUi.Palette("Dim"), HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = "Select a contact to view details", FontSize = 14, Foreground = ContactsUi.Palette("Dim"), HorizontalAlignment = HorizontalAlignment.Center });
        AutomationProperties.SetAutomationId(panel, "contacts.detail.empty");
        return panel;
    }

    private void RenderBulwark(JsonNode card)
    {
        var name = ContactsListRules.FullName(card);
        var isGroup = ContactsJson.IsGroup(card);
        var readOnly = _vm.Place == ContactsPlace.Directory;
        var email = ContactsJson.Emails(card).FirstOrDefault() ?? string.Empty;
        var phone = ContactsJson.All(card["phones"], "number").FirstOrDefault();
        var id = ContactsJson.Id(card);
        var radius = _look.ButtonRadius;

        // the header
        var tint = ContactsAvatar.BulwarkTint(name, email);
        var photo = ContactsAvatar.Photo(ContactsJson.PhotoUri(card));
        var avatar = new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(24),
            Background = photo is not null ? photo : tint.Background,
            VerticalAlignment = VerticalAlignment.Center,
            Child = photo is null ? new TextBlock { Text = ContactsListRules.Initials(name, email), FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = tint.Foreground, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : null,
        };
        AutomationProperties.SetAutomationId(avatar, "contacts.detail.avatar");
        if (photo is null && !isGroup)
        {
            LoadBlobPhoto(avatar, card);
        }

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        titles.Children.Add(new TextBlock { Text = name.Length > 0 ? name : "-", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = ContactsUi.Palette("Text"), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, IsTextSelectionEnabled = true });
        AutomationProperties.SetAutomationId(titles, "contacts.detail.name");
        var nickname = ContactsJson.FirstOf(card["nicknames"], "name");
        if (nickname.Length > 0)
        {
            titles.Children.Add(Sub("“" + nickname + "”"));
        }

        var role = string.Join(" · ", new[] { ContactsJson.FirstOf(card["titles"], "name"), ContactsJson.FirstOf(card["organizations"], "name") }.Where(t => t.Length > 0));
        if (role.Length > 0)
        {
            titles.Children.Add(Sub(role));
        }

        if (isGroup)
        {
            titles.Children.Add(Sub("Group · " + _vm.Contacts.Count(c => ContactsJson.HasMember(card, c)) + " members"));
        }

        var identity = new StackPanel { Orientation = Orientation.Horizontal };
        identity.Children.Add(avatar);
        identity.Children.Add(titles);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(Outline("contacts.detail.email", "Send email", "Send", () => ContactsUi.RunCommand(ContactsCommandIds.SendEmail, id), email.Length > 0, radius));
        if (phone is not null && ContactsJson.TelUri(phone) is { } tel)
        {
            actions.Children.Add(Outline("contacts.detail.call", "Call", "Phone", () => OpenTel(tel), true, radius));
        }

        if (!readOnly)
        {
            actions.Children.Add(Outline("contacts.detail.edit", isGroup ? "Edit members" : "Edit", "Pen", () => ContactsUi.RunCommand(isGroup ? ContactsCommandIds.GroupEdit : ContactsCommandIds.Edit, id), true, radius));
            actions.Children.Add(Toolbar("contacts.detail.print", "Print", "Print", () => ContactsUi.RunCommand(ContactsCommandIds.Print, id), radius));
            var more = Toolbar("contacts.detail.more", "More actions", "More", () => { }, radius);
            more.Click += (_, _) =>
            {
                var at = more.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, more.ActualHeight + 4));
                if (isGroup)
                {
                    ContactsUi.ShowMenu(ContactsMenuIds.Group, at.X, at.Y, new ContactMenuTarget(_vm.AccountKey ?? string.Empty, [], GroupId: id));
                }
                else
                {
                    ContactsUi.ShowContactMenu(id, at.X, at.Y);
                }
            };
            actions.Children.Add(more);
        }

        var headRow = new Grid { ColumnSpacing = 16, ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } } };
        headRow.Children.Add(identity);
        Grid.SetColumn(actions, 1);
        headRow.Children.Add(actions);
        _body.Children.Add(new Border { Padding = new Thickness(24), BorderBrush = ContactsUi.Palette("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Child = headRow });

        // the sections: py-6 with a hairline between them
        var first = true;
        void AddSection(string title, UIElement content)
        {
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = ContactsUi.Palette("Dim") });
            panel.Children.Add(content);
            _body.Children.Add(new Border { Padding = new Thickness(24, 24, 24, 24), BorderBrush = ContactsUi.Palette("Line"), BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0), Child = panel });
            first = false;
        }

        var keywords = ContactsJson.Keys(card, "keywords");
        if (keywords.Count > 0)
        {
            var chips = new WrapRow { Spacing = 6 };
            foreach (var k in keywords)
            {
                chips.Children.Add(new Border { Background = ContactsUi.Palette("FeedbackActive"), CornerRadius = new CornerRadius(12), Padding = new Thickness(8, 4, 8, 4), Child = new TextBlock { Text = k, FontSize = 12, Foreground = ContactsUi.Palette("FeedbackActiveFg") } });
            }

            AddSection("Categories", chips);
        }

        foreach (var group in _vm.DetailLines.GroupBy(l => l.Section, StringComparer.Ordinal))
        {
            var rows = new StackPanel { Spacing = 12 };
            foreach (var line in group)
            {
                rows.Children.Add(Field(group.Key, line));
            }

            AddSection(SectionTitle(group.Key), rows);
        }

        if (isGroup)
        {
            var members = _vm.Contacts.Where(c => ContactsJson.HasMember(card, c)).ToList();
            var list = new StackPanel { Spacing = 8 };
            foreach (var m in members)
            {
                var memberId = ContactsJson.Id(m);
                var link = new HyperlinkButton { Content = ContactsListRules.FullName(m), Padding = new Thickness(0), FontSize = 14 };
                AutomationProperties.SetAutomationId(link, "contacts.detail.member." + memberId);
                link.Click += (_, _) =>
                {
                    _vm.ShowPlace(ContactsPlace.All, null);
                    _vm.SelectContact(memberId, false);
                    _vm.Open(memberId);
                };
                list.Children.Add(link);
            }

            AddSection("Members", list);
        }
    }

    private static string SectionTitle(string section) => section.Length == 0 ? string.Empty : char.ToUpperInvariant(section[0]) + section[1..].ToLowerInvariant();

    private static string? IconOf(string section) => section switch
    {
        "EMAIL" => "Mail",
        "PHONE" => "Phone",
        "ADDRESS" => "Book",
        "NOTES" => "Pen",
        _ => null,
    };

    // :540-549 FieldRow: a 16 px muted icon, then an xs muted label over the value (text-sm; mail and phone in the primary colour)
    private static Grid Field(string section, ContactLine line)
    {
        var row = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(16) }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } } };
        if (IconOf(section) is { } icon)
        {
            row.Children.Add(new SIcon { Kind = icon, Size = 16, StrokeWidth = 1.8, Foreground = ContactsUi.Palette("Dim"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) });
        }

        var text = new StackPanel();
        if (line.Label.Length > 0)
        {
            text.Children.Add(new TextBlock { Text = line.Label, FontSize = 12, Foreground = ContactsUi.Palette("Dim"), Margin = new Thickness(0, 0, 0, 2) });
        }

        var link = string.Equals(section, "EMAIL", StringComparison.Ordinal) || string.Equals(section, "PHONE", StringComparison.Ordinal);
        var value = new TextBlock { Text = line.Value, FontSize = 14, Foreground = ContactsUi.Palette(link ? "Accent" : "Body"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        text.Children.Add(value);
        if (string.Equals(section, "PHONE", StringComparison.Ordinal) && ContactsJson.TelUri(line.Value) is { } tel)
        {
            // contact-detail.tsx:267-269: the number is the tel: link (text-sm text-primary)
            AutomationProperties.SetAutomationId(value, "contacts.detail.phone");
            AutomationProperties.SetName(value, "Call " + line.Value);
            value.Tapped += (_, _) => OpenTel(tel);
        }

        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    private static void OpenTel(Uri tel) =>
        // a phone number is a tel: link (I-205): the system hands it to the phone app the user chose; only plain digits ever leave the app
        ContactsUi.Run(async () => _ = await (ViewHost.Find<Barbican.Platform.IBrowserLauncher>()?.OpenAsync(tel, CancellationToken.None) ?? Task.FromResult(default(Barbican.Core.Common.Result<Barbican.Core.Common.Unit>))).ConfigureAwait(true));

    private static TextBlock Sub(string text) => new() { Text = text, FontSize = 14, Foreground = ContactsUi.Palette("Dim"), TextTrimming = TextTrimming.CharacterEllipsis };

    // button.tsx outline xs: h-8 px-2.5 text-sm font-medium, a hairline, gap 1.5
    private static Button Outline(string id, string label, string icon, Func<bool> run, bool enabled, CornerRadius radius)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new SIcon { Kind = icon, Size = 16, StrokeWidth = 2 });
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Style = (Style)Application.Current.Resources["Button.Action"], Height = 32, CornerRadius = radius, Padding = new Thickness(10, 0, 10, 0), FontSize = 14, FontWeight = FontWeights.Medium, Content = row, IsEnabled = enabled };
        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => _ = run();
        return button;
    }

    // toolbar.tsx ToolbarButton: a muted 32 px icon button
    private static Button Toolbar(string id, string label, string icon, Func<bool> run, CornerRadius radius)
    {
        var button = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = 32, Height = 32, CornerRadius = radius, Content = new SIcon { Kind = icon, Size = 16, StrokeWidth = 2 } };
        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => _ = run();
        return button;
    }

    private static Button Toolbar(string id, string label, string icon, Action run, CornerRadius radius) => Toolbar(id, label, icon, () =>
    {
        run();
        return true;
    }, radius);

    private static Button Outline(string id, string label, string icon, Action run, bool enabled, CornerRadius radius) => Outline(id, label, icon, () =>
    {
        run();
        return true;
    }, enabled, radius);
}

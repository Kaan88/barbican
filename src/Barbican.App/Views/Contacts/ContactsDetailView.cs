// Mirrors: Rust app.slint:5024-5109 (the contact detail), pim-contacts.slint ContactCardView, Bulwark components/contacts/contact-detail.tsx (avatar, name, organization, write an email, edit, delete; sections EMAIL, PHONE, ADDRESS, dates, related people, notes; categories); B-CON-002, B-CON-013, B-CON-026, B-CON-027, G-011;
// spec/layout/contacts.md (photo 88 squircle or circle, detail padding 24/26). Draws the lines of ContactsViewModel.DetailLines; every button is a registered command.
// The activity tiles of B-CON-026 (recent mail, files and the next event of the contact) are not drawn: they need the mail store and IFileStore reads that the data streams have not exposed for a contact yet (deferred, see the Fable section).

using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Contacts;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;

#pragma warning disable MA0051 // a drawing routine builds one frame in one pass (no flicker); splitting it would only pass the same locals around
namespace Barbican.App.Views.Contacts;

/// <summary>The detail pane of the open contact.</summary>
public sealed class ContactsDetailView : UserControl
{
    private readonly ContactsViewModel _vm = ContactsUi.Model;
    private readonly StyleState _look = ViewHost.Get<StyleState>();
    private readonly StackPanel _body = new() { Spacing = 14, Padding = new Thickness(26, 24, 26, 24) };

    public ContactsDetailView()
    {
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body };
        AutomationProperties.SetAutomationId(this, "contacts.detail");
        AutomationProperties.SetName(this, "Contact");
        _vm.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(Render);
        _look.PropertyChanged += (_, _) => Render();
        Render();
    }

    /// <summary>Redraws the pane from the model.</summary>
    public void Render()
    {
        _body.Children.Clear();
        if (_vm.OpenCard is not { } card)
        {
            _body.Children.Add(EmptyState());
            return;
        }

        var name = ContactsListRules.FullName(card);
        var isGroup = ContactsJson.IsGroup(card);
        var readOnly = _vm.Place == ContactsPlace.Directory;
        var email = ContactsJson.Emails(card).FirstOrDefault() ?? string.Empty;
        var bento = _look.IsBento;

        var header = new Grid { ColumnSpacing = 18, ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } } };
        var tint = ContactsAvatar.Tint(name);
        var photo = ContactsAvatar.Photo(ContactsJson.PhotoUri(card));
        var avatar = new Border
        {
            Width = 88,
            Height = 88,
            CornerRadius = ContactsAvatar.Radius(bento, 88),
            Background = photo is not null ? photo : tint.Background,
            Child = photo is null ? new TextBlock { Text = ContactsListRules.Initials(name, email), FontSize = 28, FontWeight = FontWeights.Bold, Foreground = tint.Foreground, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : null,
        };
        AutomationProperties.SetAutomationId(avatar, "contacts.detail.avatar");
        header.Children.Add(avatar);
        if (photo is null && !isGroup)
        {
            LoadBlobPhoto(avatar, card);
        }

        var titles = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = name, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = ContactsUi.Palette("Text"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        AutomationProperties.SetAutomationId(titles, "contacts.detail.name");
        var role = string.Join(" · ", new[] { ContactsJson.FirstOf(card["titles"], "name"), ContactsJson.FirstOf(card["organizations"], "name") }.Where(t => t.Length > 0));
        if (role.Length > 0)
        {
            titles.Children.Add(new TextBlock { Text = role, FontSize = 13.5, Foreground = ContactsUi.Palette("Dim"), TextWrapping = TextWrapping.Wrap });
        }

        if (isGroup)
        {
            titles.Children.Add(new TextBlock { Text = "Group · " + _vm.Contacts.Count(c => ContactsJson.HasMember(card, c)) + " members", FontSize = 12.5, Foreground = ContactsUi.Palette("Faint") });
        }

        Grid.SetColumn(titles, 1);
        header.Children.Add(titles);
        _body.Children.Add(header);

        var id = ContactsJson.Id(card);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(Action("contacts.detail.email", "Send email", ContactsCommandIds.SendEmail, id, primary: true, enabled: email.Length > 0));
        if (!readOnly)
        {
            actions.Children.Add(Action("contacts.detail.edit", isGroup ? "Edit members" : "Edit", isGroup ? ContactsCommandIds.GroupEdit : ContactsCommandIds.Edit, id, primary: false, enabled: true));
            actions.Children.Add(Action("contacts.detail.print", "Print", ContactsCommandIds.Print, id, primary: false, enabled: true));
            actions.Children.Add(Action("contacts.detail.delete", "Delete", isGroup ? ContactsCommandIds.GroupDelete : ContactsCommandIds.Delete, id, primary: false, enabled: true, danger: true));
        }

        _body.Children.Add(actions);

        var keywords = ContactsJson.Keys(card, "keywords");
        if (keywords.Count > 0)
        {
            var chips = new ChipRow();
            foreach (var k in keywords)
            {
                var chip = new Border { Background = ContactsUi.Palette("Chip"), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 6), Child = new TextBlock { Text = k, FontSize = 12, Foreground = ContactsUi.Palette("Dim") } };
                chips.Children.Add(chip);
            }

            _body.Children.Add(chips);
        }

        var section = string.Empty;
        StackPanel? tile = null;
        foreach (var line in _vm.DetailLines)
        {
            if (!string.Equals(line.Section, section, StringComparison.Ordinal))
            {
                section = line.Section;
                tile = new StackPanel { Spacing = 6 };
                tile.Children.Add(new TextBlock { Text = section, FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = ContactsUi.Palette("Faint") });
                _body.Children.Add(Wrap(tile, bento));
            }

            var row = new StackPanel { Spacing = 0 };
            if (line.Label.Length > 0)
            {
                row.Children.Add(new TextBlock { Text = line.Label, FontSize = 11.5, Foreground = ContactsUi.Palette("Faint") });
            }

            var value = new TextBlock { Text = line.Value, FontSize = 13.5, Foreground = ContactsUi.Palette("Body"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            if (string.Equals(line.Section, "PHONE", StringComparison.Ordinal) && ContactsJson.TelUri(line.Value) is { } tel)
            {
                // a phone number is a tel: link (I-205): the system hands it to the phone app the user chose; only plain digits ever leave the app
                var call = new HyperlinkButton { Content = "Call", Padding = new Thickness(6, 0, 6, 0), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                AutomationProperties.SetAutomationId(call, "contacts.detail.call");
                AutomationProperties.SetName(call, "Call " + line.Value);
                call.Click += (_, _) => ContactsUi.Run(async () => _ = await (ViewHost.Find<Barbican.Platform.IBrowserLauncher>()?.OpenAsync(tel, CancellationToken.None) ?? Task.FromResult(default(Barbican.Core.Common.Result<Barbican.Core.Common.Unit>))).ConfigureAwait(true));
                var withCall = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                withCall.Children.Add(value);
                withCall.Children.Add(call);
                row.Children.Add(withCall);
            }
            else
            {
                row.Children.Add(value);
            }

            tile!.Children.Add(row);
        }

        if (isGroup)
        {
            var members = _vm.Contacts.Where(c => ContactsJson.HasMember(card, c)).ToList();
            var tileM = new StackPanel { Spacing = 6 };
            tileM.Children.Add(new TextBlock { Text = "MEMBERS", FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = ContactsUi.Palette("Faint") });
            foreach (var m in members)
            {
                var memberId = ContactsJson.Id(m);
                var link = new HyperlinkButton { Content = ContactsListRules.FullName(m), Padding = new Thickness(0) };
                AutomationProperties.SetAutomationId(link, "contacts.detail.member." + memberId);
                link.Click += (_, _) =>
                {
                    _vm.ShowPlace(ContactsPlace.All, null);
                    _vm.SelectContact(memberId, false);
                    _vm.Open(memberId);
                };
                tileM.Children.Add(link);
            }

            _body.Children.Add(Wrap(tileM, bento));
        }

    }

    private static StackPanel EmptyState()
    {
        var panel = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 120, 0, 0) };
        panel.Children.Add(new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(20), BorderBrush = ContactsUi.Palette("Line"), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, Child = new Barbican.App.Controls.SIcon { Kind = "User", Size = 26, Foreground = ContactsUi.Palette("Faint"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        panel.Children.Add(new TextBlock { Text = "Select a contact to see the details.", FontSize = 13, Foreground = ContactsUi.Palette("Faint"), HorizontalAlignment = HorizontalAlignment.Center });
        AutomationProperties.SetAutomationId(panel, "contacts.detail.empty");
        return panel;
    }

    private Border Wrap(UIElement content, bool bento) => new()
    {
        Child = content,
        Background = bento ? _look.TileBrush : null,
        BorderBrush = bento ? ContactsUi.Palette("Line") : null,
        BorderThickness = new Thickness(bento ? 1 : 0),
        CornerRadius = bento ? new CornerRadius(18) : new CornerRadius(0),
        Padding = bento ? new Thickness(16, 12, 16, 12) : new Thickness(0),
    };

    private static Button Action(string id, string label, string command, string argument, bool primary, bool enabled, bool danger = false)
    {
        var button = new Button { Style = (Style)Application.Current.Resources[primary ? "Button.Primary" : "Button.Action"], Content = label, Height = 34, CornerRadius = new CornerRadius(17), FontSize = 13, IsEnabled = enabled };
        if (danger)
        {
            button.Foreground = ContactsUi.Palette("RoseFg");
        }

        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => _ = ContactsUi.RunCommand(command, argument);
        return button;
    }

    /// <summary>A wrapping row of chips (a plain panel that lays children left to right and starts a new line when the row is full).</summary>
    private sealed class ChipRow : Panel
    {
        protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size availableSize)
        {
            double x = 0, y = 0, rowHeight = 0, width = 0;
            foreach (var child in Children)
            {
                child.Measure(new Windows.Foundation.Size(availableSize.Width, double.PositiveInfinity));
                var d = child.DesiredSize;
                if (x > 0 && x + d.Width > availableSize.Width)
                {
                    y += rowHeight;
                    x = 0;
                    rowHeight = 0;
                }

                x += d.Width;
                width = Math.Max(width, x);
                rowHeight = Math.Max(rowHeight, d.Height);
            }

            return new Windows.Foundation.Size(width, y + rowHeight);
        }

        protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size finalSize)
        {
            double x = 0, y = 0, rowHeight = 0;
            foreach (var child in Children)
            {
                var d = child.DesiredSize;
                if (x > 0 && x + d.Width > finalSize.Width)
                {
                    y += rowHeight;
                    x = 0;
                    rowHeight = 0;
                }

                child.Arrange(new Windows.Foundation.Rect(x, y, d.Width, d.Height));
                x += d.Width;
                rowHeight = Math.Max(rowHeight, d.Height);
            }

            return finalSize;
        }
    }

    /// <summary>A photo held as a blob is downloaded through IContactPhotos (never a web link) and shown when it arrives, if the same card is still open (B-CON-014, D-M5-I02).</summary>
    private void LoadBlobPhoto(Border avatar, System.Text.Json.Nodes.JsonNode card)
    {
        if (ViewHost.Find<Barbican.Core.Contacts.IContactPhotos>() is not { } photos || _vm.AccountKey is not { } key || !HasBlobPhoto(card))
        {
            return;
        }

        var id = ContactsJson.Id(card);
        ContactsUi.Run(async () =>
        {
            var bytes = await photos.BytesAsync(key, card, CancellationToken.None).ConfigureAwait(true);
            if (!bytes.IsOk || !string.Equals(_vm.OpenCard is { } open ? ContactsJson.Id(open) : null, id, StringComparison.Ordinal))
            {
                return;
            }

            var brush = ContactsAvatar.Photo("data:" + bytes.Value.MediaType + ";base64," + Convert.ToBase64String(bytes.Value.Bytes));
            if (brush is not null)
            {
                avatar.Background = brush;
                avatar.Child = null;
            }
        });
    }

    private static bool HasBlobPhoto(System.Text.Json.Nodes.JsonNode card) =>
        card["media"] is System.Text.Json.Nodes.JsonObject media && media.Any(m => string.Equals(ContactsJson.Str(m.Value, "kind"), "photo", StringComparison.Ordinal) && ContactsJson.Str(m.Value, "blobId").Length > 0);
}

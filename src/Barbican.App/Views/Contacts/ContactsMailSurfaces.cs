// Mirrors: Bulwark components/email/email-viewer.tsx:587-751 (the reader's contact sidebar: the card of the sender with copy, edit and add), recipient-popover.tsx:123, :156 (View contact, phone numbers); I-193, I-231; M5 plan section 6.2 step 3 and D-M5-19 (the mail-side surfaces read contacts only through IContactLookup).
// The card shows under the sender in the reader's sender panel: the sender's contact (name, organization, phones and email with a Copy button each, View contact, Edit) or an "Add to contacts" button. A phone number is copied, not dialled: a tel: link would hand the number to another program (I-205's tel: links are deferred, see the Fable section).

using Barbican.App.Shell;
using Barbican.Core.Common;
using Barbican.Core.Contacts;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Common;
using Barbican.Presentation.Contacts;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Contacts;

/// <summary>The contact card of the reader's sender panel.</summary>
public static class ContactsMailSurfaces
{

    /// <summary>Fills <paramref name="panel"/> for the sender <paramref name="email"/> (cleared when there is none); the lookup is asynchronous and a late answer for another sender is dropped.</summary>
    public static void FillSender(StackPanel panel, string email)
    {
        ArgumentNullException.ThrowIfNull(panel);
        panel.Tag = email ?? string.Empty;
        panel.Children.Clear();
        panel.Visibility = Visibility.Collapsed;
        if (string.IsNullOrWhiteSpace(email) || ViewHost.Find<IContactLookup>() is not { } lookup)
        {
            return;
        }

        ContactsUi.Run(async () =>
        {
            ContactMatch? match;
            try
            {
                match = await lookup.FindByEmailAsync(email, CancellationToken.None).ConfigureAwait(true);
            }
            catch (PortPendingException)
            {
                // con-data's lookup has not landed: the card offers adding the sender
                match = null;
            }

            if (!string.Equals(panel.Tag as string, email, StringComparison.Ordinal))
            {
                return;
            }

            panel.Children.Clear();
            Build(panel, email, match);
            panel.Visibility = Visibility.Visible;
        });
    }

    private static void Build(StackPanel panel, string email, ContactMatch? match)
    {
        if (match is null)
        {
            panel.Children.Add(Button("Add to contacts", "context.contact.add", () => ContactsUi.RunCommand(CommandIds.AddToContacts, email)));
            return;
        }

        var card = match.Card;
        panel.Children.Add(new TextBlock { Text = "CONTACT", FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = ContactsUi.Palette("Faint") });
        panel.Children.Add(new TextBlock { Text = ContactsListRules.FullName(card), FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = ContactsUi.Palette("Text"), TextWrapping = TextWrapping.Wrap });
        if (ContactsJson.FirstOf(card["organizations"], "name") is { Length: > 0 } org)
        {
            panel.Children.Add(new TextBlock { Text = org, FontSize = 12.5, Foreground = ContactsUi.Palette("Dim"), TextWrapping = TextWrapping.Wrap });
        }

        foreach (var phone in ContactsJson.All(card["phones"], "number"))
        {
            panel.Children.Add(CopyRow(phone, "context.contact.phone"));
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(Button("View contact", "context.contact.view", () => ContactsUi.RunCommand(CommandIds.ViewContact, email)));
        row.Children.Add(Button("Edit", "context.contact.edit", () => ContactsUi.RunCommand(ContactsCommandIds.Edit, ContactsJson.Id(card))));
        panel.Children.Add(row);
    }

    /// <summary>The name popover of the reader (I-204): the sender's contact card (or Add to contacts) in the reader's in-window flyout (declared in XAML, constrained to the window) under <paramref name="anchor"/>.</summary>
    public static void ShowPopover(Flyout flyout, StackPanel card, FrameworkElement anchor, string email)
    {
        ArgumentNullException.ThrowIfNull(flyout);
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(anchor);
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        FillSender(card, email);
        flyout.ShowAt(anchor);
    }

    private static Grid CopyRow(string value, string id)
    {
        var grid = new Grid { ColumnSpacing = 6, ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = GridLength.Auto } } };
        grid.Children.Add(new TextBlock { Text = value, FontSize = 12.5, Foreground = ContactsUi.Palette("Body"), VerticalAlignment = VerticalAlignment.Center, IsTextSelectionEnabled = true, TextTrimming = TextTrimming.CharacterEllipsis });
        var copy = Button("Copy", id + ".copy", () => ViewHost.Find<IClipboardService>()?.SetText(value));
        Grid.SetColumn(copy, 1);
        grid.Children.Add(copy);
        if (ContactsJson.TelUri(value) is { } tel)
        {
            var call = Button("Call", id + ".call", () => ContactsUi.Run(async () => _ = await (ViewHost.Find<Barbican.Platform.IBrowserLauncher>()?.OpenAsync(tel, CancellationToken.None) ?? Task.FromResult(default(Barbican.Core.Common.Result<Barbican.Core.Common.Unit>))).ConfigureAwait(true)));
            Grid.SetColumn(call, 2);
            grid.Children.Add(call);
        }

        return grid;
    }

    private static Button Button(string label, string id, Action click)
    {
        var button = new Button { Style = (Style)Application.Current.Resources["Button.Action"], Content = label, Height = 28, CornerRadius = new CornerRadius(10), FontSize = 12, Padding = new Thickness(10, 0, 10, 0), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => click();
        return button;
    }
}

// Bulwark components/email/email-viewer.tsx:963-989, 1098-1108. Display only headers present on this message.
using System.Globalization;
using Barbican.App.Shell;
using Barbican.Core.Mail.Model;
using Barbican.Core.Settings.Pages;
using Barbican.Core.Storage;
using Barbican.Presentation.Mail;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Reader;

public sealed partial class ConversationCardView
{
    private static Expander Details(ConversationExpansion card, EmailBody body)
    {
        var prefs = ViewHost.Get<IPrefsStore>().Current;
        var zone = SettingDisplayTimeZone.Resolve(prefs.Str("timeZone", "auto"), ViewHost.Get<TimeProvider>().LocalTimeZone);
        var culture = CultureInfo.CurrentCulture;
        var clock = string.Equals(prefs.Str("timeFormat", "24h"), "12h", StringComparison.Ordinal) ? "h:mm tt" : "HH:mm";
        string? Date(DateTimeOffset? instant) => instant is { } value
            ? TimeZoneInfo.ConvertTime(value, zone).ToString(culture.DateTimeFormat.LongDatePattern + " " + clock, culture) : null;
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 4, HorizontalAlignment = HorizontalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Add(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = new TextBlock { Text = label, FontSize = 12, Opacity = 0.75 };
            var field = new TextBlock { Text = value, FontSize = 12, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            Grid.SetRow(name, row);
            Grid.SetRow(field, row);
            Grid.SetColumn(field, 1);
            AutomationProperties.SetAutomationId(field, "reader.thread.field." + card.EmailId + "." + label);
            AutomationProperties.SetName(field, label + ": " + value);
            grid.Children.Add(name);
            grid.Children.Add(field);
        }

        Add("From", Addresses(body.From));
        Add("Reply-To", Addresses(body.ReplyTo));
        Add("To", Addresses(body.To));
        Add("Cc", Addresses(body.Cc));
        Add("Bcc", Addresses(body.Bcc));
        Add("Sent", Date(body.SentAt));
        Add("Received", Date(body.ReceivedAt));
        Add("Subject", body.Subject);
        Add("Message-ID", string.Join(", ", body.MessageId));
        var recipients = string.Join(", ", body.To.Select(a => string.IsNullOrWhiteSpace(a.Name) ? a.Email : a.Name));
        var summary = string.IsNullOrWhiteSpace(recipients) ? "Message details" : "To " + recipients;
        var expander = new Expander
        {
            Header = new TextBlock { Text = summary, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis },
            Content = grid, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsExpanded = card.DetailsExpanded,
        };
        AutomationProperties.SetAutomationId(expander, "reader.thread.details." + card.EmailId);
        AutomationProperties.SetName(expander, recipients.Length == 0 ? "Message details" : "Message details. " + summary);
        AutomationProperties.SetAutomationId(grid, "reader.thread.details.fields." + card.EmailId);
        expander.Expanding += (_, _) => card.DetailsExpanded = true;
        expander.Collapsed += (_, _) => card.DetailsExpanded = false;
        return expander;
    }

    private static string Addresses(IReadOnlyList<EmailAddress> list) => string.Join(", ", list.Select(a =>
        string.IsNullOrWhiteSpace(a.Name) ? a.Email : string.IsNullOrWhiteSpace(a.Email) ? a.Name : $"{a.Name} <{a.Email}>"));
}

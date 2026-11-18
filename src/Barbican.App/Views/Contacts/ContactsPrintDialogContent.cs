// Mirrors: Bulwark components/contacts/contact-print*.tsx; G-010; D-M5-12. The in-window preview of ContactsPrintModel (pages on white paper in black ink, whatever the theme) with a Print button. The OS print path (Windows: the print dialog over these same pages) is built but never run by an agent
// or a test (printing never reaches a printer in a test); on the Linux head the button is greyed. It reuses the calendar's CalendarPrinter, which prints any list of pages.

using Barbican.Presentation.Compose;
using Barbican.Presentation.Contacts;
using Barbican.Presentation.Dialogs;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Windows.UI;
using static Barbican.App.Views.Calendar.Editors.CalendarCardKit;

#pragma warning disable MA0051 // a page is drawn in one pass
namespace Barbican.App.Views.Contacts;

/// <summary>Draws <see cref="ContactsPrintRequest"/>.</summary>
public sealed class ContactsPrintDialogContent : Barbican.App.Views.Dialogs.IDialogContent
{
    private const double PageWidth = 560;
    private const double PageHeight = 760;
    private static readonly SolidColorBrush Paper = new(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush Ink = new(Color.FromArgb(0xFF, 0x11, 0x11, 0x11));
    private static readonly SolidColorBrush Faint = new(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
    private static readonly SolidColorBrush Rule = new(Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC));

    public bool OwnButtons => true;

    public double MaxWidth => 640;

    public bool CanDraw(object request) => request is ContactsPrintRequest;

    public UIElement Create(ActiveDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var document = ((ContactsPrintRequest)dialog.Request).Document;
        var panel = Card("contacts.print", "Print " + document.Title, 560);
        panel.Children.RemoveAt(0); // the dialog host shows the title
        var pages = new StackPanel { Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var page in document.Pages)
        {
            pages.Children.Add(Sheet(page));
        }

        panel.Children.Add(new ScrollViewer { Content = pages, MaxHeight = 520, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var print = Btn("Print", DialogTone.Primary, "contacts.print.go", () => ContactsUi.Run(() => Calendar.CalendarPrinter.PrintAsync(document.Title, Sheets(document))), enabled: Calendar.CalendarPrinter.Available);
        panel.Children.Add(Buttons(Btn("Close", DialogTone.Plain, "contacts.print.close", () => dialog.Complete(false)), print));
        AutomationProperties.SetName(panel, "Print preview");
        return panel;
    }

    // the printer gets its own copies of the pages (an element cannot sit in the preview and in the print job at once)
    private static List<FrameworkElement> Sheets(ContactsPrintDocument document) => [.. document.Pages.Select(Sheet)];

    private static Border Sheet(ContactsPrintPage page)
    {
        var column = new StackPanel { Spacing = 14 };
        foreach (var block in page.Blocks)
        {
            var one = new StackPanel { Spacing = 2 };
            one.Children.Add(new TextBlock { Text = block.Name, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Ink });
            if (block.Subtitle.Length > 0)
            {
                one.Children.Add(new TextBlock { Text = block.Subtitle, FontSize = 12.5, Foreground = Faint });
            }

            one.Children.Add(new Border { Height = 1, Background = Rule, Margin = new Thickness(0, 4, 0, 4) });
            var section = string.Empty;
            foreach (var line in block.Lines)
            {
                if (!string.Equals(line.Section, section, StringComparison.Ordinal))
                {
                    section = line.Section;
                    one.Children.Add(new TextBlock { Text = section, FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Faint, Margin = new Thickness(0, 4, 0, 0) });
                }

                one.Children.Add(new TextBlock { Text = (line.Label.Length > 0 ? line.Label + ": " : string.Empty) + line.Value, FontSize = 12.5, Foreground = Ink, TextWrapping = TextWrapping.Wrap });
            }

            column.Children.Add(one);
        }

        return new Border { Width = PageWidth, Height = PageHeight, Background = Paper, Padding = new Thickness(36), Child = column };
    }
}

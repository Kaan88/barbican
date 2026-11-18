// Mirrors: Bulwark components/calendar/calendar-print.tsx (window.print); D-M4-14. The OS print path behind the print preview's Print button: Windows only (the print dialog over the preview's own pages).
// It is built but never run by an agent or a test (plan section 6.3 step 3: printing never reaches a printer in a test); on the Linux head there is no print dialog yet and the button is greyed.

namespace Barbican.App.Views.Calendar;

/// <summary>Sends the preview's pages to the operating system's print dialog.</summary>
internal static class CalendarPrinter
{
#if WINDOWS
    public static bool Available => true;

    public static async Task PrintAsync(string title, IReadOnlyList<FrameworkElement> pages)
    {
        var window = Barbican.App.Shell.ActiveWindow.Current;
        if (window is null || pages.Count == 0)
        {
            return;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var document = new Microsoft.UI.Xaml.Printing.PrintDocument();
        document.Paginate += (_, _) => document.SetPreviewPageCount(pages.Count, Microsoft.UI.Xaml.Printing.PreviewPageCountType.Final);
        document.GetPreviewPage += (_, e) => document.SetPreviewPage(e.PageNumber, pages[Math.Clamp(e.PageNumber - 1, 0, pages.Count - 1)]);
        document.AddPages += (_, _) =>
        {
            foreach (var page in pages)
            {
                document.AddPage(page);
            }

            document.AddPagesComplete();
        };
        var manager = Windows.Graphics.Printing.PrintManagerInterop.GetForWindow(hwnd);
        void OnRequested(Windows.Graphics.Printing.PrintManager sender, Windows.Graphics.Printing.PrintTaskRequestedEventArgs args)
        {
            _ = args.Request.CreatePrintTask(title, source => source.SetSource(document.DocumentSource));
        }

        manager.PrintTaskRequested += OnRequested;
        try
        {
            _ = await Windows.Graphics.Printing.PrintManagerInterop.ShowPrintUIForWindowAsync(hwnd);
        }
        finally
        {
            manager.PrintTaskRequested -= OnRequested;
        }
    }
#else
    public static bool Available => false;

    public static Task PrintAsync(string title, IReadOnlyList<FrameworkElement> pages) => Task.CompletedTask;
#endif
}

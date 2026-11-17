using System.Globalization;
using Barbican.Automation.Protocol;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Barbican.Automation;

/// <summary>
/// <c>popup-probe KIND [free]</c>: opens one popup-owning control in code, reads whether XAML keeps
/// its popup inside the window, lets the guard act, and removes the control again. KIND is one of
/// menuflyout, flyout, commandbarflyout, popup, teachingtip, combobox, tooltip, autosuggest,
/// contentdialog; <c>free</c> leaves <c>ShouldConstrainToRootBounds</c> at false where the control
/// has it (the guard test). It opens popups that may be windowed, so it is run ONLY with the app on a
/// private desktop (tools/auto-run.ps1 -PrivateDesktop), where no window can reach the user's screen.
/// Reply: <c>probe KIND constrained=yes|no|none guard-closed=N</c>, or the guard's error.
/// </summary>
internal sealed partial class CommandRouter
{
    private async Task<string> PopupProbeAsync(CommandLine line)
    {
        var kind = line.Token(0).ToLowerInvariant();
        var free = string.Equals(line.Token(1), "free", StringComparison.Ordinal);
        var host = ElementSearch.First<Panel>(_context.Root);
        if (host is null)
        {
            return "error no panel to host the probe";
        }

        var anchor = new Button { Content = "probe", HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(40) };
        host.Children.Add(anchor);
        await Task.Delay(100);
        var closedBefore = _guard.Closed;
        string? constrained = null;
        Func<Task>? cleanup = null;
        try
        {
            switch (kind)
            {
                case "menuflyout":
                case "flyout":
                case "commandbarflyout":
                {
                    FlyoutBase fl = kind switch
                    {
                        "menuflyout" => new MenuFlyout { Items = { new MenuFlyoutItem { Text = "one" }, new MenuFlyoutItem { Text = "two" } } },
                        "flyout" => new Flyout { Content = new TextBlock { Text = "flyout" } },
                        _ => new CommandBarFlyout { PrimaryCommands = { new AppBarButton { Label = "one" } } },
                    };
#if WINDOWS
                    fl.ShouldConstrainToRootBounds = !free;
#endif
                    fl.ShowAt(anchor);
                    cleanup = () => { fl.Hide(); return Task.CompletedTask; };
                    break;
                }

                case "popup":
                {
                    var p = new Popup { Child = new Border { Width = 80, Height = 40, Background = new SolidColorBrush(Microsoft.UI.Colors.Gray) }, XamlRoot = _context.Root!.XamlRoot };
#if WINDOWS
                    p.ShouldConstrainToRootBounds = !free;
#endif
                    p.IsOpen = true;
                    cleanup = () => { p.IsOpen = false; return Task.CompletedTask; };
                    break;
                }

                case "teachingtip":
                {
                    var tip = new TeachingTip { Title = "tip", Target = anchor };
#if WINDOWS
                    tip.ShouldConstrainToRootBounds = !free;
#endif
                    host.Children.Add(tip);
                    tip.IsOpen = true;
                    cleanup = () => { tip.IsOpen = false; host.Children.Remove(tip); return Task.CompletedTask; };
                    break;
                }

                case "combobox":
                {
                    var box = new ComboBox { Items = { "one", "two", "three" }, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(40, 100, 0, 0) };
                    host.Children.Add(box);
                    await Task.Delay(100);
                    box.IsDropDownOpen = true;
                    cleanup = () => { box.IsDropDownOpen = false; host.Children.Remove(box); return Task.CompletedTask; };
                    break;
                }

                case "tooltip":
                {
                    var tt = new ToolTip { Content = "tooltip" };
                    ToolTipService.SetToolTip(anchor, tt);
                    tt.IsOpen = true;
                    cleanup = () => { tt.IsOpen = false; return Task.CompletedTask; };
                    break;
                }

                case "autosuggest":
                {
                    var box = new AutoSuggestBox { ItemsSource = new[] { "one", "two", "three" }, Width = 200, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(40, 100, 0, 0) };
                    host.Children.Add(box);
                    await Task.Delay(100);
                    box.IsSuggestionListOpen = true;
                    cleanup = () => { box.IsSuggestionListOpen = false; host.Children.Remove(box); return Task.CompletedTask; };
                    break;
                }

                case "contentdialog":
                {
                    var dialog = new ContentDialog { Title = "dialog", CloseButtonText = "close", XamlRoot = _context.Root!.XamlRoot };
                    _ = dialog.ShowAsync();
                    cleanup = () => { dialog.Hide(); return Task.CompletedTask; };
                    break;
                }

                default:
                    return "error unknown probe kind " + kind;
            }

            // Read the popups as XAML opened them, before the guard's timer can act.
            constrained = ReadConstrained();
            await Task.Delay(300);
            constrained ??= ReadConstrained();
        }
        finally
        {
            if (cleanup is not null)
            {
                await cleanup();
            }

            await Task.Delay(100);
            host.Children.Remove(anchor);
        }

        return string.Create(CultureInfo.InvariantCulture, $"probe {kind} constrained={constrained ?? "none"} guard-closed={_guard.Closed - closedBefore}");
    }

    private string? ReadConstrained()
    {
        if (_context.Root?.XamlRoot is not { } xr)
        {
            return null;
        }

        var popups = VisualTreeHelper.GetOpenPopupsForXamlRoot(xr);
        if (popups.Count == 0)
        {
            return null;
        }

#if WINDOWS
        return popups.All(p => p.IsConstrainedToRootBounds) ? "yes" : "no";
#else
        return "yes";
#endif
    }
}

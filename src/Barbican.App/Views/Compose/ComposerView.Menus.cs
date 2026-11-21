// Mirrors: Bulwark components/email/composer-toolbar-menu.tsx:34-66 (a toolbar button opens a menu above it), email-composer.tsx:6463-6475 (Templates menu: insert, save as template) and :6508-6600 (Options menu:
// plain text, read receipt, priority group), ContextMenu rows h-8 rounded (MenuFlyout restyled by FluentOverrides.xaml, ADR 0007: the flyout stays in the window).
// In Bulwark the Templates and Options buttons open an anchored menu; Bento and Nordic keep their cards.

using Barbican.Core.Compose;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Barbican.App.Views.Compose;

public sealed partial class ComposerView
{
    private bool Bulwark => _theme.Applied?.Bulwark == true;

    // The two menus are declared in ComposerView.xaml (Button.Flyout, ShouldConstrainToRootBounds) because the popup lint (ADR 0007) forbids code-created popups;
    // the code only syncs the checked state and shows them.
    private void ShowTemplatesMenu()
    {
        if (_vm is not null)
        {
            TemplatesMenu.ShowAt(TemplatesButton);
        }
    }

    private void OnTemplatesMenuItem(object sender, RoutedEventArgs e) => _ = _vm?.OpenTemplatesAsync();

    private void ShowOptionsMenu()
    {
        if (_vm is not { } vm)
        {
            return;
        }

        OptionsPlain.IsChecked = vm.PlainText;
        OptionsReceipt.IsChecked = vm.ReadReceipt;
        OptionsHigh.IsChecked = vm.Priority == MessagePriority.High;
        OptionsNormal.IsChecked = vm.Priority == MessagePriority.Normal;
        OptionsLow.IsChecked = vm.Priority == MessagePriority.Low;
        OptionsMenu.ShowAt(OptionsButton);
    }

    private void OnOptionsPlain(object sender, RoutedEventArgs e)
    {
        if (_vm is { } vm)
        {
            vm.PlainText = OptionsPlain.IsChecked;
        }
    }

    private void OnOptionsReceipt(object sender, RoutedEventArgs e)
    {
        if (_vm is { } vm)
        {
            vm.ReadReceipt = OptionsReceipt.IsChecked;
        }
    }

    private void OnOptionsPriority(object sender, RoutedEventArgs e)
    {
        if (_vm is { } vm)
        {
            vm.Priority = ReferenceEquals(sender, OptionsHigh) ? MessagePriority.High : ReferenceEquals(sender, OptionsLow) ? MessagePriority.Low : MessagePriority.Normal;
        }
    }
}

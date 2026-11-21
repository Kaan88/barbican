// Mirrors: Bulwark components/email/composer-toolbar-menu.tsx:34-66 (a toolbar button opens a menu above it), email-composer.tsx:6463-6475 (Templates menu: insert, save as template) and :6508-6600 (Options menu:
// plain text, read receipt, priority group), ContextMenu rows h-8 rounded (MenuFlyout restyled by FluentOverrides.xaml, ADR 0007: the flyout stays in the window).
// In Bulwark the Templates and Options buttons open an anchored menu; Bento and Nordic keep their cards.

using Barbican.Core.Compose;
using Microsoft.UI.Xaml.Automation;
using Windows.Foundation;

namespace Barbican.App.Views.Compose;

public sealed partial class ComposerView
{
    private bool Bulwark => _theme.Applied?.Bulwark == true;

    private MenuFlyout NewToolbarMenu()
    {
        var flyout = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.TopEdgeAlignedLeft };
#if WINDOWS
        flyout.ShouldConstrainToRootBounds = true;
#endif
        return flyout;
    }

    private void ShowTemplatesMenu()
    {
        if (_vm is not { } vm)
        {
            return;
        }

        var menu = NewToolbarMenu();
        var insert = new MenuFlyoutItem { Text = "Insert template", Icon = new FontIcon { Glyph = "" } };
        AutomationProperties.SetAutomationId(insert, "composer.templates.insert");
        insert.Click += (_, _) => _ = vm.OpenTemplatesAsync();
        var save = new MenuFlyoutItem { Text = "Save as template", Icon = new FontIcon { Glyph = "" } };
        AutomationProperties.SetAutomationId(save, "composer.templates.save");
        save.Click += (_, _) => _ = vm.OpenTemplatesAsync();
        menu.Items.Add(insert);
        menu.Items.Add(save);
        menu.ShowAt(TemplatesButton);
    }

    private void ShowOptionsMenu()
    {
        if (_vm is not { } vm)
        {
            return;
        }

        var menu = NewToolbarMenu();
        var plain = new ToggleMenuFlyoutItem { Text = "Plain text", IsChecked = vm.PlainText };
        AutomationProperties.SetAutomationId(plain, "composer.options.plain");
        plain.Click += (_, _) => vm.PlainText = plain.IsChecked;
        var receipt = new ToggleMenuFlyoutItem { Text = "Request a read receipt", IsChecked = vm.ReadReceipt };
        AutomationProperties.SetAutomationId(receipt, "composer.options.receipt");
        receipt.Click += (_, _) => vm.ReadReceipt = receipt.IsChecked;
        menu.Items.Add(plain);
        menu.Items.Add(receipt);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = "Priority", IsEnabled = false });
        foreach (var p in new[] { MessagePriority.High, MessagePriority.Normal, MessagePriority.Low })
        {
            var value = p;
            var item = new RadioMenuFlyoutItem { Text = p.ToString(), GroupName = "composer.priority", IsChecked = vm.Priority == p };
            AutomationProperties.SetAutomationId(item, "composer.options.priority." + p.ToString().ToLowerInvariant());
            item.Click += (_, _) => vm.Priority = value;
            menu.Items.Add(item);
        }

        menu.ShowAt(OptionsButton);
    }
}

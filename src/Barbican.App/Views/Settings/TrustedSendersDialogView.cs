// Mirrors: Bulwark components/trusted-senders-modal.tsx (the list with a Remove button per sender, the search from five senders, the
// "Add sender manually" row with its errors, the empty state and its description); the dialog card, scrim and focus handling are the
// shared host's (PORT-PLAN section 3.7). Owner reader, I-209.

using Barbican.App.Theme;
using Barbican.App.Views.Dialogs;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Settings;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Settings;

/// <summary>The content of the Trusted senders dialog: the list, the add row, its search and the Close button.</summary>
internal sealed class TrustedSendersDialogView : StackPanel
{
    private readonly ActiveDialog _dialog;
    private readonly TrustedSendersViewModel _vm;
    private readonly TextBox _search;
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly TextBlock _status;
    private readonly StackPanel _addArea = new() { Spacing = 6 };
    private TextBox? _field;

    internal TrustedSendersDialogView(ActiveDialog dialog, TrustedSendersViewModel vm)
    {
        _dialog = dialog;
        _vm = vm;
        Spacing = 10;
        MinWidth = 320;
        _search = new TextBox { PlaceholderText = "Search senders..." };
        AutomationProperties.SetAutomationId(_search, "trusted.senders.search");
        AutomationProperties.SetName(_search, "Search senders");
        _search.TextChanged += (_, _) =>
        {
            _vm.Query = _search.Text;
            RenderList();
        };
        _status = SetUiUi.Meta(string.Empty);
        AutomationProperties.SetAutomationId(_status, "trusted.senders.status");
        Children.Add(_search);
        Children.Add(_list);
        Children.Add(_status);
        Children.Add(_addArea);
        var close = SetUiUi.Pill("Close", () => dialog.Complete(true), "trusted.senders.close");
        DialogStyles.Dress(close);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        Children.Add(close);
        AutomationProperties.SetAutomationId(this, "trusted.senders.dialog");
        RenderAdd();
        RenderList();
    }

    /// <summary>The control that takes the keyboard focus: the address field while adding, else none.</summary>
    internal Control? FocusTarget => _field;

    // Bulwark trusted-senders-modal.tsx:256-276: the address with a Remove button; the list is drawn again once it is gone
    internal void RenderList()
    {
        _list.Children.Clear();
        if (_vm.IsEmpty)
        {
            _list.Children.Add(Empty());
        }
        else
        {
            for (var i = 0; i < _vm.Addresses.Count; i++)
            {
                _list.Children.Add(Row(_vm.Addresses[i], i));
            }
        }

        _status.Text = _vm.Error.Length > 0 ? _vm.Error : _vm.NoResults ? "No senders match your search" : string.Empty;
        _status.Foreground = _vm.Error.Length > 0 ? AppResources.Brush("Destructive") : AppResources.Brush("Muted");
        _search.Visibility = _vm.ShowSearch ? Visibility.Visible : Visibility.Collapsed;
    }

    private Grid Row(string address, int index)
    {
        var text = SetUiUi.Body(address);
        AutomationProperties.SetAutomationId(text, "trusted.senders.address." + index);
        var remove = SetUiUi.Pill("Remove", () => SetUiUi.Run(() => RemoveAsync(address)), "trusted.senders.remove." + index);
        remove.HorizontalAlignment = HorizontalAlignment.Right;
        AutomationProperties.SetName(remove, "Remove " + address);

        var grid = new Grid { ColumnSpacing = 8, Padding = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);
        Grid.SetColumn(remove, 1);
        grid.Children.Add(remove);
        return grid;
    }

    private async Task RemoveAsync(string address)
    {
        await _vm.RemoveAsync(address, CancellationToken.None).ConfigureAwait(true);
        RenderList();
    }

    // Bulwark trusted-senders-modal.tsx:165-203: a ghost "Add sender manually" that opens a field and its Add button
    private void RenderAdd()
    {
        _addArea.Children.Clear();
        if (!_vm.IsAdding)
        {
            _field = null;
            var add = SetUiUi.Pill("Add sender manually", () => { _vm.BeginAdd(); RenderAdd(); }, "trusted.senders.add");
            add.HorizontalAlignment = HorizontalAlignment.Left;
            _addArea.Children.Add(add);
            return;
        }

        var field = new TextBox { PlaceholderText = "Enter email address", Text = _vm.NewAddress, MinWidth = 240 };
        _field = field;
        AutomationProperties.SetAutomationId(field, "trusted.senders.new");
        AutomationProperties.SetName(field, "Enter email address");
        field.TextChanged += (_, _) => _vm.NewAddress = field.Text;
        field.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                SetUiUi.Run(SubmitAsync);
            }
        };
        var submit = SetUiUi.Pill("Add", () => SetUiUi.Run(SubmitAsync), "trusted.senders.submit", primary: true);
        var cancel = SetUiUi.Pill("Cancel", () => { _vm.CancelAdd(); RenderAdd(); RenderList(); }, "trusted.senders.cancel");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(field);
        row.Children.Add(submit);
        row.Children.Add(cancel);
        _addArea.Children.Add(row);
    }

    private async Task SubmitAsync()
    {
        if (await _vm.AddAsync(CancellationToken.None).ConfigureAwait(true))
        {
            RenderAdd();
        }

        RenderList();
    }

    // Bulwark trusted-senders-modal.tsx:238-252: no senders yet, and how one gets here
    private static StackPanel Empty()
    {
        var panel = new StackPanel { Spacing = 4, Padding = new Thickness(0, 12, 0, 12) };
        panel.Children.Add(new TextBlock { Text = "No trusted senders yet", Style = (Style)Application.Current.Resources["Text.Body"], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(SetUiUi.Meta("When viewing an email with blocked images, click \"Always trust this sender\" to add them here."));
        return panel;
    }
}

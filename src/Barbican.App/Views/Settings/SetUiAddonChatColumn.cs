// Mirrors: Rust assistant column, M6 plan section 4.4 and decision D-M6-B03; B-AI-006 (the conversation, Clear), B-AI-010 ("this email" is the message selected in the list); decision D-M6b-08: the generic chat panel any add-on that implements IPluginChat gets.
// The conversation is the add-on's, per account. Mounted inside the add-on's settings page (D-M6-B05: the shell's mail screen is not set-ui's file).

using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Settings.Addons;
using Microsoft.UI.Xaml.Automation;
using Windows.System;

#pragma warning disable MA0051 // a page is built top to bottom in one method, like its layout reads

namespace Barbican.App.Views.Settings;

/// <summary>The conversation with an add-on.</summary>
public sealed class SetUiAddonChatColumn : UserControl
{
    private readonly AddonChatViewModel _vm;
    private readonly string _emptyHint;
    private readonly StackPanel _messages = new() { Spacing = 8 };
    private readonly ScrollViewer _scroll;
    private readonly TextBox _input = new() { AcceptsReturn = false };
    private readonly TextBlock _status = SetUiUi.Meta(string.Empty);
    private readonly Button _send;

    public SetUiAddonChatColumn(string pluginId, AddonChatViewModel vm)
    {
        _vm = vm;
        _emptyHint = vm.Chat.EmptyHint;
        var id = "set.addon." + pluginId;
        _input.PlaceholderText = vm.Chat.InputPlaceholder;
        _scroll = SetUiUi.Scroll(_messages);
        _send = SetUiUi.Pill("Ask", () => SetUiUi.Run(SendAsync), id + ".send", primary: true);
        _ = SetUiUi.Id(_input, id + ".input");
        _ = SetUiUi.Id(_status, id + ".status");
        _ = SetUiUi.Id(_messages, id + ".messages");
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                SetUiUi.Run(SendAsync);
            }
        };
        var clear = SetUiUi.Pill("Clear", () => SetUiUi.Run(() => _vm.ClearAsync(CancellationToken.None)), id + ".clear");
        var bar = new Grid { ColumnSpacing = 8 };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.Children.Add(_input);
        Grid.SetColumn(_send, 1);
        bar.Children.Add(_send);
        Grid.SetColumn(clear, 2);
        bar.Children.Add(clear);
        var root = new Grid { RowSpacing = 8, Padding = new Thickness(12), BorderBrush = AppResources.Brush("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), MinHeight = 320 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(SetUiUi.Body(vm.Chat.Title));
        Grid.SetRow(_scroll, 1);
        root.Children.Add(_scroll);
        Grid.SetRow(_status, 2);
        root.Children.Add(_status);
        Grid.SetRow(bar, 3);
        root.Children.Add(bar);
        Content = root;
        AutomationProperties.SetAutomationId(this, id + ".column");
        _vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(_vm.AccountKey), StringComparison.Ordinal))
            {
                _input.Text = string.Empty;
            }

            Render();
        };
        Render();
    }

    private async Task SendAsync()
    {
        var text = _input.Text;
        _input.Text = string.Empty;
        var selected = ViewHost.Find<IMailSelection>()?.Current is { Count: > 0 } current ? current[0].EmailId : null;
        await _vm.AskAsync(text, selected, CancellationToken.None).ConfigureAwait(true);
    }

    private void Render()
    {
        _messages.Children.Clear();
        var any = false;
        foreach (var m in _vm.Turns)
        {
            any = true;
            var user = m.FromUser;
            var bubble = new Border
            {
                Background = user ? AppResources.Brush("Sel") : AppResources.Brush("Bubble"),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 6, 10, 6),
                HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                MaxWidth = 420,
                Child = new TextBlock { Text = m.Text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Foreground = user ? AppResources.Brush("SelText") : AppResources.Brush("Body"), FontSize = 13 },
            };
            _messages.Children.Add(bubble);
        }

        if (!any)
        {
            _messages.Children.Add(SetUiUi.Meta(_emptyHint));
        }

        _status.Text = _vm.Busy ? "Thinking" : _vm.Error;
        _send.IsEnabled = !_vm.Busy;
        _scroll.ChangeView(null, double.MaxValue, null, true);
    }
}

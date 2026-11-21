// Mirrors: Bulwark components/email/selection-view.tsx (the bar's buttons run the same commands as keys and menus); spec I-138..I-142; components/email/email-list.tsx:1412-1425 (the count slides in from
// the left and the buttons from the right while fading in, ML-08). Owner actions.

using Barbican.App.Motion;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Mail;

namespace Barbican.App.Views.Actions;

/// <summary>The selection bar in the list's slot.</summary>
public sealed partial class SelectionBarView : UserControl
{
    private readonly ICommandRegistry _commands;
    private readonly TranslateTransform _countShift = new();
    private readonly TranslateTransform _actionsShift = new();
    private bool _shown;

    public SelectionBarView()
    {
        List = ViewHost.Get<MessageListViewModel>();
        _commands = ViewHost.Get<ICommandRegistry>();
        Look = ViewHost.Get<StyleState>();
        InitializeComponent();
        CountLabel.RenderTransform = _countShift;
        BarActions.RenderTransform = _actionsShift;
        ApplyStyle();
        Look.PropertyChanged += (_, _) => ApplyStyle();
        List.PropertyChanged += OnListChanged;
    }

    // the bar swaps in when the selection starts (not when it grows): fade in, 12 px from the left (count) and the right (buttons), the shared enter motion, nothing with reduce motion
    private void OnListChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(MessageListViewModel.HasChecked), StringComparison.Ordinal))
        {
            return;
        }

        var now = List.HasChecked;
        if (now && !_shown && Look.IsBulwark)
        {
            var ms = MotionRuntime.Scale.EnterMs;
            Rise.Run(CountLabel, _countShift, ms, fromOpacity: 0, fromY: -12, horizontal: true);
            Rise.Run(BarActions, _actionsShift, ms, fromOpacity: 0, fromY: 12, horizontal: true);
        }

        _shown = now;
    }

    public MessageListViewModel List { get; }

    public StyleState Look { get; }

    // Bulwark (email-list.tsx:1412-1440): the buttons are 32 px icon buttons on the count's line, at the right; the prototypes' text chips fill a second line.
    private void ApplyStyle()
    {
        var bulwark = Look.IsBulwark;
        var style = (Style)Application.Current.Resources[bulwark ? "Button.Icon" : "Button.Chip"];
        BarActions.SetValue(Grid.RowProperty, bulwark ? 0 : 1);
        BarActions.SetValue(Grid.ColumnProperty, bulwark ? 2 : 0);
        BarActions.SetValue(Grid.ColumnSpanProperty, bulwark ? 1 : 4);
        BarActions.ColumnSpacing = bulwark ? 2 : 4;
        BarGrid.RowSpacing = bulwark ? 0 : 6;
        BarActions.HorizontalAlignment = bulwark ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        foreach (var button in BarActions.Children.OfType<Button>().Append(BarClear))
        {
            button.Style = style;
            if (bulwark)
            {
                button.Width = 32;
                button.Height = 32;
                button.CornerRadius = Look.ButtonRadius;
            }
            else
            {
                button.ClearValue(FrameworkElement.WidthProperty);
                button.ClearValue(FrameworkElement.HeightProperty);
                button.ClearValue(Control.CornerRadiusProperty);
            }
        }
    }

    private void OnToggleAll(object sender, RoutedEventArgs e) => List.ToggleSelectAll();

#pragma warning disable VSTHRD100 // XAML event handlers must return void; a failed action reports itself with a toast
    private async void OnCommand(object sender, RoutedEventArgs e)
#pragma warning restore VSTHRD100
    {
        if (sender is FrameworkElement { Tag: string id } && _commands.TryGet(id, out var command))
        {
            var context = new CommandContext(CommandSurface.SelectionBar, [], null, null, null);
            if (command.CanExecute(context))
            {
                // a thrown exception becomes an error toast and a crash-log line (Fable review 3 R3-05); a failed result toasts itself
                await (ViewHost.Find<CommandRunner>() ?? new CommandRunner(ViewHost.Find<Barbican.Presentation.Actions.IToastService>())).RunAsync(command, context);
            }
        }
    }
}

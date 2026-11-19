// Mirrors: PORT-PLAN section 3.7 (a custom host: Scrim + PopIn, focus trapped and restored, Esc cancels and Enter accepts, never ContentDialog); design/MOTION.md (Scrim fades in over
// Motion.enter, PopIn on the card, closing is instant); M1.md section 5 ("no caller" of IBodyOverlayStrategy: Linux covers the WebKit body with a stand-in before a scrim shows).
// The model is DialogService (Presentation); this draws the current dialog. Owner menus.

using Barbican.App.MailBody;
using Barbican.App.Motion;
using Barbican.App.Shell;
using Barbican.Presentation.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Barbican.App.Views.Dialogs;

/// <summary>Draws the dialog on screen.</summary>
public sealed partial class DialogHostView : UserControl
{
    private readonly DialogService _dialogs = ViewHost.Get<DialogService>();
    private readonly IBodyOverlayStrategy? _body = ViewHost.Find<IBodyOverlayStrategy>();
    private ActiveDialog? _shown;
    private Control? _restoreFocus;
    private bool _covered;

    public DialogHostView()
    {
        InitializeComponent();
        _dialogs.Changed += (_, _) => Render();
        SizeChanged += (_, _) => FitScrim();
    }

    private void Render()
    {
        var dialog = _dialogs.Current;
        if (ReferenceEquals(dialog, _shown))
        {
            return;
        }

        _shown = dialog;
        if (dialog is null)
        {
            Hide();
            return;
        }

        TitleText.Text = dialog.Title;
        MessageText.Visibility = Visibility.Collapsed;
        Field.Visibility = Visibility.Collapsed;
        CustomContent.Content = null;
        Buttons.Visibility = Visibility.Visible;
        Card.MaxWidth = 440;
        OkButton.Content = "OK";
        OkButton.Style = (Style)Application.Current.Resources["Button.Primary"];
        switch (dialog.Request)
        {
            case ConfirmDialogRequest confirm:
                MessageText.Text = confirm.Message;
                MessageText.Visibility = Visibility.Visible;
                OkButton.Content = confirm.ConfirmLabel;
                break;
            case PromptDialogRequest prompt:
                MessageText.Text = prompt.Label;
                MessageText.Visibility = Visibility.Visible;
                Field.Text = dialog.Text;
                Field.Visibility = Visibility.Visible;
                OkButton.Content = prompt.ConfirmLabel;
                break;
            default:
                // an owner's own dialog: its content comes from an IDialogContent registered for the request type
                foreach (var factory in ViewHost.Services.GetServices<IDialogContent>())
                {
                    if (factory.CanDraw(dialog.Request))
                    {
                        CustomContent.Content = factory.Create(dialog);
                        Buttons.Visibility = factory.OwnButtons ? Visibility.Collapsed : Visibility.Visible;
                        Card.MaxWidth = factory.MaxWidth;
                        break;
                    }
                }

                break;
        }

        Show();
    }

    private void Show()
    {
        if (!_covered && _body is not null && XamlRoot is not null && ActiveWindow.Current is { } window)
        {
            // Linux: the WebKit window hides under a full-window overlay, so the body is replaced by a captured stand-in before the scrim shows; Windows composes directly
            _body.Cover(window);
            _covered = true;
        }

        _restoreFocus = (XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot)) as Control;
        FitScrim();
        DialogPopup.IsOpen = true;
        MotionRuntime.FadeIn(Backdrop);
        PopIn.Run(Card, new Point(0.5, 0.5));
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (Field.Visibility == Visibility.Visible)
            {
                _ = Field.Focus(FocusState.Programmatic);
                Field.SelectAll();
            }
            else if (Buttons.Visibility == Visibility.Collapsed && FocusManager.FindFirstFocusableElement(CustomContent) is Control custom)
            {
                _ = custom.Focus(FocusState.Keyboard);
            }
            else
            {
                _ = OkButton.Focus(FocusState.Programmatic);
            }
        });
    }

    private void Hide()
    {
        DialogPopup.IsOpen = false;
        if (_covered)
        {
            _body?.Uncover();
            _covered = false;
        }

        _ = _restoreFocus?.Focus(FocusState.Programmatic);
        _restoreFocus = null;
    }

    private void FitScrim()
    {
        Backdrop.Width = Math.Max(ActualWidth, 1);
        Backdrop.Height = Math.Max(ActualHeight, 1);
    }

    private void OnOk(object sender, RoutedEventArgs e) => Accept();

    private void OnCancel(object sender, RoutedEventArgs e) => _shown?.Complete(null);

    private void OnFieldKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // a TextBox keeps its keys away from the shell's router, so Enter and Escape are handled here
        if (_shown is null)
        {
            return;
        }

        _shown.Text = Field.Text;
        if (e.Key == global::Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            Accept();
        }
        else if (e.Key == global::Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            _shown.Complete(null);
        }
    }

    private void Accept()
    {
        switch (_shown?.Request)
        {
            case ConfirmDialogRequest:
                _shown.Complete(true);
                break;
            case PromptDialogRequest:
                _shown.Text = Field.Text;
                if (_shown.Text.Trim().Length > 0)
                {
                    _shown.Complete(_shown.Text.Trim());
                }

                break;
            default:
                _shown?.Complete(true);
                break;
        }
    }
}

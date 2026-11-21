// Bulwark 1.21.2 reference (default theme): components/ui/dialog.tsx:131-134 (title text-lg font-semibold, description mt-2 text-sm muted, footer gap-3 px-6 pb-6), :181 (card
// bg-background rounded-lg shadow-xl, p-6 body; zoom-in-95 duration-200), :231 (scrim bg-black/50 fade-in duration-150), ui/confirm-dialog.tsx:143-170 (destructive variant: 40 px circle
// bg-destructive/10 with a 20 px triangle beside the text column, solid destructive button, Cancel is the outline button; no close X: showClose false). Bento and Nordic keep the older look.
// Mirrors: PORT-PLAN section 3.7 (a custom host: Scrim + PopIn, focus trapped and restored, Esc cancels and Enter accepts, never ContentDialog); design/MOTION.md (Scrim fades in over
// Motion.enter, PopIn on the card, closing is instant); M1.md section 5 ("no caller" of IBodyOverlayStrategy: Linux covers the WebKit body with a stand-in before a scrim shows).
// The model is DialogService (Presentation); this draws the current dialog. Owner menus.

using Barbican.App.MailBody;
using Barbican.App.Motion;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Barbican.App.Views.Dialogs;

/// <summary>Draws the dialog on screen.</summary>
public sealed partial class DialogHostView : UserControl
{
    private readonly DialogService _dialogs;
    private readonly IBodyOverlayStrategy? _body;
    private IDialogContent? _factory;
    private ActiveDialog? _shown;
    private Control? _restoreFocus;
    private bool _covered;

    public DialogHostView()
        : this(ViewHost.Get<DialogService>(), ViewHost.Find<IBodyOverlayStrategy>())
    {
    }

    /// <summary>A host over its own <see cref="DialogService"/>: the detached composer window has no shell overlay, so it carries one (<see cref="DialogHostRouter"/>).</summary>
    internal DialogHostView(DialogService dialogs, IBodyOverlayStrategy? body)
    {
        _dialogs = dialogs;
        _body = body;
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
        ResetCard();
        ApplyLook();
        switch (dialog.Request)
        {
            case ConfirmDialogRequest confirm:
                MessageText.Text = confirm.Message;
                MessageText.Visibility = Visibility.Visible;
                OkButton.Content = confirm.ConfirmLabel;
                if (confirm.Danger && DialogStyles.Bulwark)
                {
                    WarnIcon.Visibility = Visibility.Visible;
                    OkButton.Style = DialogStyles.Destructive;
                    DialogStyles.Dress(OkButton);
                }

                break;
            case PromptDialogRequest prompt:
                MessageText.Text = prompt.Label;
                MessageText.Visibility = Visibility.Visible;
                Field.Text = dialog.Text;
                Field.Visibility = Visibility.Visible;
                OkButton.Content = prompt.ConfirmLabel;
                break;
            default:
                DrawCustom(dialog);
                break;
        }

        CustomContent.Visibility = CustomContent.Content is null ? Visibility.Collapsed : Visibility.Visible;
        Show();
    }

    // the card as a plain question: the stock buttons and the destructive icon, until a request or a content says otherwise
    private void ResetCard()
    {
        _factory = null;
        HeaderMenu.Set(null);
        Buttons.Visibility = Visibility.Visible;
        Card.MaxWidth = 440;
        OkButton.Content = "OK";
        OkButton.Style = (Style)Application.Current.Resources["Button.Primary"];
        WarnIcon.Visibility = Visibility.Collapsed;
        WarnGlyph.Kind = "Alert";
        WarnGlyph.Foreground = AppResources.Brush("Destructive");
        WarnIcon.Background = AppResources.Brush("FeedbackDestructiveHover");
    }

    // an owner's own dialog: its content comes from an IDialogContent registered for the request type
    private void DrawCustom(ActiveDialog dialog)
    {
        foreach (var factory in ViewHost.Services.GetServices<IDialogContent>())
        {
            if (factory.CanDraw(dialog.Request))
            {
                _factory = factory;
                CustomContent.Content = factory.Create(dialog);
                HeaderMenu.Set(factory.CreateHeaderMenu(dialog));
                if (factory.HeaderIcon(dialog) is { } icon)
                {
                    WarnGlyph.Kind = icon.Kind;
                    WarnGlyph.Foreground = AppResources.Brush(icon.ForegroundKey);
                    WarnIcon.Background = AppResources.Brush(icon.BackgroundKey);
                    WarnIcon.Visibility = Visibility.Visible;
                }

                Buttons.Visibility = factory.OwnButtons ? Visibility.Collapsed : Visibility.Visible;
                Card.MaxWidth = factory.MaxWidthOf(dialog.Request);
                break;
            }
        }
    }

    // Bulwark metrics when that style is on (read at each open, so a style flip applies to the next dialog), the older ones otherwise
    private void ApplyLook()
    {
        var bulwark = DialogStyles.Bulwark;
        Backdrop.Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(bulwark ? (byte)0x80 : (byte)0x66, 0, 0, 0));
        Card.CornerRadius = StyleState.Current.DialogRadius;
        CardShadow.Apply(Card, CardDepth.ExtraLarge, Backdrop);
        Card.Padding = new Thickness(bulwark ? 24 : 20);
        TitleText.FontSize = bulwark ? 18 : 16;
        MessageText.FontSize = bulwark ? 14 : 13;
        MessageText.Foreground = AppResources.Brush(bulwark ? "Dim" : "Body");
        MessageText.Margin = new Thickness(0, bulwark ? -4 : 0, 0, 0);
        Buttons.Spacing = DialogStyles.ButtonGap;
        Buttons.Margin = new Thickness(0, bulwark ? 24 : 12, 0, 0);
        DialogStyles.Dress(CancelButton);
        DialogStyles.Dress(OkButton);
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
            else if (Buttons.Visibility == Visibility.Collapsed && CustomContent.Content is UIElement drawn
                && (_factory?.InitialFocus(drawn) ?? FocusManager.FindFirstFocusableElement(CustomContent) as Control) is { } custom)
            {
                // a content that draws its own buttons says where the focus starts (a text field, or the safe button of a question)
                FocusWhenReady(custom);
            }
            else
            {
                _ = OkButton.Focus(FocusState.Programmatic);
            }
        });
    }

    // a content built in code may not be in the tree yet on the first turn: focus on its Loaded then (a first Focus on an unloaded control returns false)
    private static void FocusWhenReady(Control target)
    {
        if (target.IsLoaded && target.Focus(FocusState.Keyboard))
        {
            return;
        }

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            target.Loaded -= OnLoaded;
            _ = target.Focus(FocusState.Keyboard);
        }

        target.Loaded += OnLoaded;
    }

    private void Hide()
    {
        DialogPopup.IsOpen = false;
        CustomContent.Content = null;
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

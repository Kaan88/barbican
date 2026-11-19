// Mirrors: Bulwark components/email/email-composer.tsx (layout, capture key handler :5509, Cc and Bcc toggles :6031-6056, status :5660); M3 plan section 6.4 step 2; I-234..I-239, I-246, I-249, I-253, I-257.
// The composer pane. The data is ComposerViewModel (one per open composer, from IComposerService.Current); this draws it and routes the keys. The cards, the files and the key table are the other parts of this class.

#pragma warning disable CA1859, CA1822 // the card builders return UIElement by design (one slot, many kinds)

using System.Collections.Specialized;
using System.ComponentModel;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Core.Compose;
using Barbican.Presentation.Compose;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Barbican.App.Views.Compose;

/// <summary>The composer pane.</summary>
public sealed partial class ComposerView : UserControl
{
    private readonly IComposerService _service = ViewHost.Get<IComposerService>();
    private readonly ThemeController _theme = ViewHost.Get<ThemeController>();
    private ComposerViewModel? _vm;
    private bool _syncing;

    public ComposerView()
    {
        Look = ViewHost.Get<StyleState>();
        InitializeComponent();
        _service.OpenChanged += (_, _) => DispatcherQueue.TryEnqueue(() => Attach(_service.Current));
        PreviewKeyDown += OnPreviewKeyDown;
        Host.DragEnter += OnDragOver;
        Host.DragOver += OnDragOver;
        Host.DragLeave += (_, _) => DropOverlay.Visibility = Visibility.Collapsed;
        Host.Drop += OnDrop;
        SizeChanged += (_, _) => ApplyFooterDensity();
        ToField.Trailing = TogglesFor();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public StyleState Look { get; }

    public event EventHandler? MaximizeRequested;

    internal event EventHandler? WindowRequested;
#if AUTOMATION && WINDOWS
    internal bool IsConfirmationVisible => _vm?.Dialog is not null && CardLayer.IsLoaded
        && CardLayer.Visibility == Visibility.Visible && CardLayer.ActualHeight > 0;
#endif
    internal Window? DetachedWindow { get; private set; }
    internal bool CanChangeWindow => !_pickingAttachments && _vm is { IsClosed: false, IsSending: false, Dialog: null, Schedule: null, Templates: null } && _card == CardKind.None;

    private void OnWindowClick(object sender, RoutedEventArgs e) => WindowRequested?.Invoke(this, EventArgs.Empty);

    internal void SetDetachedWindow(Window? window)
    {
        DetachedWindow = window;
        string label = window is null ? "Open in a new window" : "Return to main window";
        AutomationProperties.SetName(WindowButton, label);
        Barbican.App.Motion.Tip.SetText(WindowButton, label);
        WindowGlyph.Glyph = window is null ? "\uE8A7" : "\uE72B";
        ApplyComposerLayout();
    }

    private bool _maximized;

    public void SetMaximized(bool maximized)
    {
        _maximized = maximized;
        var label = maximized ? "Restore composer" : "Maximize composer";
        MaximizeGlyph.Glyph = maximized ? "\uE73F" : "\uE740";
        AutomationProperties.SetName(MaximizeButton, label);
        Barbican.App.Motion.Tip.SetText(MaximizeButton, label);
    }

    private void OnMaximizeClick(object sender, RoutedEventArgs e) => MaximizeRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>The composer on screen, or null.</summary>
    public ComposerViewModel? Vm => _vm;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _theme.CssApplied += OnThemeApplied;
        Look.PropertyChanged += OnLookChanged;
        ApplyComposerLayout();
        Attach(_service.Current);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _theme.CssApplied -= OnThemeApplied;
        Look.PropertyChanged -= OnLookChanged;
    }

    private void OnThemeApplied(object? sender, ThemeCssEventArgs e) => ApplyComposerLayout();

    private void OnLookChanged(object? sender, PropertyChangedEventArgs e) => ApplyComposerLayout();

    // Bulwark email-composer.tsx:5674-5676, 5783-5786, 5940-5948: flat header and
    // separately ruled address rows. Keep all controls and their actions shared with legacy layouts.
    private void ApplyComposerLayout()
    {
        var bulwark = _theme.Applied?.Bulwark == true;
        Pane.Background = bulwark ? AppResources.Brush("Bg") : Look.PaneBrush;
        Pane.BorderThickness = bulwark ? new Thickness(0) : Look.PaneBorder;
        Pane.CornerRadius = bulwark ? new CornerRadius(0) : Look.PaneRadius;
        InnerBorder.BorderThickness = bulwark ? new Thickness(0) : Look.ListHairline;
        InnerBorder.CornerRadius = bulwark ? new CornerRadius(0) : Look.PaneRadius;
        Inner.Padding = bulwark ? new Thickness(0) : Look.ReaderPadding;
        Inner.RowSpacing = bulwark ? 0 : 10;
        TitleRow.MinHeight = bulwark ? 56 : 0;
        TitleRow.Padding = bulwark ? new Thickness(16, 0, 16, 0) : new Thickness(0);
        TitleRow.BorderBrush = AppResources.Brush("Line");
        TitleRow.BorderThickness = bulwark ? new Thickness(0, 0, 0, 1) : new Thickness(0);
        TitleText.FontSize = bulwark ? 16 : 17;
        TitleText.FontWeight = bulwark ? FontWeights.SemiBold : FontWeights.Bold;
#if WINDOWS
        WindowButton.Visibility = bulwark || DetachedWindow is not null ? Visibility.Visible : Visibility.Collapsed;
#endif
        MaximizeButton.Visibility = bulwark && DetachedWindow is null ? Visibility.Visible : Visibility.Collapsed;
        DiscardButton.Visibility = bulwark ? Visibility.Collapsed : Visibility.Visible;
        FooterDiscard.Visibility = bulwark ? Visibility.Visible : Visibility.Collapsed;
        ApplyFieldLayout(bulwark);
        BodyRow.Margin = bulwark ? new Thickness(12, 0, 12, 0) : new Thickness(0);
        Footer.Padding = bulwark ? new Thickness(16, 10, 16, 10) : new Thickness(0);
        Footer.BorderBrush = AppResources.Brush("Line");
        Footer.BorderThickness = bulwark ? new Thickness(0, 1, 0, 0) : new Thickness(0);
        Grid.SetColumn(SendGroup, bulwark ? 5 : 0);
        Grid.SetColumn(AttachButton, bulwark ? 0 : 2);
        Grid.SetColumn(TemplatesButton, 1);
        Grid.SetColumn(OptionsButton, bulwark ? 2 : 3);
        SendButton.CornerRadius = bulwark ? new CornerRadius(6, 0, 0, 6) : new CornerRadius(18, 0, 0, 18);
        SendArrow.CornerRadius = bulwark ? new CornerRadius(0, 6, 6, 0) : new CornerRadius(0, 18, 18, 0);
        TemplatesButton.CornerRadius = AttachButton.CornerRadius = OptionsButton.CornerRadius = new CornerRadius(bulwark ? 6 : 18);
        foreach (var button in new[] { TemplatesButton, AttachButton, OptionsButton })
        {
            button.BorderThickness = new Thickness(bulwark ? 0 : 1);
            button.FontSize = bulwark ? 14 : 13;
            button.FontWeight = bulwark ? FontWeights.Normal : FontWeights.SemiBold;
        }
        ApplyFooterDensity();
    }

    private void ApplyFieldLayout(bool bulwark)
    {
        Fields.Margin = new Thickness(0);
        FromRow.MinHeight = 34;
        SubjectRow.MinHeight = 36;
        FromRow.Padding = SubjectRow.Padding = bulwark ? new Thickness(16, 10, 16, 10) : new Thickness(0);
        FromRow.ColumnSpacing = SubjectRow.ColumnSpacing = bulwark ? 8 : 10;
        FromLabelColumn.Width = SubjectLabelColumn.Width = new GridLength(bulwark ? 64 : 54);
        FromLabel.Text = bulwark ? "From:" : "From";
        SubjectLabel.Text = bulwark ? "Subject:" : "Subject";
        FromLabel.FontSize = SubjectLabel.FontSize = bulwark ? 14 : 12.5;
        FromText.FontSize = bulwark ? 14 : 13;
        ToField.ApplyComposerStyle(bulwark);
        CcField.ApplyComposerStyle(bulwark);
        BccField.ApplyComposerStyle(bulwark);
        SubjectBox.FontWeight = bulwark ? FontWeights.Normal : FontWeights.SemiBold;
    }

    private void ApplyFooterDensity()
    {
        var bulwark = _theme.Applied?.Bulwark == true;
        var compact = bulwark && ActualWidth > 0 && ActualWidth < 640;
        TemplatesLabel.Visibility = AttachLabel.Visibility = OptionsLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Footer.ColumnSpacing = compact ? 2 : 8;
        foreach (var button in new[] { TemplatesButton, AttachButton, OptionsButton })
        {
            if (compact)
            {
                button.Width = 36;
                button.MinWidth = 0;
                button.Padding = new Thickness(0);
            }
            else
            {
                button.ClearValue(WidthProperty);
                button.ClearValue(MinWidthProperty);
                if (bulwark)
                {
                    button.Padding = new Thickness(10, 0, 10, 0);
                }
                else
                {
                    button.ClearValue(PaddingProperty);
                }
            }
        }
    }

    private void Attach(ComposerViewModel? vm)
    {
        if (ReferenceEquals(vm, _vm))
        {
            return;
        }

        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmChanged;
            _vm.Attachments.CollectionChanged -= OnAttachmentsChanged;
            _vm.Dialog?.Complete(null);
        }

        _vm = vm;
        _card = CardKind.None;
        if (vm is null)
        {
            ToField.Bind(null);
            CcField.Bind(null);
            BccField.Bind(null);
            BodyHost.Content = null;
            RefreshCards();
            return;
        }

        vm.PropertyChanged += OnVmChanged;
        vm.Attachments.CollectionChanged += OnAttachmentsChanged;
        ToField.Bind(vm.To);
        CcField.Bind(vm.Cc);
        BccField.Bind(vm.Bcc);
        TitleText.Text = TitleOf(vm.Kind);
        AutomationProperties.SetName(Pane, TitleText.Text);
        _syncing = true;
        SubjectBox.Text = vm.Subject;
        _syncing = false;
        BuildBody();
        SyncAll();
        _ = DispatcherQueue.TryEnqueue(() => (vm.To.Recipients.Count == 0 ? ToField : null)?.FocusInput());
    }

    private static string PlainOf(Barbican.Core.Editor.Doc doc) => doc.TextRange(new Barbican.Core.Editor.Pos(0, 0), doc.End);

    private static string TitleOf(ComposeKind kind) => kind switch
    {
        ComposeKind.Reply => "Reply",
        ComposeKind.ReplyAll => "Reply all",
        ComposeKind.Forward or ComposeKind.ForwardAsAttachment => "Forward",
        ComposeKind.EditDraft => "Edit draft",
        _ => "New message",
    };

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ComposerViewModel.Editor) or nameof(ComposerViewModel.PlainText):
                BuildBody();
                break;
            case nameof(ComposerViewModel.Dialog) or nameof(ComposerViewModel.Schedule) or nameof(ComposerViewModel.Templates):
                RefreshCards();
                break;
            case nameof(ComposerViewModel.CanSend):
                SyncAll();
                if (_card == CardKind.SendMenu) RefreshCards();
                break;
            default:
                SyncAll();
                break;
        }
    }

    // everything the header and footer show that the view model can change by itself
    private void SyncAll()
    {
        if (_vm is not { } vm)
        {
            return;
        }

        WindowButton.IsEnabled = !vm.IsSending;
        CcBox.Visibility = vm.ShowCc ? Visibility.Visible : Visibility.Collapsed;
        BccBox.Visibility = vm.ShowBcc ? Visibility.Visible : Visibility.Collapsed;
        FromText.Text = vm.FromText;
        StatusText.Text = vm.Status;
        SendButton.IsEnabled = vm.CanSend;
        SendArrow.IsEnabled = vm.CanSend;
        if (!string.Equals(SubjectBox.Text, vm.Subject, StringComparison.Ordinal))
        {
            _syncing = true;
            SubjectBox.Text = vm.Subject;
            _syncing = false;
        }

        if (ToField.Trailing is StackPanel toggles)
        {
            // M5 con-ui: the third child is the directory button (only where the server has a directory, D-M5-13), so the row stays while it is there
            var directory = Barbican.App.Views.Contacts.ContactsUi.Model.DirectoryAvailable;
            toggles.Visibility = vm.ShowCc && vm.ShowBcc && !directory ? Visibility.Collapsed : Visibility.Visible;
            toggles.Children[0].Visibility = vm.ShowCc ? Visibility.Collapsed : Visibility.Visible;
            toggles.Children[1].Visibility = vm.ShowBcc ? Visibility.Collapsed : Visibility.Visible;
            toggles.Children[2].Visibility = directory ? Visibility.Visible : Visibility.Collapsed;
        }

        RebuildAttachments();
    }

    // the Cc and Bcc links at the right end of the To row (I-246)
    private StackPanel TogglesFor()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        panel.Children.Add(Toggle("Cc", "composer.cc.toggle", () => _vm?.RevealCc()));
        panel.Children.Add(Toggle("Bcc", "composer.bcc.toggle", () => _vm?.RevealBcc()));
        // M5 con-ui (I-369): the directory picker adds the chosen principals to the To field
        panel.Children.Add(Toggle("Directory", "composer.directory", () => Barbican.App.Views.Contacts.ContactsUi.Run(AddFromDirectoryAsync)));
        panel.Children[2].Visibility = Visibility.Collapsed;
        return panel;
    }

    private async Task AddFromDirectoryAsync()
    {
        var chosen = await Barbican.App.Views.Contacts.ContactsUi.Actions.PickFromDirectoryAsync(CancellationToken.None).ConfigureAwait(true);
        if (_vm is not { } vm || string.IsNullOrEmpty(chosen))
        {
            return;
        }

        foreach (var recipient in Barbican.Core.Compose.Recipients.RecipientText.ParseList(chosen))
        {
            _ = vm.To.Add(recipient);
        }
    }

    private static Button Toggle(string text, string id, Action click)
    {
        var button = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Height = 26, Padding = new Thickness(8, 0, 8, 0), CornerRadius = new CornerRadius(13), Content = new TextBlock { Text = text, FontSize = 12.5, Foreground = AppResources.Brush("Dim") } };
        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetName(button, "Add " + text);
        button.Click += (_, _) => click();
        return button;
    }

    private void BuildBody()
    {
        if (_vm is not { } vm)
        {
            return;
        }

        if (vm.Editor is { } editor && ComposerBodyHost.EditorFactory is { } make)
        {
            BodyHost.Content = make(editor);
            return;
        }

        // no editor control yet: a plain text box stands in (not the shipped editor; the editor view replaces it through ComposerBodyHost.EditorFactory)
        var box = new TextBox
        {
            Style = (Style)Application.Current.Resources["TextBox.Bare"],
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Stretch,
            Text = vm.Editor is { } e ? PlainOf(e.Session.Doc) : vm.FallbackBody,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        AutomationProperties.SetAutomationId(box, "composer.body");
        AutomationProperties.SetName(box, "Message body");
        box.TextChanged += (_, _) =>
        {
            if (vm.Editor is { } session)
            {
                session.Session.Load(Barbican.Core.Editor.Doc.FromText(box.Text));
            }
            else
            {
                vm.FallbackBody = box.Text;
            }
        };
        vm.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(ComposerViewModel.FallbackBody), StringComparison.Ordinal) && !string.Equals(box.Text, vm.FallbackBody, StringComparison.Ordinal) && vm.Editor is null)
            {
                box.Text = vm.FallbackBody;
            }
        };
        BodyHost.Content = box;
    }

    private void OnSubjectChanged(object sender, TextChangedEventArgs e)
    {
        if (!_syncing && _vm is { } vm)
        {
            vm.Subject = SubjectBox.Text;
        }
    }

    private void OnSendClick(object sender, RoutedEventArgs e) => _ = _vm?.SendAsync();

    private void OnSendArrowClick(object sender, RoutedEventArgs e) => ShowPanel(CardKind.SendMenu);

    private void OnTemplatesClick(object sender, RoutedEventArgs e) => _ = _vm?.OpenTemplatesAsync();

    private void OnOptionsClick(object sender, RoutedEventArgs e) => ShowPanel(CardKind.Options);

    private void OnFromClick(object sender, RoutedEventArgs e) => ShowPanel(CardKind.From);

    private void OnDiscardClick(object sender, RoutedEventArgs e) => _ = _vm?.DiscardAsync();

    private void OnCloseClick(object sender, RoutedEventArgs e) => _ = _service.TryCloseAsync(CancellationToken.None);

    // The composer's keys (Bulwark's capture handler): they work from any field because the key tunnels down from here. The shell's app keys stay out while the composer is open.
    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_vm is not { } vm)
        {
            return;
        }

        var ctrl = Down(VirtualKey.Control);
        var shift = Down(VirtualKey.Shift);
        if (e.Key == VirtualKey.Escape)
        {
            HandleEscape(vm, e);
            return;
        }

        if (ctrl && e.Key == VirtualKey.V && TryPasteFiles())
        {
            e.Handled = true;
            return;
        }

        var name = e.Key switch
        {
            VirtualKey.Enter => "enter",
            VirtualKey.S => "s",
            VirtualKey.C => "c",
            VirtualKey.B => "b",
            _ => null,
        };
        if (name is not null && ctrl && vm.HandleChord(name, true, shift, Down(VirtualKey.Menu)))
        {
            e.Handled = true;
            return;
        }

        if (!ctrl && !shift && e.Key == VirtualKey.T && e.OriginalSource is Button)
        {
            e.Handled = true;
            _ = vm.OpenTemplatesAsync();
        }
    }

    private void HandleEscape(ComposerViewModel vm, KeyRoutedEventArgs e)
    {
            e.Handled = true;
            if (vm.Dialog is not null)
            {
                vm.AnswerDialog(null);
            }
            else if (vm.Schedule is not null)
            {
                vm.CancelSchedule();
            }
            else if (vm.Templates is not null)
            {
                vm.CloseTemplates();
            }
            else if (_card != CardKind.None)
            {
                ShowPanel(CardKind.None);
            }
            else if (_maximized && !SuggestionsOpen())
            {
                MaximizeRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (!SuggestionsOpen())
            {
                _ = _service.TryCloseAsync(CancellationToken.None);
            }
            else
            {
                e.Handled = false;
            }
    }

    private bool SuggestionsOpen() => _vm is { } vm && (vm.To.IsOpen || vm.Cc.IsOpen || vm.Bcc.IsOpen);

    private static bool Down(VirtualKey key) => Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void RebuildAttachments()
    {
        if (_vm is not { } vm)
        {
            return;
        }

        var items = vm.Attachments.Where(a => a.Attachment.Cid is null).ToList();
        AttachmentList.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AttachmentList.Items.Clear();
        for (var i = 0; i < items.Count; i++)
        {
            AttachmentList.Items.Add(AttachmentChip(items[i], i));
        }
    }

    private void OnAttachmentsChanged(object? sender, NotifyCollectionChangedEventArgs e) => DispatcherQueue.TryEnqueue(RebuildAttachments);

    private UIElement AttachmentChip(AttachmentItem item, int index)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new Barbican.App.Controls.SIcon { Kind = "Clip", Size = 14, Foreground = AppResources.Brush("Dim") });
        row.Children.Add(new TextBlock { Text = item.Name, FontSize = 12.5, Foreground = AppResources.Brush("Body"), MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = item.IsUploading ? "Uploading" : item.SizeText, FontSize = 11.5, Foreground = AppResources.Brush("Faint"), VerticalAlignment = VerticalAlignment.Center });
        if (item.IsUploading)
        {
            row.Children.Add(new ProgressRing { IsActive = true, Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center });
        }

        var remove = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Content = new Barbican.App.Controls.SIcon { Kind = "Close", Size = 10 } };
        AutomationProperties.SetAutomationId(remove, $"composer.attachment.{index}.remove");
        AutomationProperties.SetName(remove, (item.IsUploading ? "Cancel upload of " : "Remove ") + item.Name);
        remove.Click += (_, _) => _vm?.RemoveAttachment(item);
        row.Children.Add(remove);
        var box = new Border
        {
            Background = AppResources.Brush("Chip"),
            BorderBrush = AppResources.Brush("Line"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(13),
            Height = 28,
            Padding = new Thickness(10, 0, 4, 0),
            Margin = new Thickness(0, 0, 6, 6),
            Child = row,
        };
        AutomationProperties.SetAutomationId(box, $"composer.attachment.{index}");
        AutomationProperties.SetName(box, item.Name);
        item.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(AttachmentItem.State), StringComparison.Ordinal))
            {
                DispatcherQueue.TryEnqueue(RebuildAttachments);
            }
        };
        return box;
    }
}

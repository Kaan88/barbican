// Mirrors: Bulwark components/email/email-composer.tsx close dialog :6916-6923, send-warnings-dialog.tsx, external-recipient-confirm.tsx, schedule-send-dialog.tsx, template-picker.tsx, the From list :5813 with the sub-address helper
// and "Edit sender", Options :6600-6680 (Plain text, Read receipt, Priority), the Send arrow :5722; I-235, I-240, I-241, I-243, I-244, I-255, I-256, I-257, I-258, I-259, I-260.
// The cards of the composer pane: one at a time on the scrim of CardLayer, inside the pane (docked replies included), no OS window.

#pragma warning disable CA1859, CA1822 // the card builders return UIElement by design (one slot, many kinds)

using Barbican.App.Theme;
using Barbican.Core.Compose;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Compose.Pipeline;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;

namespace Barbican.App.Views.Compose;

public sealed partial class ComposerView
{
    private enum CardKind
    {
        None,
        From,
        Options,
        SendMenu,
    }

    private CardKind _card;

    private void ShowPanel(CardKind kind)
    {
        _card = kind;
        RefreshCards();
    }

    private void OnScrimTapped(object sender, TappedRoutedEventArgs e)
    {
        // a tap outside the card dismisses it (the same as Escape); a question needs its answer
        if (_vm is { } vm && vm.Dialog is null)
        {
            if (vm.Schedule is not null)
            {
                vm.CancelSchedule();
            }
            else if (vm.Templates is not null)
            {
                vm.CloseTemplates();
            }
            else
            {
                ShowPanel(CardKind.None);
            }
        }
    }

    private void OnCardTapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    private void RefreshCards()
    {
        UIElement? content = null;
        if (_vm is { } vm)
        {
            content = vm.Dialog is { } d
                ? DialogCard(d)
                : vm.Schedule is { } s
                    ? ScheduleCard(s)
                    : vm.Templates is { } t
                        ? TemplatesCard(t)
                        : _card switch
                        {
                            CardKind.From => FromCard(vm),
                            CardKind.Options => OptionsCard(vm),
                            CardKind.SendMenu => SendMenuCard(vm),
                            _ => null,
                        };
        }

        // UI-SWEEP-1 C-03: a docked reply is short, so a tall card (Schedule, Templates) scrolls instead of being cut off
        Card.Child = content is null ? null : new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Disabled };
        CardLayer.Visibility = content is null ? Visibility.Collapsed : Visibility.Visible;
        if (content is not null)
        {
            Card.MaxWidth = content is FrameworkElement { Tag: "wide" } ? 560 : 460;
        }
    }

    // ---- the questions -----------------------------------------------------------------------------------

    private UIElement DialogCard(ComposerDialog dialog)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(Heading(dialog.Title));
        panel.Children.Add(Body(dialog.Message));
        foreach (var line in dialog.Lines)
        {
            panel.Children.Add(Body("•  " + line, "composer.dialog.line"));
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var choice in dialog.Choices)
        {
            var id = choice.Id;
            buttons.Children.Add(CardButton(choice.Label, choice.Tone, "composer.dialog." + id, () => _vm?.AnswerDialog(id)));
        }

        panel.Children.Add(buttons);
        AutomationProperties.SetAutomationId(panel, "composer.dialog");
        AutomationProperties.SetName(panel, dialog.Title);
        return panel;
    }

    private UIElement SendMenuCard(ComposerViewModel vm)
    {
        var panel = new StackPanel { Spacing = 8, MinWidth = 260 };
        panel.Children.Add(Heading("Send"));
        panel.Children.Add(CardButton("Send now", DialogTone.Primary, "composer.sendmenu.now", () =>
        {
            ShowPanel(CardKind.None);
            _ = vm.SendAsync();
        }));
        panel.Children.Add(CardButton("Schedule send…", DialogTone.Plain, "composer.sendmenu.schedule", () =>
        {
            ShowPanel(CardKind.None);
            _ = vm.OpenScheduleAsync();
        }));
        AutomationProperties.SetAutomationId(panel, "composer.sendmenu");
        return panel;
    }

    // ---- schedule ----------------------------------------------------------------------------------------

    internal static StackPanel ScheduleCard(ScheduleDialogViewModel state)
    {
        var panel = new StackPanel { Spacing = 8, MinWidth = 320 };
        panel.Children.Add(Heading(state.Title));
        for (var i = 0; i < state.Presets.Count; i++)
        {
            var index = i;
            var preset = state.Presets[i];
            panel.Children.Add(CardButton($"{preset.Label}   {preset.When}", DialogTone.Plain, $"composer.schedule.preset.{i}", () => state.Choose(index), stretch: true));
        }

        panel.Children.Add(Body("Or pick a date and time"));
        var date = new TextBox { PlaceholderText = "2026-11-20", Text = state.Date, FontSize = 13, MinWidth = 120 };
        var time = new TextBox { PlaceholderText = "08:00", Text = state.Time, FontSize = 13, MinWidth = 90 };
        AutomationProperties.SetAutomationId(date, "composer.schedule.date");
        AutomationProperties.SetName(date, "Date");
        AutomationProperties.SetAutomationId(time, "composer.schedule.time");
        AutomationProperties.SetName(time, "Time");
        date.TextChanged += (_, _) => state.Date = date.Text;
        time.TextChanged += (_, _) => state.Time = time.Text;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(date);
        row.Children.Add(time);
        panel.Children.Add(row);
        var error = new TextBlock { FontSize = 12.5, Foreground = AppResources.Brush("RoseFg"), TextWrapping = TextWrapping.Wrap, Text = state.Error, Visibility = state.Error.Length == 0 ? Visibility.Collapsed : Visibility.Visible };
        AutomationProperties.SetAutomationId(error, "composer.schedule.error");
        state.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(ScheduleDialogViewModel.Error), StringComparison.Ordinal))
            {
                error.Text = state.Error;
                error.Visibility = state.Error.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
        };
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(CardButton("Cancel", DialogTone.Plain, "composer.schedule.cancel", state.Cancel));
        buttons.Children.Add(CardButton(state.ConfirmLabel, DialogTone.Primary, "composer.schedule.ok", () => state.ChooseTyped()));
        panel.Children.Add(buttons);
        AutomationProperties.SetAutomationId(panel, "composer.schedule");
        return panel;
    }

    // ---- templates ---------------------------------------------------------------------------------------

    private UIElement TemplatesCard(TemplatePickerState picker)
    {
        var panel = new StackPanel { Spacing = 8, MinWidth = 380, Tag = "wide" };
        panel.Children.Add(Heading("Templates"));
        var search = new TextBox { PlaceholderText = "Search name, subject or category", Text = picker.Search, FontSize = 13 };
        AutomationProperties.SetAutomationId(search, "composer.templates.search");
        AutomationProperties.SetName(search, "Search templates");
        search.TextChanged += (_, _) => picker.Search = search.Text;
        panel.Children.Add(search);
        var list = new StackPanel { Spacing = 4 };
        var scroll = new ScrollViewer { MaxHeight = 240, Content = list };
        panel.Children.Add(scroll);
        void Fill()
        {
            list.Children.Clear();
            if (picker.Items.Count == 0)
            {
                list.Children.Add(Body("No templates."));
            }

            for (var i = 0; i < picker.Items.Count; i++)
            {
                list.Children.Add(TemplateRow(picker, picker.Items[i], i));
            }
        }

        Fill();
        var save = new TextBox { PlaceholderText = "Name for this message as a template", FontSize = 13, Text = picker.SaveName };
        AutomationProperties.SetAutomationId(save, "composer.templates.name");
        save.TextChanged += (_, _) => picker.SaveName = save.Text;
        var error = new TextBlock { FontSize = 12.5, Foreground = AppResources.Brush("RoseFg"), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(error, "composer.templates.error");
        picker.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(TemplatePickerState.Items):
                    Fill();
                    break;
                case nameof(TemplatePickerState.Error):
                    error.Text = picker.Error ?? string.Empty;
                    error.Visibility = picker.Error is null ? Visibility.Collapsed : Visibility.Visible;
                    break;
                case nameof(TemplatePickerState.SaveName) when !string.Equals(save.Text, picker.SaveName, StringComparison.Ordinal):
                    save.Text = picker.SaveName;
                    break;
                default:
                    break;
            }
        };
        panel.Children.Add(save);
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(CardButton("Close", DialogTone.Plain, "composer.templates.close", picker.Cancel));
        buttons.Children.Add(CardButton("Save as template", DialogTone.Primary, "composer.templates.save", () => _ = picker.SaveAsync()));
        panel.Children.Add(buttons);
        AutomationProperties.SetAutomationId(panel, "composer.templates.card");
        return panel;
    }

    private UIElement TemplateRow(TemplatePickerState picker, MailTemplate template, int index)
    {
        var text = new StackPanel { Spacing = 1 };
        text.Children.Add(new TextBlock { Text = template.Name, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = AppResources.Brush("Text"), TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = template.Subject + (template.Category.Length > 0 ? "  ·  " + template.Category : string.Empty), FontSize = 11.5, Foreground = AppResources.Brush("Dim"), TextTrimming = TextTrimming.CharacterEllipsis });
        var delete = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Content = new Barbican.App.Controls.SIcon { Kind = "Trash", Size = 14 } };
        AutomationProperties.SetAutomationId(delete, $"composer.templates.{index}.delete");
        AutomationProperties.SetName(delete, "Delete " + template.Name);
        delete.Click += (_, _) => _ = picker.DeleteAsync(template);
        var insert = new Button { Style = (Style)Application.Current.Resources["Button.Action"], HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Height = 46, CornerRadius = new CornerRadius(10), Content = text };
        AutomationProperties.SetAutomationId(insert, $"composer.templates.{index}");
        AutomationProperties.SetName(insert, "Insert " + template.Name);
        insert.Click += (_, _) => _ = picker.InsertAsync(template);
        var row = new Grid { ColumnSpacing = 6 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(insert);
        Grid.SetColumn(delete, 1);
        row.Children.Add(delete);
        return row;
    }

    // ---- From and Options --------------------------------------------------------------------------------

    private UIElement FromCard(ComposerViewModel vm)
    {
        var panel = new StackPanel { Spacing = 8, MinWidth = 360 };
        panel.Children.Add(Heading("Send from"));
        foreach (var identity in vm.Identities)
        {
            var id = identity.Id;
            var selected = string.Equals(vm.SelectedIdentityId, id, StringComparison.Ordinal);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new Barbican.App.Controls.SIcon { Kind = selected ? "Check" : "Mail", Size = 14, Foreground = AppResources.Brush(selected ? "Accent" : "Faint") });
            row.Children.Add(new TextBlock { Text = identity.Label, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Style = (Style)Application.Current.Resources["Button.Action"], HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Height = 36, CornerRadius = new CornerRadius(10), Content = row };
            AutomationProperties.SetAutomationId(button, "composer.from.identity." + id);
            AutomationProperties.SetName(button, identity.Label);
            button.Click += (_, _) =>
            {
                vm.SelectedIdentityId = id;
                RefreshCards();
            };
            panel.Children.Add(button);
        }

        AddSenderEditors(panel, vm);
        panel.Children.Add(Aligned(CardButton("Done", DialogTone.Primary, "composer.from.done", () => ShowPanel(CardKind.None))));
        AutomationProperties.SetAutomationId(panel, "composer.from.card");
        return panel;
    }

    private static void AddSenderEditors(StackPanel panel, ComposerViewModel vm)
    {
        if (vm.CanTag)
        {
            panel.Children.Add(Body("Sub-address tag (user+tag@domain)"));
            var tag = new TextBox { PlaceholderText = "shop", Text = vm.SubAddressTag, FontSize = 13 };
            AutomationProperties.SetAutomationId(tag, "composer.from.tag");
            AutomationProperties.SetName(tag, "Sub-address tag");
            var note = new TextBlock { FontSize = 12, Foreground = AppResources.Brush("Dim"), TextWrapping = TextWrapping.Wrap, Text = vm.SubAddressError ?? vm.SubAddressPreview };
            AutomationProperties.SetAutomationId(note, "composer.from.tag.note");
            tag.TextChanged += (_, _) =>
            {
                vm.SubAddressTag = tag.Text;
                note.Text = vm.SubAddressError ?? vm.SubAddressPreview;
                note.Foreground = AppResources.Brush(vm.SubAddressError is null ? "Dim" : "RoseFg");
            };
            panel.Children.Add(tag);
            panel.Children.Add(note);
        }

        var edit = new CheckBox { Content = "Send as a different address", IsChecked = vm.FromOverrideEnabled, FontSize = 13 };
        AutomationProperties.SetAutomationId(edit, "composer.from.override");
        var name = new TextBox { PlaceholderText = "Name", Text = vm.FromOverrideName, FontSize = 13, Visibility = vm.FromOverrideEnabled ? Visibility.Visible : Visibility.Collapsed };
        var email = new TextBox { PlaceholderText = "address@example.org", Text = vm.FromOverrideEmail, FontSize = 13, Visibility = vm.FromOverrideEnabled ? Visibility.Visible : Visibility.Collapsed };
        AutomationProperties.SetAutomationId(name, "composer.from.override.name");
        AutomationProperties.SetAutomationId(email, "composer.from.override.email");
        edit.Click += (_, _) =>
        {
            vm.FromOverrideEnabled = edit.IsChecked == true;
            name.Visibility = email.Visibility = vm.FromOverrideEnabled ? Visibility.Visible : Visibility.Collapsed;
        };
        name.TextChanged += (_, _) => vm.FromOverrideName = name.Text;
        email.TextChanged += (_, _) => vm.FromOverrideEmail = email.Text;
        panel.Children.Add(edit);
        panel.Children.Add(name);
        panel.Children.Add(email);
    }

    private UIElement OptionsCard(ComposerViewModel vm)
    {
        var panel = new StackPanel { Spacing = 8, MinWidth = 300 };
        panel.Children.Add(Heading("Options"));
        var plain = new CheckBox { Content = "Plain text", IsChecked = vm.PlainText, FontSize = 13 };
        AutomationProperties.SetAutomationId(plain, "composer.options.plain");
        plain.Click += (_, _) => vm.PlainText = plain.IsChecked == true;
        var receipt = new CheckBox { Content = "Request a read receipt", IsChecked = vm.ReadReceipt, FontSize = 13 };
        AutomationProperties.SetAutomationId(receipt, "composer.options.receipt");
        receipt.Click += (_, _) => vm.ReadReceipt = receipt.IsChecked == true;
        panel.Children.Add(plain);
        panel.Children.Add(receipt);
        panel.Children.Add(Body("Priority"));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var p in new[] { MessagePriority.Normal, MessagePriority.High, MessagePriority.Low })
        {
            var value = p;
            var on = vm.Priority == p;
            var b = CardButton(p.ToString(), on ? DialogTone.Primary : DialogTone.Plain, "composer.options.priority." + p.ToString().ToLowerInvariant(), () =>
            {
                vm.Priority = value;
                RefreshCards();
            });
            row.Children.Add(b);
        }

        panel.Children.Add(row);
        panel.Children.Add(Aligned(CardButton("Done", DialogTone.Plain, "composer.options.done", () => ShowPanel(CardKind.None))));
        AutomationProperties.SetAutomationId(panel, "composer.options.card");
        return panel;
    }

    // ---- small builders ----------------------------------------------------------------------------------

    internal static TextBlock Heading(string text) => new() { Text = text, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = AppResources.Brush("Text"), TextWrapping = TextWrapping.Wrap };

    internal static TextBlock Body(string text, string? id = null)
    {
        var t = new TextBlock { Text = text, FontSize = 13, Foreground = AppResources.Brush("Dim"), TextWrapping = TextWrapping.Wrap };
        if (id is not null)
        {
            AutomationProperties.SetAutomationId(t, id);
        }

        return t;
    }

    private static FrameworkElement Aligned(FrameworkElement e)
    {
        e.HorizontalAlignment = HorizontalAlignment.Right;
        return e;
    }

    internal static Button CardButton(string label, DialogTone tone, string id, Action click, bool stretch = false)
    {
        var style = tone == DialogTone.Primary ? "Button.Primary" : "Button.Action";
        var button = new Button { Style = (Style)Application.Current.Resources[style], Content = label, Height = 34, CornerRadius = new CornerRadius(17), FontSize = 13 };
        if (tone == DialogTone.Danger)
        {
            button.Foreground = AppResources.Brush("RoseFg");
        }

        if (stretch)
        {
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
        }

        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => click();
        return button;
    }
}

// Mirrors: Bulwark components/email/link-dialog.tsx:60-129 and image-url-dialog.tsx:60-125 (a title, "mt-4 space-y-3" fields with a text-sm font-medium label over an Input, the error text-xs destructive,
// a hint text-xs muted; footer: "Remove link" ghost at the start, Cancel outline, Save or Insert primary and disabled until there is an address; Enter submits, Escape cancels); locales/en/common.json:1222-1228, 7761-7769; CP-06.
// The editor's link and picture dialogs live in the shared dialog host: the card, scrim, PopIn and focus handling are its own; the validation is LinkValidator (I-266, I-267).

using Barbican.App.Theme;
using Barbican.App.Views.Editor;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Editor;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Dialogs;

/// <summary>Draws <see cref="LinkDialogRequest"/> and <see cref="ImageUrlDialogRequest"/>.</summary>
public sealed class EditorDialogContent : IDialogContent
{
    // Bulwark's picture.invalid (the shared LinkValidator wording is the older one)
    internal const string PictureInvalid = "Enter a web address that starts with https://.";

    /// <inheritdoc />
    public bool CanDraw(object request) => request is LinkDialogRequest or ImageUrlDialogRequest;

    /// <inheritdoc />
    public bool OwnButtons => true;

    /// <inheritdoc />
    public Control? InitialFocus(UIElement content) => content is FrameworkElement { Tag: Control field } ? field : null;

    /// <inheritdoc />
    public UIElement Create(ActiveDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        return dialog.Request switch
        {
            LinkDialogRequest link => Link(dialog, link),
            ImageUrlDialogRequest picture => Picture(dialog, picture),
            _ => throw new InvalidOperationException("Not an editor dialog request."),
        };
    }

    private static StackPanel Link(ActiveDialog dialog, LinkDialogRequest request)
    {
        var text = Field("editor.link.text", "Text", request.Text, string.Empty);
        var href = Field("editor.link.href", "Link", request.Href, "Link address");
        var error = Note("editor.link.error", "Destructive");
        var save = DialogStyles.FooterButton("Save", DialogTone.Primary, "editor.link.save", () => { });
        void Validate()
        {
            var message = LinkValidator.LinkError(href.Text);
            error.Text = message ?? string.Empty;
            error.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
            save.IsEnabled = LinkValidator.CanSubmitLink(href.Text);
        }

        void Submit()
        {
            if (LinkValidator.CanSubmitLink(href.Text))
            {
                request.Apply(text.Text, href.Text.Trim());
                dialog.Complete(true);
            }
        }

        href.TextChanged += (_, _) => Validate();
        Keys(text, Submit, dialog);
        Keys(href, Submit, dialog);
        Validate();
        var footer = Footer();
        footer.Children.Add(DialogStyles.FooterButton("Cancel", DialogTone.Plain, "editor.link.cancel", () => dialog.Complete(null)));
        save.Click += (_, _) => Submit();
        footer.Children.Add(save);
        var footerRow = LinkFooter(footer, request.Remove, dialog);
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Labelled("Text", text));
        var linkBlock = Labelled("Link", href);
        linkBlock.Children.Add(error);
        panel.Children.Add(linkBlock);
        panel.Children.Add(footerRow);
        // with text already there, the address is what is left to give (link-dialog.tsx:44-46)
        panel.Tag = request.Text.Length > 0 ? href : text;
        AutomationProperties.SetAutomationId(panel, "editor.link.dialog");
        return panel;
    }

    // link-dialog.tsx:84-98: "Remove link" is a ghost button at the start of the footer ("me-auto"), the others stay at the end
    private static UIElement LinkFooter(StackPanel footer, Action? remove, ActiveDialog dialog)
    {
        if (remove is null)
        {
            return footer;
        }

        var removeButton = DialogStyles.FooterButton("Remove link", DialogTone.Plain, "editor.link.remove", () =>
        {
            remove();
            dialog.Complete(true);
        });
        removeButton.BorderThickness = new Thickness(0);
        var row = new Grid { Margin = footer.Margin };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Margin = new Thickness(0);
        row.Children.Add(removeButton);
        Grid.SetColumn(footer, 1);
        row.Children.Add(footer);
        return row;
    }

    private static StackPanel Picture(ActiveDialog dialog, ImageUrlDialogRequest request)
    {
        var address = Field("editor.imageurl.src", "Web address of the picture", string.Empty, string.Empty);
        var alt = Field("editor.imageurl.alt", "Description", string.Empty, string.Empty);
        var hint = Note("editor.imageurl.hint", "Dim");
        hint.Text = "The picture stays on the web and is shown from there.";
        hint.Visibility = Visibility.Visible;
        var error = Note("editor.imageurl.error", "Destructive");
        var insert = DialogStyles.FooterButton("Insert", DialogTone.Primary, "editor.imageurl.insert", () => { });
        insert.IsEnabled = false;
        var touched = false;
        void Show()
        {
            var bad = touched && address.Text.Trim().Length > 0 && LinkValidator.PictureAddress(address.Text) is null;
            error.Text = bad ? PictureInvalid : string.Empty;
            error.Visibility = bad ? Visibility.Visible : Visibility.Collapsed;
        }

        void Submit()
        {
            touched = true;
            Show();
            if (LinkValidator.PictureAddress(address.Text) is { } src)
            {
                request.Insert(src, alt.Text.Trim());
                dialog.Complete(true);
            }
        }

        address.TextChanged += (_, _) =>
        {
            insert.IsEnabled = address.Text.Trim().Length > 0;
            Show();
        };
        address.LostFocus += (_, _) =>
        {
            touched = true;
            Show();
        };
        Keys(address, Submit, dialog);
        Keys(alt, Submit, dialog);
        insert.Click += (_, _) => Submit();
        var footer = Footer();
        footer.Children.Add(DialogStyles.FooterButton("Cancel", DialogTone.Plain, "editor.imageurl.cancel", () => dialog.Complete(null)));
        footer.Children.Add(insert);
        var panel = new StackPanel { Spacing = 12 };
        var addressBlock = Labelled("Web address of the picture", address);
        addressBlock.Children.Add(hint);
        addressBlock.Children.Add(error);
        panel.Children.Add(addressBlock);
        panel.Children.Add(Labelled("Description", alt));
        panel.Children.Add(footer);
        panel.Tag = address;
        AutomationProperties.SetAutomationId(panel, "editor.imageurl.dialog");
        return panel;
    }

    private static TextBox Field(string id, string name, string text, string placeholder)
    {
        var box = new TextBox { Text = text, PlaceholderText = placeholder, FontSize = 14, IsSpellCheckEnabled = false };
        AutomationProperties.SetAutomationId(box, id);
        AutomationProperties.SetName(box, name);
        return box;
    }

    private static TextBlock Note(string id, string brush)
    {
        var note = new TextBlock { FontSize = 12, Foreground = AppResources.Brush(brush), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(note, id);
        return note;
    }

    // space-y-1.5: a label (text-sm font-medium) over its field
    private static StackPanel Labelled(string label, TextBox field)
    {
        var block = new StackPanel { Spacing = 6 };
        block.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Foreground = AppResources.Brush("Text") });
        block.Children.Add(field);
        return block;
    }

    private static StackPanel Footer() => new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Right,
        Spacing = DialogStyles.ButtonGap,
        Margin = new Thickness(0, DialogStyles.Bulwark ? 12 : 0, 0, 0),
    };

    // a TextBox keeps its keys from the shell, so Enter (the form's submit) and Escape (cancel) are read here
    private static void Keys(TextBox box, Action submit, ActiveDialog dialog) => box.KeyDown += (_, e) =>
    {
        if (e.Key == global::Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            submit();
        }
        else if (e.Key == global::Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            dialog.Complete(null);
        }
    };
}

// Mirrors: Bulwark components/email/rich-text-editor.tsx (size menu FONT_SIZES :70 of font-size.ts, TEXT_COLORS :413, TableSizePicker 6 x 8 :406), image-alt-dialog (link-dialog.tsx and image-url-dialog.tsx are dialogs of the shared host, Views/Dialogs/EditorDialogContent.cs),
// the link popover and recipient-mention.tsx (the list); each card is in-window, built in code, and closed by the host's scrim.

using System.Globalization;
using Barbican.App.Theme;
using Barbican.Core.Editor;
using Barbican.Presentation.Editor;
using Microsoft.UI.Xaml.Automation;
using Windows.UI;

namespace Barbican.App.Views.Editor;

/// <summary>The dropdown and dialog cards of the editor.</summary>
public static class EditorCards
{
    /// <summary>The sizes of Bulwark's size menu (px); 14 is "Normal".</summary>
    public static readonly IReadOnlyList<int> Sizes = [14, 16, 18, 20, 24, 32];

    /// <summary>Bulwark's preset text colours (2 x 8).</summary>
    public static readonly IReadOnlyList<string> Colors =
    [
        "#000000", "#5f6368", "#9aa0a6", "#c5221f", "#e8710a", "#f9ab00", "#188038", "#1967d2",
        "#7627bb", "#c2185b", "#795548", "#fa5252", "#fd7e14", "#40c057", "#4dabf7", "#e64980",
    ];

    public const int TableRows = 6;
    public const int TableCols = 8;

    public static FrameworkElement SizePicker(int? current, Action<int?> pick)
    {
        var panel = new StackPanel { MinWidth = 120 };
        foreach (var px in Sizes)
        {
            var normal = px == EditorCommandRunner.NormalSize;
            var selected = (current ?? EditorCommandRunner.NormalSize) == px;
            panel.Children.Add(EditorUi.Row("size." + px.ToString(CultureInfo.InvariantCulture), normal ? "Normal" : px.ToString(CultureInfo.InvariantCulture), selected, () => pick(normal ? null : px)));
        }

        return EditorUi.Card(panel);
    }

    public static FrameworkElement ColorPicker(string? current, Action<string?> pick)
    {
        var grid = new Grid { RowSpacing = 4, ColumnSpacing = 4 };
        for (var i = 0; i < Colors.Count / 2; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < Colors.Count; i++)
        {
            var hex = Colors[i];
            var swatch = new Button
            {
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Parse(hex)),
                BorderBrush = AppResources.Brush(string.Equals(current, hex, StringComparison.OrdinalIgnoreCase) ? "Accent" : "Line"),
                BorderThickness = new Thickness(string.Equals(current, hex, StringComparison.OrdinalIgnoreCase) ? 2 : 1),
                IsTabStop = false,
            };
            AutomationProperties.SetAutomationId(swatch, "editor.color." + hex[1..]);
            AutomationProperties.SetName(swatch, hex);
            swatch.Click += (_, _) => pick(hex);
            Grid.SetRow(swatch, i / (Colors.Count / 2));
            Grid.SetColumn(swatch, i % (Colors.Count / 2));
            grid.Children.Add(swatch);
        }

        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(grid);
        stack.Children.Add(EditorUi.Row("color.default", "Default colour", current is null, () => pick(null)));
        return EditorUi.Card(stack);
    }

    public static FrameworkElement BlockPicker(BlockKind current, Action<EditorCommand> pick)
    {
        var panel = new StackPanel { MinWidth = 150 };
        (string Label, EditorCommand Command, bool On)[] rows =
        [
            ("Normal text", EditorCommand.Paragraph, current is BlockKind.Para),
            ("Heading 1", EditorCommand.Heading1, current is BlockKind.Heading { Level: 1 }),
            ("Heading 2", EditorCommand.Heading2, current is BlockKind.Heading { Level: 2 }),
            ("Heading 3", EditorCommand.Heading3, current is BlockKind.Heading { Level: 3 }),
            ("Quote", EditorCommand.Quote, current is BlockKind.Quote),
            ("Code block", EditorCommand.CodeBlock, current is BlockKind.CodeBlock),
        ];
        foreach (var (label, command, on) in rows)
        {
            panel.Children.Add(EditorUi.Row("block." + command, label, on, () => pick(command)));
        }

        return EditorUi.Card(panel);
    }

    public static FrameworkElement AlignPicker(Align current, Action<EditorCommand> pick)
    {
        var panel = new StackPanel { MinWidth = 130 };
        (string Label, EditorCommand Command, bool On)[] rows =
        [
            ("Align left", EditorCommand.AlignLeft, current == Barbican.Core.Editor.Align.Left),
            ("Center", EditorCommand.AlignCenter, current == Barbican.Core.Editor.Align.Center),
            ("Align right", EditorCommand.AlignRight, current == Barbican.Core.Editor.Align.Right),
            ("Justify", EditorCommand.AlignJustify, current == Barbican.Core.Editor.Align.Justify),
        ];
        foreach (var (label, command, on) in rows)
        {
            panel.Children.Add(EditorUi.Row("align." + command, label, on, () => pick(command)));
        }

        return EditorUi.Card(panel);
    }

    public static FrameworkElement ImagePicker(Action fromFile, Action fromUrl)
    {
        var panel = new StackPanel { MinWidth = 170 };
        panel.Children.Add(EditorUi.Row("image.file", "From this computer…", false, fromFile));
        panel.Children.Add(EditorUi.Row("image.url", "From a web address…", false, fromUrl));
        return EditorUi.Card(panel);
    }

    /// <summary>The 6 x 8 grid; hovering lights the rectangle from the corner, a click inserts it (Bulwark TableSizePicker).</summary>
    public static FrameworkElement TableGrid(Action<int, int> pick)
    {
        var label = EditorUi.Label("Insert table", dim: true);
        var cells = new Button[TableRows, TableCols];
        var grid = new Grid { RowSpacing = 2, ColumnSpacing = 2 };
        for (var c = 0; c < TableCols; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        for (var r = 0; r < TableRows; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        void Light(int rows, int cols)
        {
            label.Text = rows > 0 ? $"{rows} × {cols}" : "Insert table";
            for (var r = 0; r < TableRows; r++)
            {
                for (var c = 0; c < TableCols; c++)
                {
                    cells[r, c].Background = AppResources.Brush(r < rows && c < cols ? "Accent" : "Chip");
                }
            }
        }

        for (var r = 0; r < TableRows; r++)
        {
            for (var c = 0; c < TableCols; c++)
            {
                var (row, col) = (r + 1, c + 1);
                var cell = new Button { Width = 20, Height = 20, Padding = new Thickness(0), CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(0), IsTabStop = false, Background = AppResources.Brush("Chip") };
                AutomationProperties.SetAutomationId(cell, $"editor.table.{row}x{col}");
                AutomationProperties.SetName(cell, $"{row} by {col} table");
                cell.PointerEntered += (_, _) => Light(row, col);
                cell.Click += (_, _) => pick(row, col);
                cells[r, c] = cell;
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }

        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(grid);
        stack.Children.Add(label);
        return EditorUi.Card(stack);
    }

    /// <summary>The alt-text card of an image.</summary>
    public static FrameworkElement AltText(string alt, Action<string> apply, Action cancel)
    {
        var box = EditorUi.Field("alt.text", "Alt text", alt);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(EditorUi.Action("alt.cancel", "Cancel", cancel));
        buttons.Children.Add(EditorUi.Action("alt.save", "Save", () => apply(box.Text.Trim()), primary: true));
        var stack = new StackPanel { Spacing = 8, MinWidth = 300 };
        stack.Children.Add(EditorUi.Label("Describe the picture for people who cannot see it"));
        stack.Children.Add(box);
        stack.Children.Add(buttons);
        return EditorUi.Card(stack);
    }

    /// <summary>The popover under a link the caret is in: its address, Edit, Open, Remove (Esc closes only this).</summary>
    public static FrameworkElement LinkPopover(string url, Action edit, Action open, Action copy, Action remove)
    {
        var address = new TextBlock { Text = url, FontSize = 12, Foreground = AppResources.Brush("SkyFg"), MaxWidth = 280, TextTrimming = TextTrimming.CharacterEllipsis };
        AutomationProperties.SetAutomationId(address, "editor.linkpop.url");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        row.Children.Add(EditorUi.Action("linkpop.edit", "Edit", edit));
        row.Children.Add(EditorUi.Action("linkpop.open", "Open", open));
        row.Children.Add(EditorUi.Action("linkpop.copy", "Copy", copy));
        row.Children.Add(EditorUi.Action("linkpop.remove", "Remove", remove));
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(address);
        stack.Children.Add(row);
        return EditorUi.Card(stack);
    }

    /// <summary>The @ list: the highlighted row, name first (what will be inserted), then the address.</summary>
    public static FrameworkElement Mentions(IReadOnlyList<MentionCandidate> items, int selected, Action<MentionCandidate> pick)
    {
        var panel = new StackPanel { MinWidth = 260, MaxWidth = 360 };
        AutomationProperties.SetAutomationId(panel, "editor.mentions");
        for (var i = 0; i < items.Count && i < 8; i++)
        {
            var item = items[i];
            var label = $"@{MentionTrigger.Label(item)}  {item.Email}";
            var row = EditorUi.Row("mention." + i.ToString(CultureInfo.InvariantCulture), label, i == selected, () => pick(item));
            panel.Children.Add(row);
        }

        return EditorUi.Card(panel);
    }

    private static Color Parse(string hex) => Color.FromArgb(255, byte.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture), byte.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture), byte.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
}

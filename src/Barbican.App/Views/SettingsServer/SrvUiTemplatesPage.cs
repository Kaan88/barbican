// Mirrors: Bulwark components/templates/template-manager-modal.tsx and template-form.tsx (name, subject, body, rich or plain, delete asks, {{placeholders}}), account-template-sync.tsx; I-289, I-290. Export shows the document as text and import takes pasted text: no file dialog is opened from the page. Owner srv-ui.

using Barbican.Presentation.Settings;
using Barbican.Presentation.Settings.Server;
using static Barbican.App.Views.Calendar.Editors.CalendarCardKit;

namespace Barbican.App.Views.SettingsServer;

/// <summary>The Templates settings page.</summary>
internal sealed class SrvUiTemplatesPage : SrvUiPage
{
    private readonly SrvUiTemplatesViewModel _model;
    private string _importText = string.Empty;

    public SrvUiTemplatesPage(SrvUiTemplatesViewModel model)
        : base(SettingsPageIds.Templates, "Templates", "Saved messages you can insert while writing. They are kept on your account, so every device has them.")
    {
        _model = model;
        _model.PropertyChanged += (_, _) => Invalidate();
    }

    protected override Task LoadAsync() => _model.LoadAsync(CancellationToken.None);

    protected override void Render()
    {
        Reset();
        foreach (var t in _model.Items)
        {
            var id = t.Id;
            var picked = string.Equals(id, _model.SelectedId, StringComparison.Ordinal);
            var label = new StackPanel { Spacing = 1 };
            label.Children.Add(SrvUiKit.Strong(t.Name));
            label.Children.Add(SrvUiKit.Dim(t.Subject.Length == 0 ? "(no subject)" : t.Subject));
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(label);
            var edit = SrvUiKit.Small(picked ? "Editing" : "Edit", "srv.templates.edit." + id, () => _model.Select(id));
            Grid.SetColumn(edit, 1);
            grid.Children.Add(edit);
            var card = SrvUiKit.Card(grid, "srv.templates.row." + id);
            if (picked)
            {
                card.BorderBrush = Barbican.App.Theme.AppResources.Brush("Accent");
                card.BorderThickness = new Thickness(1.5);
            }

            Body.Children.Add(card);
        }

        Body.Children.Add(SrvUiKit.Row(SrvUiKit.Small("New template", "srv.templates.new", _model.StartNew), SrvUiKit.Small("Export", "srv.templates.export", () => SrvUiKit.Run(() => _model.ExportAsync(CancellationToken.None)))));
        Body.Children.Add(Form());
        if (_model.ExportText.Length > 0)
        {
            Body.Children.Add(SrvUiKit.Section("Export"));
            Body.Children.Add(new TextBox { Text = _model.ExportText, IsReadOnly = true, AcceptsReturn = true, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas, monospace"), FontSize = 12, MaxHeight = 220 });
        }

        Body.Children.Add(SrvUiKit.Section("Import"));
        Body.Children.Add(Body13("Paste a template export, then import it. Names that exist get a number."));
        Body.Children.Add(Text("srv.templates.import-text", _importText, v => _importText = v, multiline: true, placeholder: "{ \"templates\": [ ... ] }"));
        Body.Children.Add(SrvUiKit.Small("Import", "srv.templates.import", () => SrvUiKit.Run(() => _model.ImportAsync(_importText, CancellationToken.None))));
        if (_model.Message.Length > 0)
        {
            Body.Children.Add(SrvUiKit.Dim(_model.Message));
        }
    }

    private static TextBlock Body13(string text) => SrvUiKit.Dim(text);

    private Border Form()
    {
        var form = new StackPanel { Spacing = 6 };
        form.Children.Add(SrvUiKit.Section(_model.IsNew ? "New template" : "Edit template"));
        form.Children.Add(Label("Name"));
        form.Children.Add(Text("srv.templates.name", _model.Name, v => _model.Name = v, placeholder: "Thanks"));
        form.Children.Add(Label("Subject"));
        form.Children.Add(Text("srv.templates.subject", _model.Subject, v => _model.Subject = v));
        form.Children.Add(Label("Body"));
        form.Children.Add(Text("srv.templates.body", _model.Body, v => _model.Body = v, multiline: true));
        form.Children.Add(Check("srv.templates.html", "Rich text (the body is HTML)", _model.IsHtml, v => _model.IsHtml = v));
        var holders = _model.Placeholders;
        form.Children.Add(SrvUiKit.Dim(holders.Count == 0 ? "Placeholders: none. Write {{name}} to fill something in when you insert the template." : "Placeholders: " + string.Join(", ", holders.Select(h => "{{" + h + "}}"))));
        form.Children.Add(SrvUiKit.Error(_model.Error));
        var buttons = new List<UIElement> { Btn("Save", Barbican.Presentation.Compose.DialogTone.Primary, "srv.templates.save", () => SrvUiKit.Run(() => _model.SaveAsync(CancellationToken.None))) };
        if (!_model.IsNew)
        {
            buttons.Insert(0, Btn("Delete", Barbican.Presentation.Compose.DialogTone.Danger, "srv.templates.delete", () => SrvUiKit.Run(DeleteAsync)));
        }

        form.Children.Add(Buttons([.. buttons]));
        return SrvUiKit.Card(form, "srv.templates.form");
    }

    private async Task DeleteAsync()
    {
        if (await SrvUiKit.AskAsync("Delete template", "Delete the template \"" + _model.Name + "\"?", "Delete").ConfigureAwait(true))
        {
            await _model.DeleteAsync(CancellationToken.None).ConfigureAwait(true);
        }
    }
}

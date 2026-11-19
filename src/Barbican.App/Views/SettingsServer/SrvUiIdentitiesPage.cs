// Mirrors: Bulwark components/settings/identity-settings.tsx and identity-manager-modal.tsx (the list in the user's order, the first is the default sender; add, edit name, address, reply-to, Bcc, text and HTML signature; delete asks), Rust identities; I-291. Owner srv-ui.

using Barbican.Presentation.Settings;
using Barbican.Presentation.Settings.Server;
using static Barbican.App.Views.Calendar.Editors.CalendarCardKit;

namespace Barbican.App.Views.SettingsServer;

/// <summary>The Identities settings page.</summary>
internal sealed class SrvUiIdentitiesPage : SrvUiPage
{
    private readonly SrvUiIdentitiesViewModel _model;

    public SrvUiIdentitiesPage(SrvUiIdentitiesViewModel model)
        : base(SettingsPageIds.Identities, "Identities", "The addresses you send from. The first one is the default sender.")
    {
        _model = model;
        _model.PropertyChanged += (_, _) => Invalidate();
    }

    protected override Task LoadAsync() => _model.LoadAsync(CancellationToken.None);

    protected override void Render()
    {
        Reset();
        var generation = _model.AccountGeneration;
        var items = _model.Items;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var id = item.Id;
            var picked = string.Equals(id, _model.SelectedId, StringComparison.Ordinal);
            var label = new StackPanel { Spacing = 1 };
            label.Children.Add(SrvUiKit.Strong((item.Name.Length == 0 ? item.Email : item.Name) + (i == 0 ? "  (default)" : string.Empty)));
            label.Children.Add(SrvUiKit.Dim(item.Email));
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(label);
            var index = i;
            var buttons = SrvUiKit.Row(
                Enable(SrvUiKit.Small("Up", "srv.identities.up." + id, () => RunCurrent(generation, () => _model.MoveAsync(id, -1, CancellationToken.None))), index > 0),
                Enable(SrvUiKit.Small("Down", "srv.identities.down." + id, () => RunCurrent(generation, () => _model.MoveAsync(id, 1, CancellationToken.None))), index < items.Count - 1),
                SrvUiKit.Small(picked ? "Editing" : "Edit", "srv.identities.edit." + id, () => { if (_model.IsCurrentGeneration(generation)) _model.Select(id); }));
            Grid.SetColumn(buttons, 1);
            grid.Children.Add(buttons);
            var card = SrvUiKit.Card(grid, "srv.identities.row." + id);
            if (picked)
            {
                card.BorderBrush = Barbican.App.Theme.AppResources.Brush("Accent");
                card.BorderThickness = new Thickness(1.5);
            }

            Body.Children.Add(card);
        }

        Body.Children.Add(SrvUiKit.Small("New identity", "srv.identities.new", () => { if (_model.IsCurrentGeneration(generation)) _model.StartNew(); }));
        Body.Children.Add(Form(generation));
    }

    private void RunCurrent(long generation, Func<Task> action)
    {
        if (_model.IsCurrentGeneration(generation)) SrvUiKit.Run(action);
    }

    private static Button Enable(Button button, bool on)
    {
        button.IsEnabled = on;
        return button;
    }

    private Border Form(long generation)
    {
        var selected = _model.SelectedId;
        bool Current() => _model.IsCurrentGeneration(generation) && string.Equals(selected, _model.SelectedId, StringComparison.Ordinal);
        void Edit(Action action) { if (Current()) action(); }
        var form = new StackPanel { Spacing = 6 };
        form.Children.Add(SrvUiKit.Section(_model.IsNew ? "New identity" : "Edit identity"));
        form.Children.Add(Label("Name"));
        form.Children.Add(Text("srv.identities.name", _model.Name, v => Edit(() => _model.Name = v), placeholder: "Your name"));
        form.Children.Add(Label("Address"));
        form.Children.Add(Text("srv.identities.email", _model.Email, v => Edit(() => _model.Email = v), placeholder: "you@example.org"));
        form.Children.Add(Label("Reply-to (optional, comma separated)"));
        form.Children.Add(Text("srv.identities.reply-to", _model.ReplyTo, v => Edit(() => _model.ReplyTo = v)));
        form.Children.Add(Label("Bcc every message to (optional, comma separated)"));
        form.Children.Add(Text("srv.identities.bcc", _model.Bcc, v => Edit(() => _model.Bcc = v)));
        form.Children.Add(Label("Signature (plain text)"));
        form.Children.Add(Text("srv.identities.signature", _model.TextSignature, v => Edit(() => _model.TextSignature = v), multiline: true));
        form.Children.Add(Label("Signature (HTML)"));
        form.Children.Add(Text("srv.identities.html-signature", _model.HtmlSignature, v => Edit(() => _model.HtmlSignature = v), multiline: true));
        form.Children.Add(SrvUiKit.Error(_model.Error));
        var buttons = new List<UIElement>
        {
            Btn("Save", Barbican.Presentation.Compose.DialogTone.Primary, "srv.identities.save", () => { if (Current()) SrvUiKit.Run(() => _model.SaveAsync(CancellationToken.None)); }),
        };
        if (!_model.IsNew)
        {
            buttons.Insert(0, Btn("Delete", Barbican.Presentation.Compose.DialogTone.Danger, "srv.identities.delete", () => { if (Current()) SrvUiKit.Run(() => DeleteAsync(generation, selected)); }, _model.CanDelete));
        }

        form.Children.Add(Buttons([.. buttons]));
        return SrvUiKit.Card(form, "srv.identities.form");
    }

    private async Task DeleteAsync(long generation, string? id)
    {
        if (await SrvUiKit.AskAsync("Delete identity", "Delete the identity " + _model.Email + "? Drafts that use it keep their text.", "Delete").ConfigureAwait(true) && _model.IsCurrentGeneration(generation) && string.Equals(id, _model.SelectedId, StringComparison.Ordinal))
        {
            await _model.DeleteAsync(CancellationToken.None).ConfigureAwait(true);
        }
    }
}

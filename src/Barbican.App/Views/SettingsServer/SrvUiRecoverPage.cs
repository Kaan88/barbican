// Mirrors: Bulwark components/settings/recover-deleted-settings.tsx (deleted items the server still keeps, a restore per item), Rust server_settings.rs:137-152; I-438. Without Stalwart's archive the page says so. Owner srv-ui.

using System.Globalization;
using Barbican.Presentation.Settings;
using Barbican.Presentation.Settings.Server;

namespace Barbican.App.Views.SettingsServer;

/// <summary>The Recover deleted items settings page.</summary>
internal sealed class SrvUiRecoverPage : SrvUiPage
{
    private readonly SrvUiRecoverViewModel _model;

    public SrvUiRecoverPage(SrvUiRecoverViewModel model)
        : base(SettingsPageIds.Recover, "Recover deleted items", "Messages you deleted for good are kept by the server for a while. Restore one and it returns to the folder it was in.")
    {
        _model = model;
        _model.PropertyChanged += (_, _) => Invalidate();
    }

    protected override Task LoadAsync() => _model.Available ? _model.LoadAsync(CancellationToken.None) : Task.CompletedTask;

    protected override void Render()
    {
        Reset();
        if (!_model.Available)
        {
            Body.Children.Add(SrvUiKit.Card(SrvUiKit.Dim("This server does not keep deleted items, so there is nothing to restore."), "srv.recover.unavailable"));
            return;
        }

        Body.Children.Add(SrvUiKit.Error(_model.Error));
        if (_model.Message.Length > 0)
        {
            Body.Children.Add(SrvUiKit.Dim(_model.Message));
        }

        if (_model.Items.Count == 0 && !_model.IsLoading)
        {
            Body.Children.Add(SrvUiKit.Dim("No deleted items to restore."));
        }

        foreach (var item in _model.Items)
        {
            var id = item.Id;
            var generation = _model.AccountGeneration;
            var label = new StackPanel { Spacing = 1 };
            label.Children.Add(SrvUiKit.Strong(item.Subject.Length == 0 ? "(no subject)" : item.Subject));
            label.Children.Add(SrvUiKit.Dim(item.ArchivedAt is { } at ? "Deleted " + at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "Deleted"));
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(label);
            var restore = SrvUiKit.Small("Restore", "srv.recover.restore." + id, () => SrvUiKit.Run(() => _model.RestoreAsync(id, generation, CancellationToken.None)));
            Grid.SetColumn(restore, 1);
            grid.Children.Add(restore);
            Body.Children.Add(SrvUiKit.Card(grid, "srv.recover.row." + id));
        }
    }
}

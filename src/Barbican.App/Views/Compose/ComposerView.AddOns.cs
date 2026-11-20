#if WINDOWS
using Barbican.App.Plugins;
using Barbican.App.Shell;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Slots;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Compose;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Compose;

public sealed partial class ComposerView
{
    private PluginActionRegistration? _addOnRegistrations;
    private PluginBackgroundRuntimes? _addOnRuntimes;

    private void WatchAddOnActions()
    {
        if (_addOnRegistrations is not null) return;
        _addOnRegistrations = ViewHost.Get<PluginActionRegistration>();
        _addOnRuntimes = ViewHost.Get<PluginBackgroundRuntimes>();
        _addOnRegistrations.Changed += OnAddOnActionsChanged;
        _addOnRuntimes.RunningChanged += OnAddOnActionsChanged;
        RefreshAddOnActions();
    }

    private void UnwatchAddOnActions()
    {
        if (_addOnRegistrations is null || _addOnRuntimes is null) return;
        _addOnRegistrations.Changed -= OnAddOnActionsChanged;
        _addOnRuntimes.RunningChanged -= OnAddOnActionsChanged;
        _addOnRegistrations = null;
        _addOnRuntimes = null;
    }

    private void OnAddOnActionsChanged(object? sender, EventArgs args) =>
        _ = DispatcherQueue.TryEnqueue(RefreshAddOnActions);

    private void RefreshAddOnActions()
    {
        if (_addOnRegistrations is null || _addOnRuntimes is null) return;
        var draft = _vm;
        var actions = draft is null ? [] : _addOnRegistrations.Actions(PluginActionRegistration.ComposerHeader)
            .Where(action => _addOnRuntimes.ActiveRuntime(action.Owner, draft.AccountKey, "ui:composer-toolbar")
                ?.Activation.Hooks.Contains("onAction", StringComparer.Ordinal) == true).ToArray();
        var overflow = ActualWidth < 500 || actions.Length > 3;
        AddOnActions.Children.Clear();
        AddOnMenu.Items.Clear();
        AddOnActions.Visibility = actions.Length > 0 && !overflow ? Visibility.Visible : Visibility.Collapsed;
        AddOnMore.Visibility = actions.Length > 0 && overflow ? Visibility.Visible : Visibility.Collapsed;
        if (actions.Length == 0) return;
        if (overflow)
        {
            foreach (var action in actions)
            {
                var item = new MenuFlyoutItem { Text = action.Action.Label, Icon = ActionIcon(action.Action.Icon) };
                SetActionAutomation(item, action);
                item.Click += (_, _) => _ = InvokeAddOnActionAsync(action, draft!);
                AddOnMenu.Items.Add(item);
            }
            return;
        }
        foreach (var action in actions)
        {
            var button = new Button { Content = ActionIcon(action.Action.Icon), Width = 32, Height = 32, Padding = new Thickness(0) };
            SetActionAutomation(button, action);
            Barbican.App.Motion.Tip.SetText(button, action.Action.Label);
            button.Click += (_, _) => _ = InvokeAddOnActionAsync(action, draft!);
            AddOnActions.Children.Add(button);
        }
    }

    private static SymbolIcon ActionIcon(string? icon) => new(icon switch
    {
        "folder" => Symbol.Folder,
        "mail" => Symbol.Mail,
        "edit" => Symbol.Edit,
        "print" => Symbol.Print,
        _ => Symbol.Add,
    });

    private static void SetActionAutomation(DependencyObject control, PluginActionContribution item)
    {
        AutomationProperties.SetName(control, item.Action.Label);
        AutomationProperties.SetAutomationId(control, $"composer.addon.{item.Owner.Package.Id}.{item.Action.Id}");
    }

    private async Task InvokeAddOnActionAsync(PluginActionContribution item, ComposerViewModel draft)
    {
        if (!IsActionDraftCurrent(draft) || _addOnRegistrations?.Contains(item) != true ||
            _addOnRuntimes?.ActiveRuntime(item.Owner, draft.AccountKey, "ui:composer-toolbar") is not { } runtime) return;
        var ct = item.Owner.Lifetime;
        try
        {
            var installed = await ViewHost.Get<PluginPackageHost>().FindAsync(item.Owner.Package.Id, ct).ConfigureAwait(true);
            if (!installed.IsOk || installed.Value is not { Enabled: true, Grant: { Enabled: true } grant } ||
                grant.Package != item.Owner.Package || grant.Generation != item.Owner.GrantGeneration ||
                !grant.AccountKeys.Contains(draft.AccountKey) || !grant.Permissions.Contains("ui:composer-toolbar") ||
                !IsActionDraftCurrent(draft) || !item.Owner.IsAlive || _addOnRegistrations?.Contains(item) != true ||
                !ViewHost.Get<PluginComposerAccess>().Expose(item.Owner, draft.Id, draft.AccountKey)) return;
            var values = new PluginValueBuilder();
            var context = values.Object(("composerId", values.String(draft.Id)));
            var payload = values.Object(("surface", values.String(item.Slot)), ("actionId", values.String(item.Action.Id)), ("context", context));
            var result = await runtime.InvokeHookAsync("onAction", values.Build(values.Array(payload)), TimeSpan.FromSeconds(30), ct).ConfigureAwait(true);
            if (!result.IsOk) _ = ViewHost.Find<IToastService>()?.Show(new ToastRequest("The add-on action could not run.", IsError: true));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private bool IsActionDraftCurrent(ComposerViewModel draft) =>
        ReferenceEquals(_vm, draft) && ReferenceEquals(_service.Current, draft) && !draft.IsClosed && !draft.IsSending;
}
#endif

#if WINDOWS
// Bulwark components/email/email-viewer.tsx:2212,4859; lib/plugin-sandbox/loader.ts:444-452.
using Barbican.App.Plugins;
using Barbican.App.Plugins.Sandbox;
using Barbican.App.Shell;
using Barbican.Core.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Slots;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Mail;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Reader;

public sealed partial class ReaderView
{
    private const string ReaderActionsSlot = "reader-actions";
    private PluginActionRegistration? _readerAddOnRegistrations;
    private PluginBackgroundRuntimes? _readerAddOnRuntimes;
    private IActiveMail? _readerAddOnMail;

    private void WatchReaderAddOns()
    {
        if (_readerAddOnRegistrations is not null) return;
        _readerAddOnRegistrations = ViewHost.Get<PluginActionRegistration>();
        _readerAddOnRuntimes = ViewHost.Get<PluginBackgroundRuntimes>();
        _readerAddOnMail = ViewHost.Get<IActiveMail>();
        _readerAddOnRegistrations.Changed += ReaderAddOnsChanged;
        _readerAddOnRuntimes.RunningChanged += ReaderAddOnsChanged;
        _readerAddOnMail.Changed += ReaderAddOnsChanged;
        RefreshReaderAddOns();
    }

    private void UnwatchReaderAddOns()
    {
        if (_readerAddOnRegistrations is null || _readerAddOnRuntimes is null || _readerAddOnMail is null) return;
        _readerAddOnRegistrations.Changed -= ReaderAddOnsChanged;
        _readerAddOnRuntimes.RunningChanged -= ReaderAddOnsChanged;
        _readerAddOnMail.Changed -= ReaderAddOnsChanged;
        _readerAddOnRegistrations = null;
        _readerAddOnRuntimes = null;
        _readerAddOnMail = null;
        ReaderAddOnsMenu.Items.Clear();
        ReaderAddOnButtons.Children.Clear();
        ReaderAddOnButtons.Visibility = Visibility.Collapsed;
        ReaderAddOns.Visibility = Visibility.Collapsed;
    }

    private void ReaderAddOnsChanged(object? sender, EventArgs args) =>
        _ = DispatcherQueue.TryEnqueue(RefreshReaderAddOns);

    private void RefreshReaderAddOns()
    {
        if (_readerAddOnRegistrations is null || _readerAddOnRuntimes is null) return;
        ReaderAddOnsMenu.Items.Clear();
        ReaderAddOnButtons.Children.Clear();
        ReaderAddOnButtons.Visibility = Visibility.Collapsed;
        ReaderAddOns.Visibility = Visibility.Collapsed;
        var snapshot = Reader.Current;
        var mail = _readerAddOnMail;
        if (mail?.Mail is not { } session ||
            snapshot.Body is null || string.IsNullOrEmpty(snapshot.EmailId) || snapshot.ListAccountKey is not { } account ||
            !string.Equals(account, mail.AccountKey, StringComparison.Ordinal)) return;
        var actions = _readerAddOnRegistrations.Actions(ReaderActionsSlot).Where(action =>
            _readerAddOnRuntimes.ActiveRuntime(action.Owner, account, "ui:toolbar")?.Activation.Hooks.Contains("onAction", StringComparer.Ordinal) == true).ToArray();
        var overflow = UseReaderAddOnOverflow(ContentGrid.ActualWidth) || actions.Length > 3;
        foreach (var action in actions)
        {
            var icon = new SymbolIcon(action.Action.Icon switch
            {
                "file" => Symbol.Document,
                "printer" or "print" => Symbol.Print,
                "folder" => Symbol.Folder,
                _ => Symbol.Add,
            });
            var id = $"reader.addon.{action.Owner.Package.Id}.{action.Action.Id}";
            if (overflow)
            {
                var item = new MenuFlyoutItem { Text = action.Action.Label, Icon = icon };
                AutomationProperties.SetName(item, action.Action.Label);
                AutomationProperties.SetAutomationId(item, id);
                item.Click += (_, _) => _ = InvokeReaderAddOnAsync(action, snapshot, account, session);
                ReaderAddOnsMenu.Items.Add(item);
            }
            else
            {
                var button = new Button
                {
                    Content = icon, Width = 32, Height = Look.ReaderActionHeight, Padding = new Thickness(0),
                    Style = (Style)Application.Current.Resources["Button.Icon"],
                };
                AutomationProperties.SetName(button, action.Action.Label);
                AutomationProperties.SetAutomationId(button, id);
                Barbican.App.Motion.Tip.SetText(button, action.Action.Label);
                button.Click += (_, _) => _ = InvokeReaderAddOnAsync(action, snapshot, account, session);
                ReaderAddOnButtons.Children.Add(button);
            }
        }
        if (ReaderAddOnsMenu.Items.Count > 0) ReaderAddOns.Visibility = Visibility.Visible;
        if (ReaderAddOnButtons.Children.Count > 0) ReaderAddOnButtons.Visibility = Visibility.Visible;
    }

    private static bool UseReaderAddOnOverflow(double width) => width < 690;

    private bool ReaderActionCurrent(ReaderSnapshot snapshot, string account, IMailService mailService) =>
        IsLoaded && ReferenceEquals(Reader.Current, snapshot) && _readerAddOnMail is { } mail &&
        ReferenceEquals(mail.Mail, mailService) && string.Equals(mail.AccountKey, account, StringComparison.Ordinal) &&
        string.Equals(snapshot.ListAccountKey, account, StringComparison.Ordinal);

    private async Task InvokeReaderAddOnAsync(PluginActionContribution action, ReaderSnapshot snapshot, string account, IMailService mailService)
    {
        if (!ReaderActionCurrent(snapshot, account, mailService) || _readerAddOnRegistrations?.Contains(action) != true ||
            _readerAddOnRuntimes?.ActiveRuntime(action.Owner, account, "ui:toolbar") is not IPluginFencedHookRuntime runtime) return;
        var sessions = ViewHost.Get<IPluginSessionSource>();
        var captured = sessions.CaptureActive();
        if (!captured.IsOk || captured.Value is not { } owner ||
            !string.Equals(owner.LocalAccountKey, account, StringComparison.Ordinal) ||
            !ReferenceEquals(owner.Session.Mail, mailService)) return;
        IReadOnlyList<PluginSessionCapture> owners = [owner];
        var ct = action.Owner.Lifetime;
        try
        {
            var installed = await ViewHost.Get<PluginPackageHost>().FindAsync(action.Owner.Package.Id, ct).ConfigureAwait(true);
            if (!installed.IsOk || installed.Value is not { Enabled: true, Grant: { Enabled: true } grant } ||
                grant.Package != action.Owner.Package || grant.Generation != action.Owner.GrantGeneration ||
                !grant.AccountKeys.Contains(account) || !grant.Permissions.Contains("ui:toolbar") ||
                !ReaderActionCurrent(snapshot, account, mailService) || !action.Owner.IsAlive || !sessions.IsCurrent(owner) ||
                _readerAddOnRegistrations?.Contains(action) != true ||
                !ReferenceEquals(_readerAddOnRuntimes?.ActiveRuntime(action.Owner, account, "ui:toolbar"), runtime)) return;
            var values = new PluginValueBuilder();
            // Upstream's ordinary reader supplies only emailId; projection removes it without email:read.
            var context = grant.Permissions.Contains("email:read")
                ? values.Object(("emailId", values.String(snapshot.EmailId))) : values.Object();
            var payload = values.Object(("surface", values.String(ReaderActionsSlot)), ("actionId", values.String(action.Action.Id)), ("context", context));
            var result = await runtime.InvokeHookWhileCurrentAsync("onAction", values.Build(values.Array(payload)), TimeSpan.FromSeconds(30),
                write => sessions.TryRunWhileCurrent(owners, () => ReaderActionCurrent(snapshot, account, mailService) &&
                    _readerAddOnRegistrations?.Contains(action) == true && write()), ct).ConfigureAwait(true);
            if (!result.IsOk && ReaderActionCurrent(snapshot, account, mailService) && sessions.IsCurrent(owner))
                _ = ViewHost.Find<IToastService>()?.Show(new ToastRequest("The add-on action could not run.", IsError: true));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
#endif

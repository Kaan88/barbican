// Bulwark components/email/classification-label-picker.tsx; selection is revalidated by the send pipeline.
using System.ComponentModel;
using Barbican.Presentation.Compose;
#if WINDOWS
using Barbican.App.Shell;
using Barbican.Core.Accounts;
using Barbican.Core.Compose;
using Barbican.Core.OrganizationPolicy;
using Barbican.Core.Compose.Recipients;
using Microsoft.UI.Xaml.Automation;
#endif

namespace Barbican.App.Views.Compose;

public sealed partial class ComposerView
{
#if WINDOWS
    private CancellationTokenSource? _classificationLoad;
    private ComposerViewModel? _classificationVm;
    private string? _loadingIdentity;
    private IAccountSession? _loadingOwner;

    private void StopClassification()
    {
        var pending = _classificationLoad;
        _classificationLoad = null;
        _loadingIdentity = null;
        _loadingOwner = null;
        pending?.Cancel();
        pending?.Dispose();
    }

    private void RefreshClassification()
    {
        if (!ReferenceEquals(_classificationVm, _vm))
        {
            if (_classificationVm is not null) _classificationVm.PropertyChanged -= OnClassificationVmChanged;
            _classificationVm = _vm;
            if (_classificationVm is not null) _classificationVm.PropertyChanged += OnClassificationVmChanged;
        }
        if (_vm is not { IsClosed: false } vm || !IsLoaded) { StopClassification(); ClassificationRow.Visibility = Visibility.Collapsed; return; }
        if (vm.IsSending) { ClassificationPicker.IsEnabled = false; return; }
        var identity = vm.Identities.FirstOrDefault(item => string.Equals(item.Id, vm.SelectedIdentityId, StringComparison.Ordinal));
        var sender = vm.ClassificationSender;
        var delimiter = vm.ClassificationSubAddressDelimiter;
        var accounts = ViewHost.Get<IAccountSessionSnapshots>();
        var captured = accounts.CaptureOwnerSession(vm.AccountKey).Value;
        if (identity is null || captured is null || sender is null || sender.Length > 254 || !RecipientText.IsValidEmail(sender))
        {
            StopClassification();
            vm.BeginClassificationLoad();
            ClassificationRow.Visibility = Visibility.Collapsed;
            return;
        }
        var senderKey = identity.Id + "\0" + sender + "\0" + delimiter;
        if (_classificationLoad is not null && ReferenceEquals(_loadingOwner, captured.Session) && string.Equals(_loadingIdentity, senderKey, StringComparison.Ordinal)) return;
        StopClassification();
        vm.BeginClassificationLoad(captured.Session, senderKey);
        ClassificationRow.Visibility = Visibility.Collapsed;
        var pending = new CancellationTokenSource();
        _classificationLoad = pending;
        _loadingOwner = captured.Session;
        _loadingIdentity = senderKey;
        _ = LoadClassificationAsync(vm, identity, sender, delimiter, captured, accounts, pending);
    }

    private void OnClassificationVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(ComposerViewModel.IsSending), StringComparison.Ordinal) || _classificationVm is not { } vm) return;
        ClassificationPicker.IsEnabled = !vm.IsSending;
        if (!vm.IsSending) RefreshClassification();
    }
    private async Task LoadClassificationAsync(ComposerViewModel vm, IdentityChoice identity, string sender, string? delimiter, AccountSessionSnapshot account,
        IAccountSessionSnapshots accounts, CancellationTokenSource pending)
    {
        try
        {
            var policy = await ViewHost.Get<ComposerClassification>().PreviewAsync(account.Session, identity.Id, sender, delimiter, pending.Token).ConfigureAwait(true);
            if (pending.IsCancellationRequested || !ReferenceEquals(_classificationLoad, pending) || !ReferenceEquals(_vm, vm) ||
                vm.IsClosed || vm.IsSending || !IsLoaded ||
                !string.Equals(vm.ClassificationSender, sender, StringComparison.Ordinal) ||
                !string.Equals(vm.ClassificationSubAddressDelimiter, delimiter, StringComparison.Ordinal) ||
                !string.Equals(vm.SelectedIdentityId, identity.Id, StringComparison.Ordinal)) return;
            _ = accounts.TryRunWhileCurrent([account], () => { vm.SetClassificationPolicy(policy); return true; });
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_classificationLoad, pending))
            {
                _classificationLoad = null;
                _loadingIdentity = null;
                _loadingOwner = null;
                pending.Dispose();
            }
        }
    }

    private void AddClassificationItem(ComposerViewModel vm, ClassificationPolicy policy, string name, string id, string automationId)
    {
        var item = new MenuFlyoutItem { Text = name };
        AutomationProperties.SetAutomationId(item, "composer.classification." + automationId);
        item.Click += (_, _) =>
        {
            if (!ReferenceEquals(_vm, vm) || vm.IsSending || !vm.ClassificationReady ||
                !ReferenceEquals(vm.ClassificationPolicy, policy) || !ClassificationMenu.Items.Contains(item)) return;
            vm.ClassificationChoice = new ComposerClassificationChoice(id.Length == 0 ? null : id);
        };
        ClassificationMenu.Items.Add(item);
    }

    private void SyncClassification()
    {
        ClassificationMenu.Items.Clear();
        if (_vm is not { ClassificationReady: true, ClassificationPolicy: { } policy } vm || policy.List.Count == 0)
        {
            ClassificationMenu.Hide();
            ClassificationRow.Visibility = Visibility.Collapsed;
            return;
        }
        if (!policy.Required) AddClassificationItem(vm, policy, "None", string.Empty, "none");
        foreach (var label in policy.List) AddClassificationItem(vm, policy, label.Name, label.Id, label.Id);
        var selected = vm.ClassificationChoice is null ? policy.DefaultId : vm.ClassificationChoice.LabelId ?? string.Empty;
        ClassificationPicker.Content = selected.Length == 0 && !policy.Required ? "None" :
            policy.List.FirstOrDefault(label => string.Equals(label.Id, selected, StringComparison.Ordinal))?.Name ?? "Choose a classification";
        ClassificationPicker.IsEnabled = !vm.IsSending;
        ClassificationRow.Visibility = Visibility.Visible;
    }
#endif
}

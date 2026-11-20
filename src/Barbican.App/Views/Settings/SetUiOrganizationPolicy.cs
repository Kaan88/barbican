#if WINDOWS
// Session-only connection to a user-trusted organisation policy provider.
using Barbican.App.Shell;
using Barbican.App.Settings;
using Barbican.Core.Accounts;
using Barbican.Core.OrganizationPolicy;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using ProviderEnrollmentAccount = Barbican.App.Settings.OrganizationPolicyEnrollment.ProviderEnrollmentAccount;

namespace Barbican.App.Views.Settings;

/// <summary>Account-scoped, explicitly consented policy-provider controls.</summary>
internal sealed class SetUiOrganizationPolicy : StackPanel
{
    private readonly OrganizationPolicyEnrollment? _enrollment = ViewHost.Find<OrganizationPolicyEnrollment>();
    private readonly IAccountManager? _accounts = ViewHost.Find<IAccountManager>();
    private readonly TextBox _origin = SetUiUi.Id(new TextBox { PlaceholderText = "https://policy.example.org", MaxLength = 2048 }, "set.account.policy.origin");
    private readonly CheckBox _consent = SetUiUi.Id(new CheckBox { HorizontalAlignment = HorizontalAlignment.Stretch }, "set.account.policy.consent");
    private readonly TextBlock _consentText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = SetUiUi.Id(SetUiUi.Meta(""), "set.account.policy.status");
    private readonly Button _connect;
    private readonly Button _disconnect;
    private ProviderEnrollmentAccount? _account;
    private CancellationTokenSource? _pending;
    private int _generation;

    public SetUiOrganizationPolicy()
    {
        Spacing = 6;
        Children.Add(SetUiUi.Heading("Organisation policy"));
        Children.Add(SetUiUi.Meta("Connect a trusted policy provider for this signed-in account. The connection lasts only for this session."));
        Children.Add(SetUiUi.Body("Policy provider address"));
        AutomationProperties.SetName(_origin, "Policy provider address");
        Children.Add(_origin);
        _consent.Content = _consentText;
        _consent.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        Children.Add(_consent);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _connect = SetUiUi.Pill("Verify and connect", () => SetUiUi.Run(ConnectAsync), "set.account.policy.connect", primary: true);
        _disconnect = SetUiUi.Pill("Disconnect", Disconnect, "set.account.policy.disconnect");
        buttons.Children.Add(_connect);
        buttons.Children.Add(_disconnect);
        Children.Add(buttons);
        Children.Add(_status);
        _origin.TextChanged += (_, _) => { _consent.IsChecked = false; UpdateConsent(); };
        _consent.Checked += (_, _) => UpdateButtons();
        _consent.Unchecked += (_, _) => UpdateButtons();
        Loaded += (_, _) => { if (_accounts is not null) _accounts.ActiveChanged += AccountChanged; Reset(); };
        Unloaded += (_, _) => { if (_accounts is not null) _accounts.ActiveChanged -= AccountChanged; Clear(); };
    }

    private void AccountChanged(object? sender, ActiveAccountChangedEventArgs args) => _ = DispatcherQueue.TryEnqueue(Reset);

    private void Reset()
    {
        Clear();
        if (!IsLoaded) return;
        _account = _enrollment?.Capture();
        var provider = _account is not null && _enrollment?.IsCurrent(_account) == true ? _enrollment.CurrentProvider(_account) : null;
        var origin = provider is null ? null : CanonicalOrigin(provider);
        if (origin is not null) _origin.Text = origin;
        _status.Text = _account is null ? "An active OAuth sign-in is required to connect a policy provider."
            : origin is null ? "No policy provider is connected for this account." : "Connected to " + origin + ".";
        UpdateConsent();
    }

    private void Clear()
    {
        _generation++;
        _pending?.Cancel();
        _pending = null;
        _account = null;
        _origin.IsEnabled = _consent.IsEnabled = true;
        _origin.Text = string.Empty;
        _consent.IsChecked = false;
        _status.Text = string.Empty;
        UpdateButtons();
    }

    private void UpdateConsent()
    {
        var origin = ValidOrigin();
        var account = _account?.DisplayName;
        var text = origin is null || string.IsNullOrWhiteSpace(account) ? "Enter an HTTPS provider address to review the connection."
            : "I trust " + origin + " to receive the OAuth access token for " + account + " and provide its organisation policy.";
        _consentText.Text = text;
        AutomationProperties.SetName(_consent, text);
        UpdateButtons();
    }

    private string? ValidOrigin() => Uri.TryCreate(_origin.Text.Trim(), UriKind.Absolute, out var uri) ? CanonicalOrigin(uri) : null;

    private static string? CanonicalOrigin(Uri uri)
    {
        if (!OrganizationPolicyService.IsProviderOrigin(uri)) return null;
        var host = uri.IdnHost;
        if (host.Contains(':', StringComparison.Ordinal) && !host.StartsWith('[')) host = "[" + host + "]";
        return "https://" + host.ToLowerInvariant() + (uri.IsDefaultPort ? "" : ":" + uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private void UpdateButtons()
    {
        var current = IsLoaded && _pending is null && _account is not null && _enrollment?.IsCurrent(_account) == true;
        _connect.IsEnabled = current && _consent.IsChecked == true && ValidOrigin() is not null;
        _disconnect.IsEnabled = current && _enrollment!.CurrentProvider(_account!) is not null;
    }

    private async Task ConnectAsync()
    {
        var account = _account;
        var origin = ValidOrigin();
        if (account is null || origin is null || _consent.IsChecked != true || _pending is not null || _enrollment?.IsCurrent(account) != true) return;
        var generation = _generation;
        using var pending = new CancellationTokenSource();
        _pending = pending;
        _origin.IsEnabled = _consent.IsEnabled = false;
        UpdateButtons();
        _status.Text = "Verifying the policy provider…";
        try
        {
            var result = await _enrollment.ConnectAsync(account, origin, pending.Token).ConfigureAwait(true);
            if (generation != _generation || !IsLoaded || !_enrollment.IsCurrent(account)) return;
            if (result.Status == OrganizationPolicyTransportStatus.Authoritative &&
                !string.Equals(_enrollment.CurrentProvider(account) is { } provider ? CanonicalOrigin(provider) : null, origin, StringComparison.Ordinal)) return;
            _consent.IsChecked = false;
            _status.Text = result.Status == OrganizationPolicyTransportStatus.Authoritative && result.Snapshot is { } snapshot
                ? "Connected to " + origin + ". Provider policy " + (snapshot.Effective.NoPrint ? "restricts" : "allows") + " printing. " + snapshot.Effective.Labels.List.Count.ToString(System.Globalization.CultureInfo.CurrentCulture) + " classification labels."
                : _enrollment.CurrentProvider(account) is null
                    ? "The provider could not supply a verified policy for this account. Check the address and try again."
                    : "The new provider could not be verified. The previous connection remains active.";
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
#pragma warning disable CA1031 // A failed verification stays in the panel and never reveals transport details.
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            if (generation == _generation && IsLoaded && _enrollment.IsCurrent(account))
                _status.Text = "The provider could not be verified. Check the address and try again.";
        }
#pragma warning restore CA1031
        finally
        {
            if (generation == _generation)
            {
                _pending = null;
                _origin.IsEnabled = _consent.IsEnabled = true;
                UpdateButtons();
            }
        }
    }

    private void Disconnect()
    {
        if (_pending is not null || _account is not { } account || _enrollment?.IsCurrent(account) != true) return;
        _ = _enrollment.Disconnect(account);
        Reset();
    }
}

#endif

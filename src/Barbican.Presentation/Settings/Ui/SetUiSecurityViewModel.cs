// Mirrors: Bulwark components/settings/account-settings.tsx, security-settings.tsx, stores/account-security-store.ts (password, two-step sign-in, app passwords, API keys, display name, storage); I-399..I-408; D-M6-A09: no password, secret or otpauth URI is logged or kept past the page.
// THREADING (D-M6-00): every call completes off the UI thread; this class awaits them from the UI thread and raises PropertyChanged there.

using System.ComponentModel;
using Barbican.Core.Common;
using Barbican.Core.Settings.Account;
using Barbican.Presentation.Common;

namespace Barbican.Presentation.Settings.Ui;

/// <summary>The state of the Account and Password and sign-in pages.</summary>
public sealed class SetUiSecurityViewModel : INotifyPropertyChanged
{
    private readonly IAccountSecurity security;
    private readonly IAccountQuota quota;
    private readonly ISetUiViewModel screen;
    private readonly IDispatcher? dispatcher;
    private long _accountVersion;

    public SetUiSecurityViewModel(IAccountSecurity security, IAccountQuota quota, ISetUiViewModel screen, IDispatcher? dispatcher = null)
    {
        this.security = security;
        this.quota = quota;
        this.screen = screen;
        this.dispatcher = dispatcher;
        screen.PropertyChanged += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.PropertyName) || string.Equals(e.PropertyName, nameof(ISetUiViewModel.AccountKey), StringComparison.Ordinal))
            {
                ClearAccountState();
            }
        };
    }

    private string _quotaText = string.Empty;
    private double _quotaFraction;
    private string _displayName = string.Empty;
    private string _status = string.Empty;
    private bool _totpOn;
    private string _otpUri = string.Empty;
    private string _issued = string.Empty;
    private IReadOnlyList<AccountAppPassword> _apps = [];
    private IReadOnlyList<AccountApiKey> _keys = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public string AccountKey => screen.AccountKey;

    public long AccountVersion => _accountVersion;

    public bool Available
    {
        get
        {
            try
            {
                return security.IsAvailable(AccountKey);
            }
            catch (PortPendingException)
            {
                return false;
            }
        }
    }

    public string QuotaText => _quotaText;

    public double QuotaFraction => _quotaFraction;

    public string DisplayName => _displayName;

    /// <summary>The last outcome ("Password changed.", or the server's refusal).</summary>
    public string Status => _status;

    public bool TotpOn => _totpOn;

    /// <summary>The otpauth URI of an enrolment in progress (shown once, cleared on confirm or cancel).</summary>
    public string OtpUri => _otpUri;

    /// <summary>The secret of the app password or API key just created (shown once, never again).</summary>
    public string IssuedSecret => _issued;

    public IReadOnlyList<AccountAppPassword> AppPasswords => _apps;

    public IReadOnlyList<AccountApiKey> ApiKeys => _keys;

    public async Task RefreshAsync(CancellationToken ct)
    {
        var key = AccountKey;
        var version = _accountVersion;
        try
        {
            var q = await quota.UsageAsync(key, ct).ConfigureAwait(true);
            if (!Current(key, version)) { return; }
            if (q.IsOk && q.Value is { } u)
            {
                _quotaText = SetUiFormat.Quota(u.Used, u.Limit);
                _quotaFraction = SetUiFormat.QuotaFraction(u.Used, u.Limit);
            }

            var name = await security.DisplayNameAsync(key, ct).ConfigureAwait(true);
            if (!Current(key, version)) { return; }
            if (name.IsOk)
            {
                _displayName = name.Value ?? string.Empty;
            }

            var totp = await security.TotpAsync(key, ct).ConfigureAwait(true);
            if (!Current(key, version)) { return; }
            if (totp.IsOk && totp.Value is { } t)
            {
                _totpOn = t.Enabled;
            }

            var apps = await security.AppPasswordsAsync(key, ct).ConfigureAwait(true);
            if (!Current(key, version)) { return; }
            if (apps.IsOk && apps.Value is { } a)
            {
                _apps = a;
            }

            var keys = await security.ApiKeysAsync(key, ct).ConfigureAwait(true);
            if (!Current(key, version)) { return; }
            if (keys.IsOk && keys.Value is { } k)
            {
                _keys = k;
            }
        }
        catch (PortPendingException)
        {
            if (!Current(key, version)) { return; }
            _status = "Account settings are not available yet.";
        }

        Raise(null);
    }

    public Task SetDisplayNameAsync(string name, CancellationToken ct) => DoAsync(key => security.SetDisplayNameAsync(key, name, ct), "Name saved.", () => _displayName = name.Trim());

    public async Task ChangePasswordAsync(string current, string next, string confirm, CancellationToken ct)
    {
        var problem = Problem(current, next, confirm);
        if (problem is not null)
        {
            Set(problem);
            return;
        }

        await DoAsync(key => security.ChangePasswordAsync(key, current, next, ct), "Password changed.", null).ConfigureAwait(true);
    }

    public async Task BeginTotpAsync(string password, CancellationToken ct)
    {
        var key = AccountKey;
        var version = _accountVersion;
        try
        {
            var r = await security.BeginTotpAsync(key, password, ct).ConfigureAwait(true);
            if (!Current(key, version)) { return; }
            _otpUri = r.IsOk ? r.Value?.OtpAuthUri ?? string.Empty : string.Empty;
            Set(r.IsOk ? "Add the secret to your authenticator, then enter a code." : r.Error?.Message ?? "Could not start two-step sign-in.");
        }
        catch (PortPendingException)
        {
            if (Current(key, version)) { Set("Two-step sign-in is not available yet."); }
        }
    }

    public Task ConfirmTotpAsync(string code, CancellationToken ct) => DoAsync(key => security.ConfirmTotpAsync(key, code, ct), "Two-step sign-in is on.", () =>
    {
        _totpOn = true;
        _otpUri = string.Empty;
    });

    public Task DisableTotpAsync(string password, CancellationToken ct) => DoAsync(key => security.DisableTotpAsync(key, password, ct), "Two-step sign-in is off.", () => _totpOn = false);

    public async Task CreateAppPasswordAsync(string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            Set("Give the app password a name.");
            return;
        }

        await IssueAsync(key => security.CreateAppPasswordAsync(key, name.Trim(), null, ct), ct).ConfigureAwait(true);
    }

    public async Task CreateApiKeyAsync(string description, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            Set("Give the API key a description.");
            return;
        }

        await IssueAsync(key => security.CreateApiKeyAsync(key, description.Trim(), null, ct), ct).ConfigureAwait(true);
    }

    public async Task DeleteAppPasswordAsync(string id, CancellationToken ct)
    {
        await DoAsync(key => security.DeleteAppPasswordAsync(key, id, ct), "App password deleted.", null).ConfigureAwait(true);
        await RefreshAsync(ct).ConfigureAwait(true);
    }

    public async Task DeleteApiKeyAsync(string id, CancellationToken ct)
    {
        await DoAsync(key => security.DeleteApiKeyAsync(key, id, ct), "API key deleted.", null).ConfigureAwait(true);
        await RefreshAsync(ct).ConfigureAwait(true);
    }

    /// <summary>Forgets the secret that was shown once.</summary>
    public void ClearIssuedSecret()
    {
        _issued = string.Empty;
        _otpUri = string.Empty;
        Raise(null);
    }

    private static string? Problem(string current, string next, string confirm)
    {
        try
        {
            return AccountSecurityRules.PasswordProblem(current, next, confirm);
        }
        catch (PortPendingException)
        {
            return string.IsNullOrEmpty(current) ? "Enter your current password." : string.IsNullOrEmpty(next) ? "Enter a new password." : !string.Equals(next, confirm, StringComparison.Ordinal) ? "The new passwords do not match." : null;
        }
    }

    private async Task IssueAsync(Func<string, Task<Result<AccountIssuedSecret>>> call, CancellationToken ct)
    {
        var key = AccountKey;
        var version = _accountVersion;
        try
        {
            var r = await call(key).ConfigureAwait(true);
            if (!Current(key, version)) { return; }
            _issued = r.IsOk ? r.Value?.Secret ?? string.Empty : string.Empty;
            Set(r.IsOk ? "Copy the secret now: it is shown only once." : r.Error?.Message ?? "Could not create it.");
        }
        catch (PortPendingException)
        {
            if (!Current(key, version)) { return; }
            Set("Not available yet.");
        }

        if (Current(key, version)) { await RefreshAsync(ct).ConfigureAwait(true); }
    }

    private async Task DoAsync(Func<string, Task<Result<Unit>>> call, string ok, Action? apply)
    {
        var key = AccountKey;
        var version = _accountVersion;
        try
        {
            var r = await call(key).ConfigureAwait(true);
            if (!Current(key, version)) { return; }
            if (r.IsOk)
            {
                apply?.Invoke();
            }

            Set(r.IsOk ? ok : r.Error?.Message ?? "That did not work.");
        }
        catch (PortPendingException)
        {
            if (Current(key, version)) { Set("Not available yet."); }
        }
    }

    private bool Current(string key, long version) => version == _accountVersion && string.Equals(key, AccountKey, StringComparison.Ordinal);

    private void ClearAccountState()
    {
        _accountVersion++;
        _quotaText = string.Empty;
        _quotaFraction = 0;
        _displayName = string.Empty;
        _status = string.Empty;
        _totpOn = false;
        _otpUri = string.Empty;
        _issued = string.Empty;
        _apps = [];
        _keys = [];
        Raise(nameof(AccountKey));
    }

    private void Set(string status)
    {
        _status = status;
        Raise(null);
    }

    private void Raise(string? name)
    {
        if (dispatcher is null || dispatcher.HasThreadAccess)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        else
        {
            dispatcher.Post(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)));
        }
    }
}

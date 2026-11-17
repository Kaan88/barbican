// Mirrors: Rust main.rs:753 on_login (server, user, password, one-time code, remember), :769 on_sso_login ("Enter your server address first.",
// "Continue in your browser…", client id default "barbican"), ui/app.slint:1388-:1424 (server field, remember me, 2FA field, client id);
// Bulwark components/auth/login-server-field.tsx:62-:89 (looking / found "Server: <host>" with Change / not found; the unsupported-provider
// notice), components/auth/oauth-status-card.tsx:103 (a status with one action), stores/auth-store.ts:1969 (TOTP_REQUIRED asks for the
// code). spec I-098, I-099, I-101 (no language picker in M1), I-103, I-104. Fable review 2 R2-03: this is the model the sign-in view binds
// to (the App's own form model is gone); Enter submits (Bulwark's form does), a failure that throws ends as a status line, never as an
// unhandled exception; R2-01/R2-02: an account the server refused is offered with its server and login filled in (Prefill).

using Barbican.Core.Accounts;
using Barbican.Core.Auth;
using Barbican.Core.Jmap.Errors;
using Barbican.Presentation.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Barbican.Presentation.Accounts;

/// <summary>The sign-in screen: password with an optional one-time code, or OAuth in the browser.</summary>
public sealed class SignInViewModel : ObservableObject, IDisposable
{
    private readonly IAccountManager _accounts;
    private readonly ServerDiscovery _discovery;
    private readonly IDispatcher _dispatcher;
    private CancellationTokenSource? _work;
    private CancellationTokenSource? _lookup;
    private string _server = "";
    private string _user = "";
    private string _password = "";
    private string _totpCode = "";
    private string _clientId = OAuthClientRegistry.DefaultClientId;
    private bool _remember = true;
    private bool _needsTotp;
    private bool _isBusy;
    private bool _isEditingServer = true;
    private bool _isLookingUp;
    private bool _oauthPending;
    private string? _status;
    private bool _statusIsError;
    private string? _providerNotice;
    private bool _canLeave;

    public SignInViewModel(IAccountManager accounts, ServerDiscovery discovery, IDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _accounts = accounts;
        _discovery = discovery;
        _dispatcher = dispatcher;
        SignInCommand = new AsyncRelayCommand(SignInAsync, () => CanSignIn);
        OAuthSignInCommand = new AsyncRelayCommand(OAuthSignInAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        ChangeServerCommand = new RelayCommand(() => IsEditingServer = true);
        LeaveCommand = new RelayCommand(Leave, () => CanLeave);
    }

    /// <summary>Raised after a successful sign-in (on the UI thread).</summary>
    public event EventHandler<SignedInEventArgs>? SignedIn;

    /// <summary>The user left the screen without signing in (<see cref="LeaveCommand"/>).</summary>
    public event EventHandler? Left;

    /// <summary>Back to the mail screen without signing in; offered only while there is one to go back to (<see cref="CanLeave"/>).</summary>
    public IRelayCommand LeaveCommand { get; }

    /// <summary>The screen was opened over the mail screen ("Add account", or an account that must sign in again while others are up).</summary>
    public bool CanLeave
    {
        get => _canLeave;
        set
        {
            if (SetProperty(ref _canLeave, value))
            {
                LeaveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public IAsyncRelayCommand SignInCommand { get; }

    public IAsyncRelayCommand OAuthSignInCommand { get; }

    /// <summary>Stops a pending sign-in (the OAuth status card's action, I-103).</summary>
    public IRelayCommand CancelCommand { get; }

    /// <summary>"Change" next to a found server (I-098).</summary>
    public IRelayCommand ChangeServerCommand { get; }

    /// <summary>The server address (I-099).</summary>
    public string Server
    {
        get => _server;
        set
        {
            if (SetProperty(ref _server, value ?? ""))
            {
                OnPropertyChanged(nameof(ServerLabel));
                Refresh();
            }
        }
    }

    /// <summary>The login (an email address starts the server lookup and the provider check).</summary>
    public string User
    {
        get => _user;
        set
        {
            if (SetProperty(ref _user, value ?? ""))
            {
                OnUserChanged();
                Refresh();
            }
        }
    }

    /// <summary>The password; kept only until the sign-in ends.</summary>
    public string Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value ?? ""))
            {
                Refresh();
            }
        }
    }

    /// <summary>The one-time code (never remembered).</summary>
    public string TotpCode
    {
        get => _totpCode;
        set => SetProperty(ref _totpCode, value ?? "");
    }

    /// <summary>The OAuth client id used when the server offers no dynamic registration (I-104).</summary>
    public string ClientId
    {
        get => _clientId;
        set => SetProperty(ref _clientId, value ?? "");
    }

    /// <summary>Remember me (I-104).</summary>
    public bool Remember
    {
        get => _remember;
        set => SetProperty(ref _remember, value);
    }

    /// <summary>The server asked for a one-time code: show the field (I-104).</summary>
    public bool NeedsTotp
    {
        get => _needsTotp;
        private set => SetProperty(ref _needsTotp, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                Refresh();
            }
        }
    }

    /// <summary>The server field is shown for editing; false shows "Server: host" with Change.</summary>
    public bool IsEditingServer
    {
        get => _isEditingServer;
        private set => SetProperty(ref _isEditingServer, value);
    }

    /// <summary>The domain lookup is running ("Looking for your server…").</summary>
    public bool IsLookingUp
    {
        get => _isLookingUp;
        private set => SetProperty(ref _isLookingUp, value);
    }

    /// <summary>The host to show for the server (no scheme).</summary>
    public string ServerLabel => MailServerAddress.NormalizeServerInput(Server) is { } url ? MailServerAddress.ServerHostLabel(url) : Server;

    /// <summary>An OAuth sign-in waits for the browser (the status card with Cancel, I-103).</summary>
    public bool OAuthPending
    {
        get => _oauthPending;
        private set => SetProperty(ref _oauthPending, value);
    }

    /// <summary>The status line.</summary>
    public string? Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetProperty(ref _statusIsError, value);
    }

    /// <summary>"provider does not offer JMAP" when the address belongs to one (I-101), else null.</summary>
    public string? ProviderNotice
    {
        get => _providerNotice;
        private set => SetProperty(ref _providerNotice, value);
    }

    public bool CanSignIn => !IsBusy && Server.Trim().Length > 0 && User.Trim().Length > 0 && Password.Length > 0;

    /// <summary>
    /// Offers <paramref name="account"/> again (the server refused it, R2-01): its server and login are filled in and shown, the password is
    /// empty, and <paramref name="reason"/> is the status line. No lookup runs: the server is known.
    /// </summary>
    public void Prefill(AccountInfo account, string? reason)
    {
        ArgumentNullException.ThrowIfNull(account);
        Cancel();
        _lookup?.Cancel();
        IsEditingServer = true;
        Server = account.Server;
        _user = account.User;
        OnPropertyChanged(nameof(User));
        ProviderNotice = null;
        IsLookingUp = false;
        Password = "";
        TotpCode = "";
        NeedsTotp = false;
        IsEditingServer = Server.Length == 0;
        SetStatus(reason ?? $"{account.User} needs to sign in again.", error: false);
        Refresh();
    }

    /// <summary>Enter in a field: signs in when the form is complete (Bulwark's form submits on Enter).</summary>
    public void Submit()
    {
        if (SignInCommand.CanExecute(null))
        {
            SignInCommand.Execute(null);
        }
    }

    /// <summary>Starts over for "Add account": empty fields, the server kept.</summary>
    public void Reset(string? status = null)
    {
        Cancel();
        User = "";
        Password = "";
        TotpCode = "";
        NeedsTotp = false;
        IsEditingServer = Server.Length == 0;
        SetStatus(status, error: false);
    }

    public void Dispose()
    {
        _work?.Cancel();
        _work?.Dispose();
        _lookup?.Cancel();
        _lookup?.Dispose();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(CanSignIn));
        SignInCommand.NotifyCanExecuteChanged();
        OAuthSignInCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private void SetStatus(string? text, bool error)
    {
        Status = text;
        StatusIsError = error;
    }

    private CancellationToken Begin()
    {
        _work?.Dispose();
        _work = new CancellationTokenSource();
        IsBusy = true;
        SetStatus(null, error: false);
        return _work.Token;
    }

    private void Cancel()
    {
        _work?.Cancel();
        OAuthPending = false;
    }

    private void Leave()
    {
        Cancel();
        Password = "";
        TotpCode = "";
        NeedsTotp = false;
        SetStatus(null, error: false);
        Left?.Invoke(this, EventArgs.Empty);
    }

    private async Task SignInAsync()
    {
        var ct = Begin();
        try
        {
            var r = await _accounts.SignInWithPasswordAsync(new PasswordSignIn(Server, User, Password, NeedsTotp ? TotpCode : null, Remember), ct).ConfigureAwait(true);
            if (r.IsOk)
            {
                Password = "";
                TotpCode = "";
                NeedsTotp = false;
                SignedIn?.Invoke(this, new SignedInEventArgs(r.Value!));
                return;
            }

            if (r.Error is OtpRequiredError)
            {
                // the password was right: ask for the code and keep everything typed (Bulwark auth-store.ts:1970)
                NeedsTotp = true;
                SetStatus("Enter the code from your authenticator app.", error: false);
                return;
            }

            SetStatus(r.Error!.Message, error: true);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Sign-in cancelled.", error: false);
        }
#pragma warning disable CA1031 // the top of a button's command: a failure (a vault that cannot be written) is shown, it must not end the app (R2-03)
        catch (Exception e)
#pragma warning restore CA1031
        {
            SetStatus($"Sign-in failed: {e.Message}", error: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OAuthSignInAsync()
    {
        if (Server.Trim().Length == 0)
        {
            SetStatus("Enter your server address first.", error: true);
            return;
        }

        var ct = Begin();
        OAuthPending = true;
        SetStatus("Continue in your browser…", error: false);
        try
        {
            var request = new OAuthSignIn(Server, ClientId, Remember) { LoginHint = User };
            var r = await _accounts.SignInWithOAuthAsync(request, ct).ConfigureAwait(true);
            if (r.IsOk)
            {
                SetStatus(null, error: false);
                SignedIn?.Invoke(this, new SignedInEventArgs(r.Value!));
                return;
            }

            SetStatus(r.Error!.Message, error: true);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Sign-in cancelled.", error: false);
        }
#pragma warning disable CA1031 // the top of a button's command: a failure is shown, it must not end the app (R2-03)
        catch (Exception e)
#pragma warning restore CA1031
        {
            SetStatus($"Sign-in failed: {e.Message}", error: true);
        }
        finally
        {
            OAuthPending = false;
            IsBusy = false;
        }
    }

    private void OnUserChanged()
    {
        var domain = MailServerAddress.EmailDomain(User);
        ProviderNotice = MailServerAddress.UnsupportedProvider(domain) is { } provider
            ? $"{provider} does not offer JMAP, so Barbican cannot sign in to it. Use the address of a JMAP server such as Stalwart."
            : null;
        _lookup?.Cancel();
        if (domain.Length == 0 || ProviderNotice is not null || (Server.Length > 0 && IsEditingServer))
        {
            IsLookingUp = false;
            return;
        }

        var lookup = new CancellationTokenSource();
        _lookup = lookup;
        _ = LookUpAsync(domain, lookup.Token);
    }

    private async Task LookUpAsync(string domain, CancellationToken ct)
    {
        IsLookingUp = true;
        try
        {
            var found = await Task.Run(() => _discovery.FindAsync(domain, ct), ct).ConfigureAwait(false);
            await _dispatcher.InvokeAsync(() =>
            {
                if (!ct.IsCancellationRequested && found is not null)
                {
                    Server = found;
                    IsEditingServer = false;
                }

                IsLookingUp = false;
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // a newer address replaced this lookup
        }
    }
}

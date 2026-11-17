// Mirrors: Bulwark components/auth/login-server-field.tsx, oauth-status-card.tsx, stores/auth-store.ts (sign-in, TOTP step); Rust main.rs sign-in screen; spec I-098, I-099, I-103, I-104.
// Drives the IAccountManager contract (M1-plan §4.2) directly; the accounts stream's SignInViewModel is an empty stub while this view is built. A password is never kept
// after the call and the one-time code is never remembered (ADR 0008).

using Barbican.Core.Accounts;
using Barbican.Core.Jmap.Errors;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.App.Views.SignIn;

/// <summary>The fields and the two actions of the sign-in screen.</summary>
public sealed class SignInFormModel : ObservableObject
{
    private readonly IAccountManager? _manager;
    private string _server = string.Empty;
    private string _user = string.Empty;
    private string _password = string.Empty;
    private string _totp = string.Empty;
    private string _clientId = string.Empty;
    private bool _remember = true;
    private bool _needsTotp;
    private bool _busy;
    private string _status = string.Empty;
    private bool _isError;

    public SignInFormModel(IAccountManager? manager)
    {
        _manager = manager;
    }

    /// <summary>A sign-in worked; the shell shows the mail screen.</summary>
    public event EventHandler? SignedIn;

    public string Server
    {
        get => _server;
        set
        {
            if (SetProperty(ref _server, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanSignIn));
                OnPropertyChanged(nameof(CanUseOAuth));
            }
        }
    }

    public string User
    {
        get => _user;
        set
        {
            if (SetProperty(ref _user, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanSignIn));
            }
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanSignIn));
            }
        }
    }

    /// <summary>The one-time code (second step; Bulwark shows the field once the server asks).</summary>
    public string Totp
    {
        get => _totp;
        set => SetProperty(ref _totp, value ?? string.Empty);
    }

    /// <summary>The OAuth client id, for a server without dynamic registration (a Barbican extra, I-104).</summary>
    public string ClientId
    {
        get => _clientId;
        set => SetProperty(ref _clientId, value ?? string.Empty);
    }

    public bool Remember
    {
        get => _remember;
        set => SetProperty(ref _remember, value);
    }

    public bool NeedsTotp
    {
        get => _needsTotp;
        private set => SetProperty(ref _needsTotp, value);
    }

    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (SetProperty(ref _busy, value))
            {
                OnPropertyChanged(nameof(CanSignIn));
                OnPropertyChanged(nameof(CanUseOAuth));
            }
        }
    }

    /// <summary>The line under the buttons: what is happening, or why it failed.</summary>
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsError
    {
        get => _isError;
        private set => SetProperty(ref _isError, value);
    }

    public bool CanSignIn => !_busy && _manager is not null && _server.Trim().Length > 0 && _user.Trim().Length > 0 && _password.Length > 0;

    public bool CanUseOAuth => !_busy && _manager is not null && _server.Trim().Length > 0;

    public async Task SignInAsync()
    {
        if (!CanSignIn || _manager is null)
        {
            return;
        }

        IsBusy = true;
        Set("Signing in…", false);
        try
        {
            var request = new PasswordSignIn(_server.Trim(), _user.Trim(), _password, NeedsTotp && _totp.Trim().Length > 0 ? _totp.Trim() : null, _remember);
            var result = await _manager.SignInWithPasswordAsync(request, CancellationToken.None);
            if (result.IsOk)
            {
                Finish();
            }
            else if (result.Error is OtpRequiredError)
            {
                NeedsTotp = true;
                Set("Enter the one-time code from your authenticator.", false);
            }
            else
            {
                Set(result.Error!.Message, true);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SignInWithOAuthAsync()
    {
        if (!CanUseOAuth || _manager is null)
        {
            return;
        }

        IsBusy = true;
        Set("Waiting for the browser…", false);
        try
        {
            var client = _clientId.Trim().Length > 0 ? _clientId.Trim() : null;
            var result = await _manager.SignInWithOAuthAsync(new OAuthSignIn(_server.Trim(), client, _remember), CancellationToken.None);
            if (result.IsOk)
            {
                Finish();
            }
            else
            {
                Set(result.Error!.Message, true);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Finish()
    {
        // the secrets leave memory with the form
        Password = string.Empty;
        Totp = string.Empty;
        NeedsTotp = false;
        Set(string.Empty, false);
        SignedIn?.Invoke(this, EventArgs.Empty);
    }

    private void Set(string text, bool error)
    {
        Status = text;
        IsError = error;
    }
}

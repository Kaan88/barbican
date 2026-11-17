// Rust main.rs on_login / on_sso_login and Bulwark login-server-field.tsx, auth-store.ts (TOTP_REQUIRED). spec I-098 (found server with
// Change), I-099 (server field), I-101 (unsupported provider notice; no language picker in M1), I-103 (OAuth status with Cancel), I-104
// (remember me, one-time code, client id).

using System.Net;
using Barbican.Core.Accounts;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Transport;
using Barbican.Presentation.Accounts;
using Barbican.Presentation.Common;

namespace Barbican.Presentation.Tests.Accounts;

public sealed class SignInViewModelTests : IDisposable
{
    private readonly FakeAccounts _accounts = new();
    private readonly JmapHttp _http = new(() => new WellKnown("https://mail.example.org"));
    private readonly SignInViewModel _vm;

    public SignInViewModelTests() => _vm = new SignInViewModel(_accounts, new ServerDiscovery(_http, TimeProvider.System), new InlineDispatcher());

    public void Dispose()
    {
        _vm.Dispose();
        _http.Dispose();
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 400 && !condition(); i++)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }

    [Fact]
    public async Task A_password_sign_in_sends_the_fields_and_clears_the_password()
    {
        SignedInEventArgs? signed = null;
        _vm.SignedIn += (_, e) => signed = e;
        _vm.Server = "https://jmap.example.org";
        _vm.User = "ana";
        Assert.False(_vm.CanSignIn);
        _vm.Password = "pw";
        _vm.Remember = false;
        Assert.True(_vm.CanSignIn);

        await _vm.SignInCommand.ExecuteAsync(null);

        var sent = Assert.Single(_accounts.PasswordRequests);
        Assert.Equal(new PasswordSignIn("https://jmap.example.org", "ana", "pw", null, false), sent);
        Assert.Equal("", _vm.Password);
        Assert.Equal("ana", signed!.Account.User);
        Assert.False(_vm.IsBusy);
    }

    [Fact]
    public async Task A_server_asking_for_a_code_shows_the_code_field_and_keeps_the_fields()
    {
        _accounts.Answers.Enqueue(FakeAccounts.Fail(new OtpRequiredError("TOTP code required")));
        _vm.Server = "https://jmap.example.org";
        _vm.User = "ana";
        _vm.Password = "pw";
        await _vm.SignInCommand.ExecuteAsync(null);

        Assert.True(_vm.NeedsTotp);
        Assert.False(_vm.StatusIsError);
        Assert.Equal("pw", _vm.Password);

        _vm.TotpCode = "123456";
        await _vm.SignInCommand.ExecuteAsync(null);
        Assert.Equal("123456", _accounts.PasswordRequests[1].TotpCode);
        Assert.False(_vm.NeedsTotp);
        Assert.Equal("", _vm.TotpCode);
    }

    [Fact]
    public async Task A_refusal_is_shown_as_an_error()
    {
        _accounts.Answers.Enqueue(FakeAccounts.Fail(new AuthError("Wrong username or password.", NeedsSignIn: false)));
        _vm.Server = "https://jmap.example.org";
        _vm.User = "ana";
        _vm.Password = "bad";
        await _vm.SignInCommand.ExecuteAsync(null);
        Assert.Equal("Wrong username or password.", _vm.Status);
        Assert.True(_vm.StatusIsError);
    }

    [Fact]
    public async Task Oauth_needs_a_server_then_waits_for_the_browser_and_can_be_cancelled()
    {
        await _vm.OAuthSignInCommand.ExecuteAsync(null);
        Assert.Equal("Enter your server address first.", _vm.Status);
        Assert.Empty(_accounts.OAuthRequests);

        _vm.Server = "https://jmap.example.org";
        _vm.User = "ana@example.org";
        _vm.ClientId = "my-client";
        _accounts.Gate = new TaskCompletionSource();
        var running = _vm.OAuthSignInCommand.ExecuteAsync(null);
        Assert.True(_vm.OAuthPending);
        Assert.Equal("Continue in your browser…", _vm.Status);
        Assert.True(_vm.CancelCommand.CanExecute(null));

        _vm.CancelCommand.Execute(null);
        await running;
        Assert.False(_vm.OAuthPending);
        Assert.False(_vm.IsBusy);
        Assert.Equal("Sign-in cancelled.", _vm.Status);
        var sent = Assert.Single(_accounts.OAuthRequests);
        Assert.Equal("my-client", sent.ClientId);
        Assert.Equal("ana@example.org", sent.LoginHint);
    }

    [Fact]
    public void The_client_id_defaults_to_barbican_and_remember_is_on()
    {
        Assert.Equal("barbican", _vm.ClientId);
        Assert.True(_vm.Remember);
    }

    [Fact]
    public void An_unsupported_provider_gets_a_notice()
    {
        _vm.User = "someone@live.example"; // a country variant of the provider (first label), on a fictional domain
        Assert.StartsWith("Outlook.com does not offer JMAP", _vm.ProviderNotice, StringComparison.Ordinal);
        _vm.User = "someone@example.org";
        Assert.Null(_vm.ProviderNotice);
    }

    [Fact]
    public async Task An_address_finds_its_server_which_can_be_changed()
    {
        _vm.User = "ana@example.org";
        await UntilAsync(() => !_vm.IsLookingUp && !_vm.IsEditingServer);
        Assert.Equal("https://mail.example.org", _vm.Server);
        Assert.Equal("mail.example.org", _vm.ServerLabel);

        _vm.ChangeServerCommand.Execute(null);
        Assert.True(_vm.IsEditingServer);
    }

    [Fact]
    public async Task A_server_typed_by_hand_is_not_replaced_by_the_lookup()
    {
        _vm.Server = "https://jmap.example.org";
        _vm.User = "ana@example.org";
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal("https://jmap.example.org", _vm.Server);
        Assert.False(_vm.IsLookingUp);
    }

    [Fact]
    public void Reset_clears_the_secrets_for_another_account()
    {
        _vm.Server = "https://jmap.example.org";
        _vm.User = "ana";
        _vm.Password = "pw";
        _vm.Reset(AccountsViewModel.AddAccountStatus);
        Assert.Equal("", _vm.User);
        Assert.Equal("", _vm.Password);
        Assert.Equal("https://jmap.example.org", _vm.Server);
        Assert.Equal(AccountsViewModel.AddAccountStatus, _vm.Status);
    }

    // a JMAP server that answers /.well-known/jmap with 401 at one origin only
    private sealed class WellKnown(string origin) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var hit = string.Equals(request.RequestUri!.GetLeftPart(UriPartial.Authority), origin, StringComparison.OrdinalIgnoreCase)
                && string.Equals(request.RequestUri.AbsolutePath, "/.well-known/jmap", StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(hit ? HttpStatusCode.Unauthorized : HttpStatusCode.NotFound));
        }
    }
}

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Settings.Account;
using Barbican.Core.Settings.Templates;
using Barbican.Presentation.Common;
using Barbican.Presentation.Settings.Sample;
using Barbican.Presentation.Settings.Server;
using Barbican.Presentation.Tests.Accounts;

namespace Barbican.Presentation.Tests.Settings.Server;

public sealed class SrvUiIdentityTemplateAccountTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Identity_cannot_write_a_stale_form_to_a_new_account()
    {
        var (accounts, b) = Accounts();
        var service = new DelayedIdentities();
        var vm = new SrvUiIdentitiesViewModel(service, accounts, new InlineDispatcher());
        await vm.LoadAsync(Ct);
        vm.StartNew(); vm.Email = "old@example.org";
        service.Hold = true;
        await accounts.SwitchToAsync(b.Key, Ct);
        vm.Email = "stale@example.org";
        await vm.SaveAsync(Ct);
        Assert.Equal(0, service.Writes);
        service.Release.TrySetResult();
    }

    [Fact]
    public async Task Template_cannot_write_a_stale_form_to_a_new_account()
    {
        var (accounts, b) = Accounts();
        var service = new DelayedTemplates();
        var vm = new SrvUiTemplatesViewModel(service, accounts, new InlineDispatcher());
        await vm.LoadAsync(Ct);
        vm.StartNew(); vm.Name = "Old draft";
        service.Hold = true;
        await accounts.SwitchToAsync(b.Key, Ct);
        vm.Name = "Stale draft";
        await vm.SaveAsync(Ct);
        await vm.ImportAsync("[]", Ct);
        Assert.Equal(0, service.Writes);
        service.Release.TrySetResult();
    }

    [Fact]
    public async Task Identity_and_template_confirmations_expire_after_a_b_a()
    {
        var (accounts, b) = Accounts();
        var a = accounts.Active!;
        var identities = new DelayedIdentities();
        var templates = new DelayedTemplates();
        var identity = new SrvUiIdentitiesViewModel(identities, accounts, new InlineDispatcher());
        var template = new SrvUiTemplatesViewModel(templates, accounts, new InlineDispatcher());
        await identity.LoadAsync(Ct);
        await template.LoadAsync(Ct);
        var oldIdentity = identity.AccountGeneration;
        var oldTemplate = template.AccountGeneration;
        identities.Hold = true;
        templates.Hold = true;
        await accounts.SwitchToAsync(b.Key, Ct);
        await accounts.SwitchToAsync(a.Key, Ct);
        Assert.False(identity.IsCurrentGeneration(oldIdentity));
        Assert.False(template.IsCurrentGeneration(oldTemplate));
        identity.Email = "stale@example.org";
        template.Name = "Stale";
        await identity.SaveAsync(Ct);
        await template.SaveAsync(Ct);
        Assert.Equal(0, identities.Writes);
        Assert.Equal(0, templates.Writes);
        identities.Release.TrySetResult();
        templates.Release.TrySetResult();
    }

    private static (FakeAccounts Accounts, AccountInfo B) Accounts()
    {
        var a = FakeAccounts.Info("a@example.org"); var b = FakeAccounts.Info("b@example.org");
        var accounts = new FakeAccounts();
        accounts.Set(new AccountStatus(a, true, true, true, null, null), new AccountStatus(b, false, false, true, null, null));
        return (accounts, b);
    }

    private sealed class DelayedIdentities : IAccountIdentities
    {
        private readonly SampleAccountIdentities _inner = new();
        public bool Hold { get; set; }
        public int Writes { get; private set; }
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event EventHandler<AccountIdentityChangedEventArgs>? Changed { add => _inner.Changed += value; remove => _inner.Changed -= value; }
        public async Task<Result<IReadOnlyList<AccountIdentity>>> ListAsync(string k, CancellationToken ct) { if (Hold) await Release.Task.WaitAsync(ct); return await _inner.ListAsync(k, ct); }
        public Task<Result<AccountIdentity>> CreateAsync(string k, AccountIdentityEdit e, CancellationToken ct) { Writes++; return _inner.CreateAsync(k, e, ct); }
        public Task<Result<AccountIdentity>> UpdateAsync(string k, string id, AccountIdentityEdit e, CancellationToken ct) { Writes++; return _inner.UpdateAsync(k, id, e, ct); }
        public Task<Result<Unit>> DeleteAsync(string k, string id, CancellationToken ct) { Writes++; return _inner.DeleteAsync(k, id, ct); }
        public Task<Result<Unit>> SetOrderAsync(string k, IReadOnlyList<string> ids, CancellationToken ct) { Writes++; return _inner.SetOrderAsync(k, ids, ct); }
    }

    private sealed class DelayedTemplates : ITemplateAdmin
    {
        private readonly SampleTemplateAdmin _inner = new();
        public bool Hold { get; set; }
        public int Writes { get; private set; }
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event EventHandler<TemplateAdminChangedEventArgs>? Changed { add => _inner.Changed += value; remove => _inner.Changed -= value; }
        public async Task<Result<IReadOnlyList<MailTemplate>>> ListAsync(string k, CancellationToken ct) { if (Hold) await Release.Task.WaitAsync(ct); return await _inner.ListAsync(k, ct); }
        public Task<Result<MailTemplate>> CreateAsync(string k, string n, string s, string b, bool h, CancellationToken ct) { Writes++; return _inner.CreateAsync(k, n, s, b, h, ct); }
        public Task<Result<MailTemplate>> UpdateAsync(string k, MailTemplate t, CancellationToken ct) { Writes++; return _inner.UpdateAsync(k, t, ct); }
        public Task<Result<Unit>> DeleteAsync(string k, string id, CancellationToken ct) { Writes++; return _inner.DeleteAsync(k, id, ct); }
        public Task<Result<JsonObject>> ExportAsync(string k, CancellationToken ct) => _inner.ExportAsync(k, ct);
        public Task<Result<int>> ImportAsync(string k, JsonNode d, CancellationToken ct) { Writes++; return _inner.ImportAsync(k, d, ct); }
    }
}

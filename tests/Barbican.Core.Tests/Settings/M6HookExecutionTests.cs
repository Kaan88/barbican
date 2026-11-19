// M6 exit evidence: real services, synthetic accounts and observed hook executions, not source-text matches.
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Filters.Rules;
using Barbican.Core.Mail.Trust;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Settings.Account;
using Barbican.Core.Settings.Backup;
using Barbican.Core.Settings.Pages;
using Barbican.Core.Settings.Prefs;
using Barbican.Core.Sieve;
using Barbican.Core.Settings.Templates;
using Barbican.Core.Settings.Vacation;
using Barbican.Core.Storage;
using Barbican.Core.Tests.Contacts;
using Barbican.Core.Tests.Settings.Prefs;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Settings;

public sealed class M6HookExecutionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Profile_settings_backup_and_trust_execute_their_eight_hooks()
    {
        var root = Directory.CreateTempSubdirectory("barbican-m6-hooks-");
        try
        {
            var paths = new TestProfilePaths(root.FullName);
            using var prefs = new PrefsStore(paths, TimeProvider.System);
            using var gate = new SettingWriteGate();
            var hooks = new ContactHooks();
            using var values = new SettingValues(prefs, prefs, hooks, gate);
            using var backup = new SettingBackup(prefs, prefs, paths, hooks, gate);
            Ok(await values.SetAsync("attachmentReminderKeywords", new JsonArray("attached", "enclosed"), Ct));
            var exported = await backup.ExportAsync(Ct);
            Ok(exported);
            Ok(await backup.ImportAsync(exported.Value!, Ct));
            Ok(await backup.ResetAsync(Ct));
            var trusted = new TrustedSenders(null, hooks);
            Ok(await trusted.AddAsync("sender@example.org", Ct));
            await ExpectAsync(hooks, HookNames.OnBeforeSettingChange, HookNames.OnSettingChange,
                HookNames.OnSettingsExport, HookNames.OnSettingsImport, HookNames.OnSettingsReset,
                HookNames.OnSettingsSync, HookNames.OnKeywordChange, HookNames.OnTrustedSenderChange);
        }
        finally
        {
            // CreateTempSubdirectory created this exact directory; never delete a caller-supplied path.
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Fake_account_identity_and_security_services_execute_ten_hooks()
    {
        var rig = ContactRig.Create();
        var (key, _, _) = await rig.AddAsync("user@example.org", null,
            new FakeJmapOptions { MetadataCapability = true, AccountSecurity = new FakeAccountSecurity() });
        var identities = new AccountIdentities(rig, rig.Hooks);
        Ok(await identities.ListAsync(key, Ct));
        var created = await identities.CreateAsync(key, new AccountIdentityEdit("Alias", "alias@example.org", null, null, "Signature", null), Ct);
        Ok(created);
        Ok(await identities.UpdateAsync(key, created.Value!.Id, new AccountIdentityEdit("Edited alias", null, null, null, "Edited signature", null), Ct));
        Ok(await identities.DeleteAsync(key, created.Value.Id, Ct));
        var security = new AccountSecurity(rig, rig.Hooks);
        Ok(await security.SetDisplayNameAsync(key, "Example user", Ct));
        Ok(await security.SetEncryptionAsync(key, false, null, Ct));
        Ok(await security.BeginTotpAsync(key, "fake-current-password", Ct));
        Ok(await security.ConfirmTotpAsync(key, "123456", Ct));
        var issued = await security.CreateAppPasswordAsync(key, "Synthetic application", null, Ct);
        Ok(issued);
        Ok(await security.DeleteAppPasswordAsync(key, issued.Value!.Id, Ct));
        Ok(await security.ChangePasswordAsync(key, "fake-current-password", "new-fake-password", Ct));
        await ExpectAsync(rig.Hooks, HookNames.OnIdentitiesLoaded, HookNames.OnBeforeIdentityChange,
            HookNames.OnIdentityCreate, HookNames.OnIdentityUpdate, HookNames.OnIdentityDelete,
            HookNames.OnDisplayNameChange, HookNames.OnEncryptionChange, HookNames.OnTotpChange,
            HookNames.OnAppPasswordChange, HookNames.OnPasswordChange);
    }

    [Fact]
    public async Task Fake_filter_vacation_and_template_services_execute_thirteen_hooks()
    {
        var rig = ContactRig.Create();
        var (key, _, _) = await rig.AddAsync("user@example.org", null,
            new FakeJmapOptions { MetadataCapability = true, SieveCapability = true, VacationCapability = true });
        var filters = new FilterService(rig, rig.Hooks);
        var loaded = await filters.LoadAsync(key, Ct);
        Ok(loaded);
        var rule = SieveScript.Rule("Synthetic filter", "from", "contains", "sender@example.org", "mark_read", string.Empty, false);
        Ok(await filters.SaveAsync(key, [rule], loaded.Value!.State, Ct));
        var vacation = new VacationService(rig, rig.Hooks, rig.Time);
        var away = await vacation.LoadAsync(key, Ct);
        Ok(away);
        Ok(await vacation.SaveAsync(key, away.Value! with { Enabled = true, Subject = "Away", TextBody = "Synthetic reply", BlockCalendar = false }, Ct));
        var templates = new TemplateAdmin(rig, new TemplateStore(rig, rig.Time), rig.Hooks, rig.Time);
        var template = await templates.CreateAsync(key, "Synthetic template", "Subject", "Body", false, Ct);
        Ok(template);
        Ok(await templates.UpdateAsync(key, template.Value! with { Body = "Edited body" }, Ct));
        var exported = await templates.ExportAsync(key, Ct);
        Ok(exported);
        Ok(await templates.ImportAsync(key, exported.Value!, Ct));
        Ok(await templates.DeleteAsync(key, template.Value!.Id, Ct));
        await ExpectAsync(rig.Hooks, HookNames.OnFiltersLoaded, HookNames.OnBeforeFiltersSave,
            HookNames.OnFilterRuleChange, HookNames.OnFiltersSave, HookNames.OnSieveScriptChange,
            HookNames.OnSieveScriptGenerate, HookNames.OnVacationLoaded, HookNames.OnBeforeVacationUpdate,
            HookNames.OnVacationUpdate, HookNames.OnTemplateCreate, HookNames.OnTemplateUpdate,
            HookNames.OnTemplateDelete, HookNames.OnTemplatesImport);
    }

    private static void Ok<T>(Result<T> result) => Assert.True(result.IsOk, result.Error?.Message);

    private static async Task ExpectAsync(ContactHooks hooks, params string[] expected)
    {
        // Observer Fire queues work, whereas intercepts/transforms finish before the operation.
        var until = TimeProvider.System.GetTimestamp();
        while (TimeProvider.System.GetElapsedTime(until) < TimeSpan.FromSeconds(3))
        {
            if (expected.All(name => hooks.Names.Contains(name, StringComparer.Ordinal))) break;
            await Task.Delay(TimeSpan.FromMilliseconds(10), Ct);
        }

        Assert.All(expected, name => Assert.Contains(name, hooks.Names, StringComparer.Ordinal));
        Assert.All(hooks.Seen.Where(item => expected.Contains(item.Hook, StringComparer.Ordinal)), item => Assert.NotNull(item.Payload));
    }
}

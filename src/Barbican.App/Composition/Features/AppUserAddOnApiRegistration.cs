#if WINDOWS
// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:194,204-209 and assistant/c.ts:33-36, assistant/d.ts:55 (see Core Plugins/Api/App, Plugins/Api/Account and Plugins/Api/Files). Registers app.searchSettings, getSettings, describeSettings,
// setSettings and undoSettings over the settings store the Settings pages write through (ISettingValues), user.findPeople over the contacts directory service, user.logout over IAccountMaintenance behind the host's confirmation dialog
// (ui.confirm's presenter, so the add-on is named and the text is the host's) and files.saveAttachments over the mail readers and IFileBlobSaver. user.returnToOwnAccount is not registered: Barbican has no impersonation session.
// Windows head only, like the other add-on host methods.
using Barbican.Core.Accounts;
using Barbican.Core.Contacts;
using Barbican.Core.Files;
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Api.App;
using Barbican.Core.Plugins.Api.Files;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Settings.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Composition.Features;

internal static class AppUserAddOnApiRegistration
{
    /// <summary>The 5 app.*Settings methods (settings:read, settings:change), user.findPeople (directory:read), user.logout (auth:emit) and files.saveAttachments (files:write and email:read).</summary>
    internal static IServiceCollection AddAppUserAddOnApi(this IServiceCollection services)
    {
        services.AddSingleton(sp => new PluginSettingsHost(sp.GetRequiredService<ISettingValues>(), sp.GetRequiredService<IPluginMailRateLimiter>(), sp.GetRequiredService<TimeProvider>()));
        for (var index = 0; index < PluginAppSettingsMethods.Count; index++)
        {
            var method = index;
            services.AddSingleton<IPluginApiMethod>(sp => PluginAppSettingsMethods.Create(sp.GetRequiredService<PluginSettingsHost>())[method]);
        }

        services.AddSingleton<IPluginApiMethod>(sp => PluginAccountMethods.FindPeople(sp.GetRequiredService<IContactDirectory>(), sp.GetRequiredService<IPluginMailRateLimiter>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IPluginApiMethod>(sp => PluginAccountMethods.Logout(sp.GetRequiredService<IAccountMaintenance>(), Confirmer(sp.GetRequiredService<Views.Plugins.PluginDialogPresenter>())));
        services.AddSingleton<IPluginApiMethod>(sp => PluginFilesMethods.SaveAttachments(sp.GetRequiredService<IPluginFilesHost>(), sp.GetRequiredService<IPluginMailReaders>(), sp.GetRequiredService<IFileBlobSaver>()));
        return services;
    }

    // user.logout asks through the same presenter as ui.confirm, so the dialog names the add-on and shows the host's own text; the answer is true (accepted), false (declined) or null (the dialog could not be shown)
    private static Func<string, IPluginInvocationContext, CancellationToken, Task<bool?>> Confirmer(Views.Plugins.PluginDialogPresenter presenter)
    {
        var confirm = presenter.Methods.First(m => string.Equals(m.Descriptor.Name, "ui.confirm", StringComparison.Ordinal));
        return async (login, context, ct) =>
        {
            var b = new PluginValueBuilder();
            var options = b.Object(
                ("title", b.String("Sign out?")),
                ("message", b.String($"This add-on wants to sign you out of {login}. Signing out removes the sign-in from this computer, and you will have to sign in again.")),
                ("confirmLabel", b.String("Sign out")), ("cancelLabel", b.String("Stay signed in")), ("danger", b.Boolean(true)));
            var outcome = await confirm.InvokeAsync(context, b.Build(b.Array(options)), ct).ConfigureAwait(false);
            return outcome.IsSuccess && outcome.Value is { Kind: PluginValueKind.Boolean } answer ? answer.BooleanValue : null;
        };
    }
}
#endif

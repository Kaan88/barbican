// Mirrors: M2 plan section 3.3. Owner unified (wave 2): IListSourceResolver (Replace), IMailTargetResolver, IAccountMaintenance (registered by
// Core's accounts module on the account manager), IUnifiedUnread, IMailImport, IAttachedMessageOpener, IAttachmentExpander, the native plugin host (INativePluginHost, IEmailDetailsSource,
// its start-up load and the reader rows), the writer source that routes unified rows, the sidebar's unified slot filler.
// Plugins (Rust main.rs:455): the bundled ones in <app>/plugins (the build copies spam-score there), data in <data>/plugin-data; disabledPlugins
// from the prefs; handlers on the app's hook bus. The user's <config>/plugins is not read until M6b (docs/milestones/M2.md default 7).

using System.Text.Json.Nodes;
using Barbican.App.Shell;
using Barbican.App.Views.Unified;
using Barbican.Core.Accounts;
using Barbican.Core.Mail.Import;
using Barbican.Core.Mail.Lists;
using Barbican.Core.Mail.Parse;
using Barbican.Core.Mail.Tnef;
using Barbican.Core.Mail.Unified;
using Barbican.Core.Mail.Write;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Native;
using Barbican.Core.Storage;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Common;
using Barbican.Presentation.Composition;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Mail.Lists;
using Barbican.Presentation.Reader;
using Barbican.Presentation.Unified;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.App.Composition.Features;

/// <summary>Unified inbox, shared accounts, account maintenance, import and export, native plugin host (M2 stream unified).</summary>
public static class UnifiedRegistration
{
    /// <summary>The prefs key of the disabled plugin ids (Rust main.rs:455).</summary>
    public const string DisabledPluginsKey = "disabledPlugins";

    /// <summary>The prefs key of "show the spam verdict for clean mail too" (B-SPAM-004).</summary>
    public const string ShowCleanSpamKey = "showCleanSpam";

    public static IServiceCollection AddBarbicanUnified(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddUnifiedInbox(services);
        AddPlugins(services);
        services.TryAddSingleton<IMailImport>(sp => new MailImportService(target => AccountOf(sp, target)));

        // attached messages (Email/parse) and winmail.dat (TNEF) on the account that holds the message; the reader consumes them (M2 plan 4.5)
        services.TryAddSingleton<IAttachedMessageOpener>(sp => new EmailParseOpener(key => sp.GetRequiredService<IAccountManager>().SessionFor(key)?.Client));
        services.TryAddSingleton<IAttachmentExpander>(sp => new TnefExpander(key => sp.GetRequiredService<IAccountManager>().SessionFor(key)?.Client));

        // import and export runs (MENU-FOLDER "Import messages…" and "Export as…" call MailTransfer through TransferCommands; the picker is the platform's, or queued answers in a hidden run)
        services.TryAddSingleton(sp => new MailExportService(key => sp.GetRequiredService<IAccountManager>().SessionFor(key)?.Client));
#if AUTOMATION
        // a hidden run never opens a dialog: picks are queued answers only (CLAUDE.md: never touch the user's screen)
        services.TryAddSingleton(_ => new TransferPicks());
#else
        services.TryAddSingleton(_ => new TransferPicks(SystemFilePicker.PickAsync));
#endif
        services.TryAddSingleton(sp => new MailTransfer(sp.GetRequiredService<IMailImport>(), sp.GetRequiredService<MailExportService>(), sp.GetRequiredService<TransferPicks>(), sp.GetRequiredService<IDispatcher>()));
        foreach (var i in Enumerable.Range(0, TransferCommands.Count))
        {
            var index = i;
            services.AddSingleton<Barbican.Presentation.Commands.IAppCommand>(sp => TransferCommands.Create(
                sp.GetRequiredService<MailTransfer>(), sp.GetRequiredService<IMailWriterSource>(), sp.GetRequiredService<Barbican.Presentation.Menus.IMenuDataSource>(), sp.GetService<IToastService>())[index]);
        }

#if AUTOMATION
        services.AddSingleton<Barbican.Automation.IAutomationCommand, Views.Unified.Automation.UnifiedCommand>();
        services.AddSingleton<Barbican.Automation.IAutomationCommand, Views.Unified.Automation.ImportSimCommand>();
        services.AddSingleton<Barbican.Automation.IAutomationCommand, Views.Unified.Automation.PluginsCommand>();
#endif
        return services;
    }

    private static void AddUnifiedInbox(IServiceCollection services)
    {
#if AUTOMATION
        if (string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE"), "1", StringComparison.Ordinal))
        {
            // a sample-data run: two in-process FakeJmap logins (the sample one and "sample-2"), each request logged per login
            services.TryAddSingleton(sp => new Views.Unified.Automation.SampleUnifiedLogins(sp.GetRequiredService<IActiveMail>(), sp.GetRequiredService<TimeProvider>()));
            services.TryAddSingleton<IUnifiedLogins>(sp => sp.GetRequiredService<Views.Unified.Automation.SampleUnifiedLogins>());
        }
#endif
        services.TryAddSingleton<IUnifiedLogins>(sp => new AccountManagerLogins(sp.GetRequiredService<IAccountManager>()));
        services.TryAddSingleton(sp => new UnifiedInbox(
            sp.GetRequiredService<IUnifiedLogins>(),
            sp.GetService<IAccountOverview>(),
            sp.GetRequiredService<IPrefsStore>(),
            sp.GetRequiredService<MailEventHub>()));
        services.AddSingleton<IStartupTask>(sp => sp.GetRequiredService<UnifiedInbox>());
        services.Replace(ServiceDescriptor.Singleton<IListSourceResolver>(sp => new UnifiedListSourceResolver(sp.GetRequiredService<UnifiedInbox>(), sp.GetRequiredService<IActiveMail>())));
        services.Replace(ServiceDescriptor.Singleton<IMailTargetResolver>(sp => new UnifiedTargetResolver(sp.GetRequiredService<IActiveMail>())));
        services.TryAddSingleton<Barbican.Presentation.Mail.Loading.IReadRoutes>(sp => new UnifiedReadRoutes(sp.GetRequiredService<IUnifiedLogins>(), sp.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<IUnifiedUnread>(sp => new UnifiedUnread(sp.GetRequiredService<IAccountOverview>()));
        services.TryAddSingleton(sp => new UnifiedSectionViewModel(
            sp.GetRequiredService<UnifiedInbox>(),
            sp.GetRequiredService<IUnifiedUnread>(),
            sp.GetRequiredService<IPrefsStore>(),
            sp.GetRequiredService<IDispatcher>(),
            () => sp.GetRequiredService<MessageListViewModel>(),
            () => sp.GetService<Barbican.Presentation.Shell.GlanceTilesViewModel>()));
        services.AddSingleton<ISlotContent>(new SlotContent(SlotNames.SidebarUnified, () => new UnifiedSectionView()));
        services.TryAddSingleton(sp => new SharedSectionViewModel(sp.GetRequiredService<UnifiedInbox>(), sp.GetRequiredService<IPrefsStore>(), sp.GetRequiredService<IDispatcher>()));
        services.AddSingleton<ISlotContent>(new SlotContent(SlotNames.SidebarShared, () => new SharedSectionView()));

        // UNIFIED-ROW (spec/menus.md): header and Refresh; Refresh re-reads every source and applies the difference to the open list
        services.AddSingleton<Barbican.Presentation.Menus.IMenuProvider>(_ => new Barbican.Presentation.Menus.DelegateMenuProvider(Barbican.Presentation.Menus.MenuIds.UnifiedRow, UnifiedMenus.Row));
        services.AddSingleton<Barbican.Presentation.Commands.IAppCommand>(sp => new Barbican.Presentation.Commands.DelegateAppCommand(
            Barbican.Presentation.Commands.CommandIds.RefreshUnified,
            _ => sp.GetRequiredService<UnifiedInbox>().Settings.Enabled,
            async (_, ct) =>
            {
                await sp.GetRequiredService<UnifiedInbox>().RefreshAsync(ct).ConfigureAwait(true);
                return Core.Common.Result<Core.Common.Unit>.Ok(default);
            }));

        // the actions' writer source, wrapped so a unified row writes only to its own account
        if (services.LastOrDefault(d => d.ServiceType == typeof(IMailWriterSource)) is { } inner)
        {
            services.Replace(ServiceDescriptor.Singleton<IMailWriterSource>(sp => new UnifiedMailWriterSource(
                (IMailWriterSource)Create(sp, inner),
                sp.GetRequiredService<IUnifiedLogins>())));
        }
    }

    private static void AddPlugins(IServiceCollection services)
    {
        services.TryAddSingleton(sp =>
        {
            var paths = sp.GetRequiredService<IProfilePaths>();
            var prefs = sp.GetRequiredService<IPrefsStore>();
            return new NativePlugins(
                // user plugins (<config>/plugins) stay off until the M6b permission UI: ForApp sets no user root (Fable review 3 R3-04)
                () => NativePluginHostOptions.ForApp(AppContext.BaseDirectory, paths.DataDir) with
                {
                    Disabled = Disabled(prefs),
                    Bus = sp.GetService<HookBus>(),
                    Time = sp.GetRequiredService<TimeProvider>(),
                },
                () => prefs.Current.Bool(ShowCleanSpamKey, false));
        });
        services.Replace(ServiceDescriptor.Singleton<INativePluginHost>(sp => sp.GetRequiredService<NativePlugins>()));
        services.Replace(ServiceDescriptor.Singleton<IEmailDetailsSource>(sp => sp.GetRequiredService<NativePlugins>()));
        services.AddSingleton<IStartupTask>(sp => new PluginStartup(sp.GetRequiredService<NativePlugins>()));
        services.AddSingleton<IReaderBannerSource>(sp => new PluginDetailsBannerSource(sp.GetRequiredService<NativePlugins>()));
    }

    private static HashSet<string> Disabled(IPrefsStore prefs) =>
        prefs.Current.Values[DisabledPluginsKey] is JsonArray a
            ? [.. a.Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).OfType<string>()]
            : [];

    private static (IMailWriter Writer, Core.Jmap.IJmapClient Client)? AccountOf(IServiceProvider sp, Core.Mail.Model.MailboxRef target) =>
        sp.GetRequiredService<IUnifiedLogins>().ClientOf(target.AccountKey) is { } client && sp.GetRequiredService<IMailWriterSource>().WriterOf(target.AccountKey) is { } writer
            ? (writer, client)
            : null;

    private static object Create(IServiceProvider sp, ServiceDescriptor d) =>
        d.ImplementationInstance
        ?? d.ImplementationFactory?.Invoke(sp)
        ?? ActivatorUtilities.CreateInstance(sp, d.ImplementationType!);
}

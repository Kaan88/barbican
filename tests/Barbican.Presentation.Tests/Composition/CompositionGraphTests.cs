// New tests: the whole DI graph of the app resolves (M1 verify: a DI cycle BodyShell -> KeyRouter -> ... -> BodyShell hung the first launch and
// no test built the composition). The App head cannot be built in a test project, so this test registers the portable modules in the
// composition root's order, with stand-ins only for what the OS or a window provides, and resolves EVERY registered service. A cycle or a
// missing registration fails here instead of at launch. M1-plan §3.3. Owner mailcore.

#pragma warning disable MA0002, VSTHRD003
using Barbican.Core.Accounts;
using Barbican.Core.Auth;
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Editor;
using Barbican.Core.Jmap;
using Barbican.Core.Mail.Body;
using Barbican.Core.Storage;
using Barbican.Presentation.Accounts;
using Barbican.Presentation.Composition;
using Barbican.Presentation.Input;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Mail.Loading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Presentation.Tests.Composition;

public sealed class CompositionGraphTests
{
    internal sealed class TempProfile(string root) : IProfilePaths
    {
        public string Profile => "test";

        public string ConfigDir => Path.Combine(root, "config");

        public string DataDir => Path.Combine(root, "data");
    }

    private sealed class EmptyVault : IAccountVault
    {
        public Task<Result<IReadOnlyList<StoredAccount>>> LoadAsync(CancellationToken ct) => Task.FromResult(Result<IReadOnlyList<StoredAccount>>.Ok([]));

        public Task<Result<Unit>> SaveAsync(IReadOnlyList<StoredAccount> accounts, CancellationToken ct) => Task.FromResult(Result<Unit>.Ok(default(Unit)));
    }

    private sealed class NoBrowser : IAuthorizationLauncher
    {
        public Task<Result<Unit>> OpenAsync(Uri authorizeUri, CancellationToken ct) => Task.FromResult(Result<Unit>.Ok(default(Unit)));
    }

    /// <summary>What BodyShell is to the graph: a presenter that reaches the key router lazily (the cycle that hung a launch).</summary>
    private sealed class ShellLikePresenter(Lazy<IKeyRouter> keys) : IBodyPresenter
    {
        public Lazy<IKeyRouter> Keys => keys;

        public void Show(BodyRequest request)
        {
        }

        public void CancelPending()
        {
        }

        public void SetDimmed(bool dimmed)
        {
        }

        public void SetLoading(bool loading)
        {
        }

        public void Clear()
        {
        }
    }

    /// <summary>The M4 streams' portable registrations (App/Composition/AppComposition.cs adds them after the M3 features).</summary>
    private static void AddM4(ServiceCollection services)
    {
        services.AddBarbicanCalendarData();
        services.AddBarbicanCalendarEditing();
        services.AddBarbicanCalendarDataViewModels();
        services.AddBarbicanCalendarEditingViewModels();
        services.AddBarbicanCalendarViewsViewModels();
        services.AddBarbicanCalendarUiViewModels();
    }

    internal static ServiceCollection Compose(string root)
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IProfilePaths>(new TempProfile(root));
        services.AddSingleton<IAccountVault>(new EmptyVault());
        services.AddSingleton<IAuthorizationLauncher>(new NoBrowser());

        // the order of App/Composition/AppComposition.cs
        services.AddBarbicanCore();
        services.AddBarbicanMailCore();
        services.AddBarbicanAccounts();
        services.AddBarbicanAccountsPresentation();

        // the defaults of the M2 seams (stage F of M2; App/Composition/Features/ActionsRegistration.cs registers them first)
        services.AddBarbicanM2Defaults();

        // the M3 streams' portable registrations (App/Composition/AppComposition.cs adds them after the M2 features)
        services.AddBarbicanSendPipeline();
        services.AddBarbicanEditorCore();
        services.AddBarbicanEditorViewModels();
        services.AddBarbicanComposerViewModels();
        AddM4(services);

        // what MailBodyRegistration adds besides the WebView shell
        services.TryAddSingleton<ILinkRouter, StandInLinkRouter>();
        services.TryAddSingleton<IBlobSource, ActiveBlobSource>();
        services.TryAddSingleton<IMailActions, ReadOnlyMailActions>();
        services.TryAddSingleton(sp => new MessageFetcher(sp.GetRequiredService<Barbican.Core.Mail.Cache.IBodyCache>(), sp.GetRequiredService<IBlobSource>()));
        services.TryAddSingleton(sp => new MessagePrefetcher(sp.GetRequiredService<MessageFetcher>()));
        services.TryAddSingleton(sp => new ListNeighbours(sp));
        services.TryAddSingleton<IMessageNeighbours>(sp => sp.GetRequiredService<ListNeighbours>());
        services.AddSingleton<IStartupTask>(sp => sp.GetRequiredService<ListNeighbours>());
        services.Replace(ServiceDescriptor.Singleton<IBodyPresenter>(sp => new ShellLikePresenter(new Lazy<IKeyRouter>(sp.GetRequiredService<IKeyRouter>))));
        services.TryAddSingleton(sp =>
        {
            var prefs = sp.GetRequiredService<Barbican.Core.Storage.IPrefsStore>();
            var system = sp.GetRequiredService<Barbican.Core.Settings.Accessibility.ISystemAccessibilitySource>();
            var time = sp.GetRequiredService<TimeProvider>();
            return new MessageLoader(
                sp.GetRequiredService<ReaderViewModel>(),
                sp.GetRequiredService<IActiveMail>(),
                sp.GetRequiredService<MessageFetcher>(),
                sp.GetRequiredService<MessagePrefetcher>(),
                sp.GetRequiredService<IBodyPresenter>(),
                sp.GetRequiredService<IMessageNeighbours>(),
                sp.GetRequiredService<IMailActions>(),
                sp.GetRequiredService<Barbican.Core.Plugins.Hooks.IHookBus>(),
                sp.GetRequiredService<Barbican.Presentation.Common.IDispatcher>(),
                time,
                prefs,
                () => MailListSettings.Read(prefs.Current, system.Current).ReaderOptionsAt(time.GetUtcNow(), time.LocalTimeZone));
        });
        services.Replace(ServiceDescriptor.Singleton<IMessageOpener>(sp => sp.GetRequiredService<MessageLoader>()));
        services.TryAddSingleton<IConversationOpener>(sp => sp.GetRequiredService<MessageLoader>());
        return services;
    }

    private sealed class StandInLinkRouter : ILinkRouter
    {
        public void Route(Uri link)
        {
        }
    }

    [Fact]
    public async Task Every_registered_service_resolves_and_the_start_up_tasks_run_without_a_cycle()
    {
        var root = Path.Combine(Path.GetTempPath(), "barbican-di-" + Guid.NewGuid().ToString("N"));
        try
        {
            var services = Compose(root);
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

            // a cycle is an exception, but a lock-up must also fail rather than hang the run
            var resolving = Task.Run(() =>
            {
                var unresolved = new List<string>();
                foreach (var type in services.Select(d => d.ServiceType).Where(t => !t.ContainsGenericParameters).Distinct())
                {
                    try
                    {
                        _ = provider.GetServices(type).ToList();
                    }
                    catch (InvalidOperationException e)
                    {
                        unresolved.Add($"{type.Name}: {e.Message}");
                    }
                }

                return unresolved;
            });
            var done = await Task.WhenAny(resolving, Task.Delay(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
            Assert.Same(resolving, done);
            Assert.Empty(await resolving);

            Assert.NotNull(provider.GetRequiredService<MessageLoader>());
            Assert.Same(provider.GetRequiredService<IKeyRouter>(), ((ShellLikePresenter)provider.GetRequiredService<IBodyPresenter>()).Keys.Value);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}

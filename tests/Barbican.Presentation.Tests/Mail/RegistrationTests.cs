// New tests: AddBarbicanMailCore wires every ViewModel (a wiring mistake would otherwise show up as a crash at app start). M1-plan §3.3. Owner mailcore.

using Barbican.Core.Jmap;
using Barbican.Core.Plugins.Hooks;
using Barbican.Presentation.Composition;
using Barbican.Presentation.Input;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.Presentation.Tests.Mail;

public sealed class RegistrationTests
{
    [Fact]
    public void Every_view_model_and_service_resolves_without_an_account_manager()
    {
        using var provider = new ServiceCollection().AddBarbicanMailCore().BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.NotNull(provider.GetRequiredService<ShellViewModel>());
        Assert.NotNull(provider.GetRequiredService<MessageListViewModel>());
        Assert.NotNull(provider.GetRequiredService<SidebarViewModel>());
        Assert.NotNull(provider.GetRequiredService<RailViewModel>());
        Assert.NotNull(provider.GetRequiredService<GlanceTilesViewModel>());
        Assert.NotNull(provider.GetRequiredService<ReaderViewModel>());
        Assert.IsType<HeaderOnlyMessageOpener>(provider.GetRequiredService<IMessageOpener>());
        Assert.IsType<KeyRouter>(provider.GetRequiredService<IKeyRouter>());
        Assert.Same(provider.GetRequiredService<HookBus>(), provider.GetRequiredService<IHookBus>());
        Assert.Equal(2, provider.GetServices<IStartupTask>().Count());
    }

    [Fact]
    public void The_jmap_module_first_and_mailcore_second_still_share_one_hook_bus()
    {
        // the composition root's order (App/Composition/AppComposition.cs): a stand-in bus from the jmap module used to win every
        // later TryAdd, so the ViewModels and the sync engine heard nothing a plugin registered on HookBus
        using var provider = new ServiceCollection().AddBarbicanCore().AddBarbicanMailCore().BuildServiceProvider();
        Assert.Same(provider.GetRequiredService<HookBus>(), provider.GetRequiredService<IHookBus>());
        Assert.Single(new ServiceCollection().AddBarbicanCore().AddBarbicanMailCore(), d => d.ServiceType == typeof(IHookBus));
    }

    [Fact]
    public void The_view_models_are_singletons_so_every_view_shares_one_list()
    {
        using var provider = new ServiceCollection().AddBarbicanMailCore().BuildServiceProvider();
        Assert.Same(provider.GetRequiredService<MessageListViewModel>(), provider.GetRequiredService<MessageListViewModel>());
        Assert.Same(provider.GetRequiredService<ReaderViewModel>(), provider.GetRequiredService<ReaderViewModel>());
    }

    [Fact]
    public async Task Start_up_without_an_account_is_a_no_op()
    {
        using var provider = new ServiceCollection().AddBarbicanMailCore().BuildServiceProvider();
        foreach (var task in provider.GetServices<IStartupTask>())
        {
            await task.StartAsync(TestContext.Current.CancellationToken);
        }

        Assert.Empty(provider.GetRequiredService<MessageListViewModel>().Rows);
    }
}

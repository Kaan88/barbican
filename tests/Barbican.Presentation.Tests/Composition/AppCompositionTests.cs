// New tests (M2 plan 3.2 "Composition smoke test", M1.md section 5: no test built the App composition): the Core and Presentation part of the five M2 Add<Feature>() calls,
// with the defaults of every M2 seam (M2DefaultsRegistration is what ActionsRegistration calls first). The App-level part (views, overlays, slots) is checked by the hidden smoke run.
// Owner: stage F of M2, frozen afterwards; the streams add their own composition tests next to their code.

using Barbican.Core.Common;
using Barbican.Core.Mail.Lists;
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Menus;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.Presentation.Tests.Composition;

public sealed class AppCompositionTests
{
    private sealed class FakeCommand(string id, bool can) : IAppCommand
    {
        public string Id => id;

        public bool CanExecute(CommandContext context) => can;

        public Task<Result<Unit>> ExecuteAsync(CommandContext context, CancellationToken ct) => Task.FromResult(Result<Unit>.Ok(default(Unit)));
    }

    private static async Task WithProviderAsync(Action<IServiceCollection> extra, Func<ServiceProvider, Task> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "barbican-m2-di-" + Guid.NewGuid().ToString("N"));
        try
        {
            var services = CompositionGraphTests.Compose(root);
            extra(services);
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            await check(provider);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task The_m2_seams_have_behaviour_preserving_defaults()
    {
        await WithProviderAsync(
            _ => { },
            provider =>
            {
                // the composer registers its own commands (M3 composer-ui); every other seam is still empty
                Assert.Equal(Barbican.Presentation.Compose.ComposerCommands.Count, provider.GetRequiredService<ICommandRegistry>().All.Count);
                Assert.IsType<NoContextMenuService>(provider.GetRequiredService<IContextMenuService>());
                Assert.False(provider.GetRequiredService<IContextMenuService>().IsOpen);
                Assert.Empty(provider.GetRequiredService<IMailSelection>().Current);
                var query = new ListQuery { MailboxId = "inbox" };
                Assert.Same(query, provider.GetRequiredService<IListQueryShaper>().Shape(query));
                Assert.NotNull(provider.GetRequiredService<IListSourceResolver>());
                return Task.CompletedTask;
            });
    }

    [Fact]
    public async Task Commands_registered_by_several_streams_land_in_one_registry_and_the_later_id_wins()
    {
        await WithProviderAsync(
            services =>
            {
                services.AddSingleton<IAppCommand>(new FakeCommand(CommandIds.Archive, can: false));
                services.AddSingleton<IAppCommand>(new FakeCommand(CommandIds.Delete, can: true));
                services.AddSingleton<IAppCommand>(new FakeCommand(CommandIds.Archive, can: true));
            },
            provider =>
            {
                var registry = provider.GetRequiredService<ICommandRegistry>();
                Assert.Equal(2 + Barbican.Presentation.Compose.ComposerCommands.Count, registry.All.Count);
                Assert.True(registry.TryGet(CommandIds.Archive, out var archive));
                Assert.True(archive.CanExecute(new CommandContext(CommandSurface.Keyboard, [], null, null, null)));
                Assert.False(registry.TryGet(CommandIds.Undo, out _), "a command id without a handler is not registered (menus show it disabled)");
                return Task.CompletedTask;
            });
    }

    [Fact]
    public void Command_ids_are_unique()
    {
        var ids = typeof(CommandIds).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.True(ids.Count > 100, "the complete list of M2 plan 4.1 (about 130 ids)");
    }
}

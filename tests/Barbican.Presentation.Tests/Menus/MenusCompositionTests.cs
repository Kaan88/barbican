// New tests (M1.md section 5: a DI cycle hung the first launch; M2 plan 3.2): the Core and Presentation part of AddBarbicanMenus resolves completely, none of the registered
// services forms a cycle (the registry holds the shell commands, which reach the palette, which reads the registry), and the real controller replaces the do-nothing menu service. Owner menus.

using Barbican.Presentation.Commands;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Input;
using Barbican.Presentation.Menus;
using Barbican.Presentation.Palette;
using Barbican.Presentation.Tests.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.Presentation.Tests.Menus;

public sealed class MenusCompositionTests
{
    [Fact]
    public async Task The_menus_composition_resolves_without_a_cycle_and_registers_every_command_and_provider()
    {
        var root = Path.Combine(Path.GetTempPath(), "barbican-menus-di-" + Guid.NewGuid().ToString("N"));
        try
        {
            var services = CompositionGraphTests.Compose(root);
            _ = services.AddBarbicanMenusPresentation();
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            var resolving = Task.Run(() =>
            {
                var failed = new List<string>();
                foreach (var type in services.Select(d => d.ServiceType).Where(t => !t.ContainsGenericParameters).Distinct())
                {
                    try
                    {
                        _ = provider.GetServices(type).ToList();
                    }
                    catch (InvalidOperationException e)
                    {
                        failed.Add($"{type.Name}: {e.Message}");
                    }
                }

                return failed;
            });
            var done = await Task.WhenAny(resolving, Task.Delay(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
            Assert.Same(resolving, done);
            Assert.Empty(await resolving);

            var registry = provider.GetRequiredService<ICommandRegistry>();
            foreach (var id in new[] { CommandIds.OpenPalette, CommandIds.ShowShortcuts, CommandIds.GoMail, CommandIds.GoInbox, CommandIds.SwitchAccount, CommandIds.SignOut })
            {
                Assert.True(registry.TryGet(id, out _), id);
            }

            Assert.Equal(ShellCommands.Count, registry.All.Count);
            Assert.Same(provider.GetRequiredService<ContextMenuController>(), provider.GetRequiredService<IContextMenuService>());
            Assert.IsType<DialogService>(provider.GetRequiredService<IDialogService>());
            Assert.Equal(MenuProviders.Count, provider.GetServices<IMenuProvider>().Count());
            Assert.Equal(4, provider.GetServices<IModalSurface>().Count());
            Assert.NotNull(provider.GetRequiredService<PaletteController>());
            Assert.NotNull(provider.GetRequiredService<IKeyRouter>());
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

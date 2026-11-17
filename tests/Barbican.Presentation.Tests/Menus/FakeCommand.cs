// Test double for IAppCommand: records what it ran, enabled by a switch. Owner menus.

using Barbican.Core.Common;
using Barbican.Presentation.Commands;

namespace Barbican.Presentation.Tests.Menus;

internal sealed class FakeCommand(string id) : IAppCommand
{
    public string Id { get; } = id;

    public bool Enabled { get; set; } = true;

    public List<CommandContext> Ran { get; } = [];

    public bool CanExecute(CommandContext context) => Enabled;

    public Task<Result<Unit>> ExecuteAsync(CommandContext context, CancellationToken ct)
    {
        Ran.Add(context);
        return Task.FromResult(Result<Unit>.Ok(default));
    }
}

#if AUTOMATION
// Mirrors: spec/automation-protocol.md (M2 commands of the menus stream). The commands run on the UI thread and answer one line.

using Barbican.Automation;

namespace Barbican.App.Views.Menus.Automation;

/// <summary>An automation command that is a function.</summary>
public sealed class LambdaAutomationCommand(string name, Func<string, Task<string>> run) : IAutomationCommand
{
    public string Name { get; } = name;

    public Task<string> RunAsync(string args, CancellationToken cancellationToken) => run(args ?? string.Empty);
}
#endif

#if AUTOMATION
// Mirrors: spec/automation-protocol.md `key CHORD`: the chord goes through the same IKeyRouter a real key does (KeySource.Automation), no input is produced.

using Barbican.Automation;
using Barbican.Presentation.Input;

namespace Barbican.App.Shell.Automation;

/// <summary>The <c>key</c> command: replaces F1's "no key router yet" built-in.</summary>
public sealed class KeyCommand(IKeyRouter router) : IAutomationCommand
{
    public string Name => "key";

    public Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        if (!KeyChordParser.TryParse(args, out var chord))
        {
            return Task.FromResult("error bad chord " + args);
        }

        return Task.FromResult(router.Handle(chord, KeySource.Automation) ? "ok" : "unhandled");
    }
}
#endif

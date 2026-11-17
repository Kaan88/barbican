namespace Barbican.Automation;

/// <summary>
/// An automation command a feature adds (docs/milestones/M1-plan.md §4.4): register implementations in
/// the feature's own registration file under <c>#if AUTOMATION</c>; the server takes them all
/// (<c>IEnumerable&lt;IAutomationCommand&gt;</c>). <see cref="RunAsync"/> runs on the UI thread; the reply
/// is one line (<c>ok</c>, a value, or <c>error ...</c>). A feature command with the name of a built-in
/// command replaces the built-in (for example <c>key</c> once the key router exists).
/// </summary>
public interface IAutomationCommand
{
    string Name { get; }

    Task<string> RunAsync(string args, CancellationToken cancellationToken);
}

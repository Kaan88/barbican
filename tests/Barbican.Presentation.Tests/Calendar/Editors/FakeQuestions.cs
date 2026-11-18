using Barbican.Core.Calendar.Editing;
using Barbican.Presentation.Calendar.Editors;
using Barbican.Presentation.Compose;
namespace Barbican.Presentation.Tests.Calendar.Editors;

internal sealed class FakeQuestions : ICalendarQuestions
{
    public Queue<(EditScope Scope, bool Notify)?> Scopes { get; } = new();

    public Queue<string?> Answers { get; } = new();

    public List<string> Asked { get; } = [];

    public Task<(EditScope Scope, bool Notify)?> AskScopeAsync(string title, bool forDelete, bool hasGuests, CancellationToken ct)
    {
        Asked.Add(forDelete ? "scope-delete" : "scope");
        return Task.FromResult(Scopes.Count > 0 ? Scopes.Dequeue() : null);
    }

    public Task<string?> AskAsync(ComposerDialog dialog, CancellationToken ct)
    {
        Asked.Add(dialog.Id);
        return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : null);
    }
}

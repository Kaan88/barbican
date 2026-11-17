// The tag commands of TagCommands.Create, kept by id so the composition registers each as an IAppCommand. Owner actions (m2-close-a).

using Barbican.Presentation.Commands;

namespace Barbican.Presentation.Tags;

/// <summary>The tag commands by id.</summary>
/// <param name="Commands">One command per id of <see cref="TagCommands.Ids"/>.</param>
public sealed record TagCommandSet(IReadOnlyDictionary<string, IAppCommand> Commands);

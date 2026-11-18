// Holder so the DI registration builds the Files commands once. Owner files-ui.

using Barbican.Presentation.Commands;

namespace Barbican.Presentation.Files;

/// <summary>The Files commands, created once.</summary>
public sealed record FilesCommandSet(IReadOnlyList<IAppCommand> Commands);

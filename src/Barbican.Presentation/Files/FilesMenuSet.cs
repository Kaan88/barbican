// Holder so the DI registration builds the Files menu providers once. Owner files-ui.

using Barbican.Presentation.Menus;

namespace Barbican.Presentation.Files;

/// <summary>The Files menu providers, created once.</summary>
public sealed record FilesMenuSet(IReadOnlyList<IMenuProvider> Providers);

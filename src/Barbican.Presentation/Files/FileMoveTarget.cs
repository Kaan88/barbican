// One row of the move dialog. Owner files-ui.

using Barbican.Core.Files;

namespace Barbican.Presentation.Files;

/// <summary>A folder a move may go to, with its depth in the tree.</summary>
public sealed record FileMoveTarget(FileEntry Folder, int Depth);

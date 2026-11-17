// Mirrors: Bulwark lib/undo-history.ts:20-65 (an entry: label, undo action); M2 plan section 4.3. Contract from stage F of M2; owner actions.

using Barbican.Core.Common;

namespace Barbican.Presentation.Actions;

/// <summary>One undoable action.</summary>
public sealed record UndoEntry(string Label, Func<CancellationToken, Task<Result<Unit>>> Undo);

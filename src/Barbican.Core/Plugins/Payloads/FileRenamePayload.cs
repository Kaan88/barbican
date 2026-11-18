// Mirrors: Bulwark lib/plugin-hook-catalog.ts:224-:225 { file, newName } (onBeforeFileRename) and { file, previousName } (onFileRename). Owner files-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>onBeforeFileRename (<see cref="NewName"/>) and onFileRename (<see cref="PreviousName"/>): the file, with its new name after the rename.</summary>
public sealed record FileRenamePayload(FileResourceView File, string? NewName, string? PreviousName);

// Mirrors: Bulwark components/files/file-picker-dialog.tsx:270-297 (the path: "My files", then one crumb per folder opened). Owner composer-ui.

namespace Barbican.Presentation.Compose;

/// <summary>One step of the picker's path below "My files".</summary>
/// <param name="Key">The folder key, or <see cref="ComposerFilesPicker.SharedLocation"/>.</param>
/// <param name="Name">The name shown on the crumb.</param>
public sealed record ComposerFilesCrumb(string Key, string Name);

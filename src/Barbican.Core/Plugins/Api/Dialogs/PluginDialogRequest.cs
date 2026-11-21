namespace Barbican.Core.Plugins.Api.Dialogs;

/// <summary>A dialog request after Bulwark's defaults and size limits. A null title or label means the host default (the add-on's name, "OK", "Cancel").</summary>
public sealed record PluginDialogRequest(PluginDialogKind Kind, string? Title, string Message, string? ConfirmLabel,
    string? CancelLabel, bool Danger, IReadOnlyList<PluginDialogField> Fields);

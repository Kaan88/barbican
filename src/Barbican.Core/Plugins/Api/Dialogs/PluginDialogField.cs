namespace Barbican.Core.Plugins.Api.Dialogs;

/// <summary>One cleaned prompt field. <paramref name="Name"/> is the key of the answer and is never drawn.</summary>
public sealed record PluginDialogField(string Name, string Label, bool Password, string? Placeholder, bool Required);

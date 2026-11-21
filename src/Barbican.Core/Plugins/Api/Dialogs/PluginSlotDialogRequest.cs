using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Dialogs;

/// <summary>A ui.openDialog request after Bulwark's defaults and limits: which of the add-on's own slots to draw, the props its caller passed, and the dialog's size. A null title means the add-on's name.</summary>
public sealed record PluginSlotDialogRequest(string Slot, string Permission, string? Title, PluginValue ExtraProps, double? Width, bool Fill);

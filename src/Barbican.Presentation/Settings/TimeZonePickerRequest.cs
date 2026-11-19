using Barbican.Presentation.Dialogs;

namespace Barbican.Presentation.Settings;

/// <summary>The in-window searchable time-zone selector.</summary>
public sealed record TimeZonePickerRequest(string Selected, bool RequireSupportedZone = false) : DialogRequest<string>("Time zone");

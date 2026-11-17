// Mirrors: Bulwark onVisibilityChange (plugin-app-events.tsx:49). Owner mailcore.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The window became visible or hidden.</summary>
public sealed record VisibilityPayload(bool Visible);

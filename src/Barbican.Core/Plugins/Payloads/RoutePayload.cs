// Mirrors: Bulwark onNavigate / onRouteEnter / onRouteLeave (components/providers/plugin-app-events.tsx:110). Owner mailcore.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>An app view the user moved to or left (mail, calendar, contacts, files, settings).</summary>
public sealed record RoutePayload(string Route);

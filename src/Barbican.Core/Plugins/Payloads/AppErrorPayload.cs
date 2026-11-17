// Mirrors: Bulwark onAppError (plugin-app-events.tsx:56). Owner mailcore.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>An unhandled error, the message cut to 500 characters.</summary>
public sealed record AppErrorPayload(string Message);

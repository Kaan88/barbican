// Mirrors: Bulwark lib/plugin-hook-catalog.ts:158 onPushConnectionChange payload `{ connected: boolean }`. Owner jmap.

namespace Barbican.Core.Sync;

/// <summary>The payload of onPushConnectionChange.</summary>
/// <param name="Connected">True when live updates connected, false when they dropped.</param>
public sealed record PushConnectionPayload(bool Connected);

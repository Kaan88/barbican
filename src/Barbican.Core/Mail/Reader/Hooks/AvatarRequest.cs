// Mirrors: Bulwark lib/plugin-hook-catalog.ts:87 onAvatarResolve (transform of string | null with { email, name }). Owner reader.

namespace Barbican.Core.Mail.Reader.Hooks;

/// <summary>A sender's avatar request: a plugin may set <see cref="Url"/> (https only is used) or clear it.</summary>
public sealed record AvatarRequest(string Email, string? Name, string? Url);

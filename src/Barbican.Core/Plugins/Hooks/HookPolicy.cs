// Mirrors: Bulwark lib/plugin-hooks.ts:62-66 (DEFAULT_TIMEOUT_MS 5 s, INTERCEPT_TIMEOUT_MS 60 s: intercepts wait on dialogs), :169 (COMPOSE_SEND_TIMEOUT_MS
// 10 min), :186-188 (failClosed: an intercept guarding an action that cannot be taken back cancels it when a handler throws or runs out of time), :290-325
// (interceptOutcome, takeOver fails closed); fable-review-2 R2-10. Owner unified (M2 wave 2).

namespace Barbican.Core.Plugins.Hooks;

/// <summary>How long one handler of a hook may take, and whether a handler that throws or is late stops the action (intercepts only).</summary>
/// <param name="Budget">The per-handler budget; past it the handler is told to cancel, abandoned and reported.</param>
/// <param name="FailClosed">True for an intercept that guards an action that cannot be taken back (sending): a failing or late handler says no.</param>
public sealed record HookPolicy(TimeSpan Budget, bool FailClosed);

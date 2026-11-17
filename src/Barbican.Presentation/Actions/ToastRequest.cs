// Mirrors: Bulwark lib/email-action-toast.ts, lib/selection-toast.ts, lib/toast-duration.ts; M2 plan section 4.3. Contract from stage F of M2; owner actions.

namespace Barbican.Presentation.Actions;

/// <summary>A toast to show: the text, whether it is an error, and an optional action button (Undo).</summary>
public sealed record ToastRequest(string Message, bool IsError = false, string? ActionLabel = null, Func<CancellationToken, Task>? Action = null, TimeSpan? Duration = null);

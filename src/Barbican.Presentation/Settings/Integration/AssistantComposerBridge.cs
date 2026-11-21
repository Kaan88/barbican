// Mirrors: Rust agent.rs draft_email (open for review); B-AI-008; M3 composer leave guards.
// Integration: pool-thread assistant tools enter the composer through its dispatcher.
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Compose.Recipients;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Settings.Assistant;
using Barbican.Presentation.Common;
using Barbican.Presentation.Compose;

namespace Barbican.Presentation.Settings.Integration;

/// <summary>Opens assistant drafts through the normal composer and its replacement guards.</summary>
public sealed class AssistantComposerBridge(IDispatcher dispatcher, IComposerService? composer = null) : IAssistantComposer
{
    public async Task<Result<Unit>> OpenAsync(ComposeRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        var result = Result<Unit>.Fail(new ValidationError("The composer is unavailable."));
        if (composer is null)
        {
            return result;
        }

        await dispatcher.InvokeAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            var previous = composer.Current;
            await composer.OpenAsync(request, ct).ConfigureAwait(true);
            var current = composer.Current;
            if (current is null || ReferenceEquals(current, previous) || !string.Equals(current.AccountKey, request.AccountId, StringComparison.Ordinal))
            {
                result = Result<Unit>.Fail(new ValidationError("The composer was not opened."));
                return;
            }

            if (request.Kind is ComposeKind.Reply or ComposeKind.ReplyAll && request.Prefill is { } prefill)
            {
                ApplyReplyPrefill(current, prefill);
            }

            result = Result<Unit>.Ok(default);
        }).ConfigureAwait(false);
        return result;
    }

    private static void ApplyReplyPrefill(ComposerViewModel current, ComposePrefill prefill)
    {
        if (!string.IsNullOrWhiteSpace(prefill.To)) current.To.Replace(RecipientText.ParseList(prefill.To));
        if (!string.IsNullOrWhiteSpace(prefill.Cc)) current.Cc.Replace(RecipientText.ParseList(prefill.Cc));
        if (!string.IsNullOrWhiteSpace(prefill.Bcc)) current.Bcc.Replace(RecipientText.ParseList(prefill.Bcc));
        current.ShowCc = current.Cc.Text.Length > 0;
        current.ShowBcc = current.Bcc.Text.Length > 0;
        if (prefill.Subject.Length > 0) current.Subject = prefill.Subject;
        if (prefill.Body.Length > 0)
        {
            if (current.Editor is { } editor)
            {
                // Replace editable content while retaining the quoted original and reply metadata.
                editor.Session.ReplaceText(prefill.Body, whole: true);
            }
            else
            {
                current.FallbackBody = prefill.Body + "\n\n" + current.FallbackBody;
            }
        }
    }
}

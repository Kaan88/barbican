#if WINDOWS
// Mirrors Bulwark lib/outgoing/compose-takeover.ts and classification-raw.ts.
using System.Globalization;
using Barbican.Core.Compose;
using Barbican.Core.Compose.Recipients;
using Barbican.Core.Plugins.Api.RawSend;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.App.Plugins;

internal sealed class PluginComposeTakeover(PluginRuntimeBinding binding, PluginSessionCapture owner,
    ComposerTakeoverRequest request, IReadOnlyCollection<PluginRuntimeBinding> contributors, Func<bool> isCurrent, CancellationToken lifetime)
{
    private readonly Lock _gate = new();
    private bool _closed;
    private bool _claimed;
    private bool _issued;
    private RawMessageReceipt? _receipt;

    internal PluginRuntimeBinding Binding { get; } = binding;
    internal PluginSessionCapture Owner { get; } = owner;
    internal ComposerTakeoverRequest Request { get; } = request;
    internal IReadOnlyCollection<PluginRuntimeBinding> Bindings { get; } = [.. contributors.Append(binding).Distinct()];
    internal string Token { get; } = Guid.NewGuid().ToString("N");

    internal bool TryClaim(PluginRawSendRequest raw)
    {
        lock (_gate)
        {
            if (_closed || _claimed || lifetime.IsCancellationRequested ||
                !string.Equals(raw.LabelToken, Token, StringComparison.Ordinal) ||
                !string.Equals(raw.IdentityId, Request.Payload.IdentityId, StringComparison.Ordinal) ||
                !ScheduleMatches(raw.ScheduledAt) || !EnvelopeMatches(raw.EnvelopeRecipients)) return false;
            return _claimed = true;
        }
    }

    internal bool TryIssue(bool submission, Func<bool> issue)
    {
        lock (_gate)
        {
            if (_closed || !_claimed || lifetime.IsCancellationRequested || !isCurrent()) return false;
            if (!submission) return issue();
            // Once issuance is attempted, even a thrown transport result must prohibit fallback.
            _issued = true;
            return issue();
        }
    }

    internal void Record(RawMessageReceipt receipt)
    {
        lock (_gate)
        {
            if (!_closed && _claimed && _issued) _receipt = receipt;
        }
    }

    internal void Close()
    {
        lock (_gate) _closed = true;
    }

    internal (bool Issued, RawMessageReceipt? Receipt) Outcome()
    {
        lock (_gate) return (_issued, _receipt);
    }

    private bool ScheduleMatches(DateTimeOffset? scheduled) =>
        scheduled is null ? Request.Options.ScheduledAt is null :
            DateTimeOffset.TryParse(Request.Payload.DelayedUntil, CultureInfo.InvariantCulture, DateTimeStyles.None, out var expected) && expected == scheduled;

    private bool EnvelopeMatches(IReadOnlyList<string>? supplied) =>
        supplied is null || supplied.Select(RecipientText.ToAsciiEmail).ToHashSet(StringComparer.Ordinal)
            .SetEquals(Request.EnvelopeRecipients.Select(RecipientText.ToAsciiEmail));
}
#endif

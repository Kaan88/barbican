// Mirrors Bulwark lib/jmap/client.ts rejectedRecipients and getEmailSubmissionSendAt; RFC 8621 section 7 EmailSubmission/get.
using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Compose.Recipients;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Compose;

/// <summary>Optional guarded observation after a raw submission was accepted.</summary>
internal static class RawSubmissionReadback
{
    internal static async Task<RawSubmissionObservation?> ReadAsync(ComposeContext ctx, string id, IReadOnlyList<string> recipients,
        Func<Func<bool>, CancellationToken, Task<bool>> fence, CancellationToken ct)
    {
        var call = JmapRequest.Call("EmailSubmission/get", new JsonObject { ["accountId"] = ctx.SubmissionAccount,
            ["ids"] = new JsonArray(id), ["properties"] = new JsonArray("id", "sendAt", "deliveryStatus") }, "raw-readback");
        var response = await ctx.Client.SendAsync(JmapRequest.Of(call) with { BeforeSend = fence }, ct).ConfigureAwait(false);
        return response.IsOk ? ParseResponse(response.Value!, ctx.SubmissionAccount, id, recipients) : null;
    }

    internal static RawSubmissionObservation? ParseResponse(JmapResponse response, string account, string id, IReadOnlyList<string> recipients)
    {
        if (response.MethodResponses.Count != 1) return null;
        var item = response.MethodResponses[0];
        if (item.Count != 3 || !string.Equals(item[0].AsString(), "EmailSubmission/get", StringComparison.Ordinal) ||
            !string.Equals(item[2].AsString(), "raw-readback", StringComparison.Ordinal) || item[1] is not JsonObject body ||
            !string.Equals(body["accountId"].AsString(), account, StringComparison.Ordinal) ||
            (body.ContainsKey("notFound") && (body["notFound"] is not JsonArray missing || missing.Count != 0)) ||
            body["list"] is not JsonArray list || list.Count != 1 || list[0] is not JsonObject submission ||
            !string.Equals(submission["id"].AsString(), id, StringComparison.Ordinal)) return null;
        return Parse(submission, recipients);
    }

    internal static RawSubmissionObservation Parse(JsonObject submission, IReadOnlyList<string> recipients)
    {
        DateTimeOffset? sendAt = null;
        if (submission["sendAt"].AsString() is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) &&
            (text.EndsWith('Z') || (text.Length >= 6 && text[^6] is '+' or '-' && text[^3] == ':'))) sendAt = date.ToUniversalTime();
        var rejected = new List<RawRejectedRecipient>();
        var status = submission["deliveryStatus"] as JsonObject;
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var recipient in recipients)
        {
            var key = AddressKey(recipient);
            if (key is null || !expected.TryAdd(key, recipient)) return new(sendAt, [], false, RawDeliveryStatus.Invalid);
        }
        var expectedRecipients = expected.Values.ToArray();
        if (!submission.ContainsKey("deliveryStatus") || submission["deliveryStatus"] is null)
            return new(sendAt, [], false) { ExpectedRecipients = expectedRecipients };
        if (expected.Count == 0 || status is null || status.Count > expected.Count) return new(sendAt, [], false, RawDeliveryStatus.Invalid);
        var matched = new HashSet<string>(StringComparer.Ordinal);
        var deliveredRecipients = new List<string>();
        var ambiguous = false;
        foreach (var entry in status)
        {
            var key = AddressKey(entry.Key);
            if (key is null || !expected.TryGetValue(key, out var original) || !matched.Add(key) || entry.Value is not JsonObject value)
            {
                ambiguous = true;
                continue;
            }
            var delivered = value["delivered"].AsString();
            if (delivered is not ("yes" or "no" or "unknown" or "queued")) { ambiguous = true; continue; }
            if (string.Equals(delivered, "yes", StringComparison.Ordinal)) deliveredRecipients.Add(original);
            if (!string.Equals(delivered, "no", StringComparison.Ordinal)) continue;
            var replyNode = value["smtpReply"];
            var reply = replyNode.AsString();
            if ((replyNode is not null && reply is null) || reply is { Length: > 1024 } || (reply?.Any(char.IsControl) ?? false))
            {
                ambiguous = true;
                continue;
            }
            rejected.Add(new(original, reply?.Trim() ?? string.Empty));
        }
        var all = !ambiguous && matched.Count == expected.Count && rejected.Count == expected.Count;
        return new(sendAt, rejected.AsReadOnly(), all, ambiguous ? RawDeliveryStatus.Invalid : RawDeliveryStatus.Valid)
            { ExpectedRecipients = expectedRecipients, DeliveredRecipients = deliveredRecipients.AsReadOnly() };
    }
    internal static RawSubmissionObservation? Merge(RawSubmissionObservation? initial, RawSubmissionObservation? observed)
    {
        if (initial is null) return observed;
        if (observed is null) return initial;
        var sendAt = observed.SendAt ?? initial.SendAt;
        if (initial.Status == RawDeliveryStatus.Invalid || observed.Status == RawDeliveryStatus.Invalid)
            return initial with { SendAt = sendAt, AllRefused = false, Status = RawDeliveryStatus.Invalid };
        if (!initial.ExpectedRecipients.ToHashSet(StringComparer.Ordinal).SetEquals(observed.ExpectedRecipients))
            return initial with { SendAt = sendAt, AllRefused = false, Status = RawDeliveryStatus.Invalid };
        var delivered = initial.DeliveredRecipients.Concat(observed.DeliveredRecipients).ToHashSet(StringComparer.Ordinal);
        var rejected = initial.RejectedRecipients.Concat(observed.RejectedRecipients)
            .GroupBy(item => item.Email, StringComparer.Ordinal).Select(group => group.Last()).ToArray();
        var contradiction = rejected.Any(item => delivered.Contains(item.Email));
        var status = contradiction ? RawDeliveryStatus.Invalid :
            initial.Status == RawDeliveryStatus.Valid || observed.Status == RawDeliveryStatus.Valid ? RawDeliveryStatus.Valid : RawDeliveryStatus.Missing;
        return new(sendAt, rejected, !contradiction && initial.ExpectedRecipients.Count > 0 &&
            rejected.Length == initial.ExpectedRecipients.Count, status)
        {
            ExpectedRecipients = initial.ExpectedRecipients,
            DeliveredRecipients = [.. delivered],
        };
    }

    private static string? AddressKey(string address)
    {
        if (address.Length > 254 || !RecipientText.IsValidEmail(address) || address.Any(char.IsControl)) return null;
        var at = address.LastIndexOf('@');
        var domain = RecipientText.ToAsciiDomain(address[(at + 1)..]);
        return domain is null ? null : address[..at] + "@" + domain;
    }
}

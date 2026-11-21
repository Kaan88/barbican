// Mirrors: Bulwark lib/phishing-report.ts sendPhishingReport :105 (one report per message to the admin's address, from the login's own identity, no copy in
// Sent, no pre-send checks or finalisers: the address is the admin's and the report is not the user's mail) and lib/phishing-report-arf.ts
// buildArfReportBlob :132 (the original downloaded from its own account; too big for the server's upload limit fails before the download).
// The send is the host raw submission (RawMessageSender with SubmitOnly: Email/import into Drafts, EmailSubmission/set with the explicit
// envelope, onSuccessDestroyEmail), RFC 8621 sections 4.8 and 7.5. Never on a read-only account. Owner reader.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Reader;
using Barbican.Core.OrganizationPolicy;

namespace Barbican.Core.Mail.Safety;

/// <summary>Sends the phishing report of one message.</summary>
public sealed class PhishingReporter(IAccountManager accounts, IIdentityService identities, RawMessageSender sender, TimeProvider time) : IPhishingReporter
{
    private static readonly string Version = typeof(PhishingReporter).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    /// <summary>Sends one report for <paramref name="message"/> to the address of <paramref name="setup"/>; a failure names why nothing went out.</summary>
    public async Task<Result<Unit>> ReportAsync(MailTarget message, PhishingReportPolicy setup, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(setup);
        if (!setup.IsOn)
        {
            return Fail("Reporting is not set up for this account.");
        }

        if (accounts.SessionFor(message.Login) is not { } session)
        {
            return Fail("The account is not signed in.");
        }

        var client = session.Client;
        if (client.IsReadOnly)
        {
            return Fail("Reports are not sent from a read-only account.");
        }

        var listed = await identities.ListAsync(message.Login, ct).ConfigureAwait(false);
        if (!listed.IsOk)
        {
            return Result<Unit>.Fail(listed.Error!);
        }

        if (PhishingReportRules.ReporterIdentity(listed.Value!.Select(i => i.Identity), client.Session.Username) is not { } identity)
        {
            return Fail("No sender address to report from.");
        }

        var fetched = await FetchAsync(message, client, ct).ConfigureAwait(false);
        if (!fetched.IsOk)
        {
            return Result<Unit>.Fail(fetched.Error!);
        }

        var (email, original) = fetched.Value;
        var at = DateTimeOffset.TryParse(email.Str("receivedAt"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var received) ? received : (DateTimeOffset?)null;
        var input = new PhishingReportMime.Input(email.Str("subject") ?? string.Empty, identity.Email, identity.Name, setup.Address, at,
            (email["from"] as JsonArray)?.FirstOrDefault()?.Str("email"), DeliveredTo(email, identity.Email))
        {
            AppVersion = Version,
            FileName = ReaderNames.EmlExportName(email.Str("subject"), at, TimeZoneInfo.Local),
        };
        var now = time.GetUtcNow();
        var raw = setup.Format == PhishingReportFormat.Arf ? PhishingReportMime.Arf(input, original, now) : PhishingReportMime.Forward(input, original, now);
        if (client.Session.Limits.MaxSizeUpload is > 0 and var limit && raw.Length > limit)
        {
            return Fail("The message is too big to report on this server.");
        }

        var sent = await sender.SendAsync(
            session,
            new RawMessageRequest(message.Login, identity.Id, raw, [setup.Address]) { SubmitOnly = true },
            (issue, _) => Task.FromResult(issue()),
            ct).ConfigureAwait(false);
        return sent.IsOk ? Result<Unit>.Ok(default) : Result<Unit>.Fail(sent.Error!);
    }

    // the message's own fields and bytes, from the account that holds it; a message the server would refuse to take back is not downloaded
    private static async Task<Result<(JsonNode Email, byte[] Original)>> FetchAsync(MailTarget message, Jmap.IJmapClient client, CancellationToken ct)
    {
        var account = message.JmapAccountId(client.Session.MailAccountId);
        var got = await ReaderCalls.CallAsync(client, "Email/get", new JsonObject
        {
            ["accountId"] = account,
            ["ids"] = new JsonArray(message.ServerId),
            ["properties"] = new JsonArray("blobId", "subject", "receivedAt", "from", "to", "size"),
        }, ct).ConfigureAwait(false);
        if (!got.IsOk)
        {
            return Result<(JsonNode, byte[])>.Fail(got.Error!);
        }

        if ((got.Value!["list"] as JsonArray)?.FirstOrDefault() is not { } email || email.Str("blobId") is not { Length: > 0 } blob)
        {
            return Result<(JsonNode, byte[])>.Fail(new ValidationError("The message has no content to report."));
        }

        var size = email["size"] is { } s && s.GetValueKind() == JsonValueKind.Number ? s.GetValue<long>() : 0;
        if (client.Session.Limits.MaxSizeUpload is > 0 and var limit && size + 8192 > limit)
        {
            return Result<(JsonNode, byte[])>.Fail(new ValidationError("The message is too big to report on this server."));
        }

        var download = await client.DownloadAsync(account, blob, "message.eml", "message/rfc822", ct).ConfigureAwait(false);
        if (!download.IsOk)
        {
            return Result<(JsonNode, byte[])>.Fail(download.Error!);
        }

        var stream = download.Value!;
        await using (stream.ConfigureAwait(false))
        {
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy, ct).ConfigureAwait(false);
            return Result<(JsonNode, byte[])>.Ok((email, copy.ToArray()));
        }
    }

    // the address the original was delivered to: the reporter's own address among the To recipients, else none
    private static string? DeliveredTo(JsonNode email, string reporter) =>
        (email["to"] as JsonArray)?.Select(a => a.Str("email")).FirstOrDefault(a => string.Equals(a, reporter, StringComparison.OrdinalIgnoreCase));

    private static Result<Unit> Fail(string text) => Result<Unit>.Fail(new ValidationError(text));
}

using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Tests.Compose;

public sealed class RawMessageSenderTests
{
    private const string SenderAddress = "user@example.org";
    private const string EnvelopeRecipient = "approved@example.net";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Raw_submission_uses_one_linked_request_and_the_explicit_envelope(bool submitOnly)
    {
        var rig = await PipelineRig.StartAsync(PipelineRig.M3Options() with { RfcSubmissionFollowupResponse = true });
        var request = Request() with { SubmitOnly = submitOnly };
        request = request with { Bytes = [.. request.Bytes, 0x00, 0x80, 0xff] };
        var result = await SendAsync(rig, request, AllowAsync);

        Assert.True(result.IsOk, result.Error?.Message);
        var submission = Assert.Single(rig.Api.SubmissionStates);
        Assert.Equal(submission.Id, result.Value!.SubmissionId);
        var arguments = Assert.Single(rig.Api.Submissions);
        Assert.Equal("i1", (string?)arguments["identityId"]);
        Assert.Equal(SenderAddress, (string?)arguments["envelope"]?["mailFrom"]?["email"]);
        Assert.Equal(EnvelopeRecipient, (string?)arguments["envelope"]?["rcptTo"]?[0]?["email"]);
        Assert.Contains("Email/import,EmailSubmission/set", rig.Fake.Server.ApiCalls, StringComparer.Ordinal);
        Assert.Empty(rig.InFolder("drafts"));
        var uploadedBlobId = rig.Fake.Server.LastUploadedBlobId;
        Assert.NotNull(uploadedBlobId);
        Assert.Equal(request.Bytes, rig.Store.Blob(uploadedBlobId));
        if (submitOnly)
        {
            Assert.Null(result.Value.EmailId);
            Assert.Null(rig.Store.Email(submission.EmailId));
        }
        else
        {
            Assert.Equal(submission.EmailId, result.Value.EmailId);
            Assert.Contains(submission.EmailId, rig.InFolder("sent"), StringComparer.Ordinal);
            Assert.False(rig.Store.Email(submission.EmailId)?["keywords"]?["$draft"]?.GetValue<bool>() ?? false);
        }
    }

    [Fact]
    public async Task Raw_submission_rechecks_authority_after_upload()
    {
        var rig = await PipelineRig.StartAsync();
        var checkedCount = 0;
        var result = await SendAsync(rig, Request(), (issue, _) => Task.FromResult(++checkedCount == 1 && issue()));

        Assert.False(result.IsOk);
        Assert.IsNotType<RawMessageOutcomeUnknown>(result.Error);
        Assert.Equal(2, checkedCount);
        Assert.DoesNotContain(rig.Fake.Server.ApiCalls, call => call.Contains("Email/import", StringComparison.Ordinal));
        Assert.Empty(rig.Api.Submissions);
        Assert.Empty(rig.InFolder("drafts"));
    }

    [Theory]
    [InlineData("accountId", "other-account")]
    [InlineData("blobId", " ")]
    [InlineData("size", "not-a-size")]
    public async Task An_unverified_upload_receipt_never_reaches_submission(string property, string replacement)
    {
        var rig = await PipelineRig.StartAsync();
        rig.Fake.Server.EditUploadReceipt = receipt => receipt[property] = replacement;

        var result = await SendAsync(rig, Request(), AllowAsync);

        Assert.IsType<TransportError>(result.Error);
        Assert.NotNull(rig.Fake.Server.LastUploadedBlobId);
        Assert.DoesNotContain(rig.Fake.Server.ApiCalls, call => call.Contains("Email/import", StringComparison.Ordinal));
        Assert.Empty(rig.Api.Submissions);
    }

    [Fact]
    public async Task A_mismatched_upload_size_never_reaches_submission()
    {
        var rig = await PipelineRig.StartAsync();
        rig.Fake.Server.EditUploadReceipt = receipt => receipt["size"] = 1;

        var result = await SendAsync(rig, Request(), AllowAsync);

        Assert.IsType<TransportError>(result.Error);
        Assert.Empty(rig.Api.Submissions);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task A_refreshed_primary_account_cannot_receive_the_old_accounts_message(int refreshAt)
    {
        var rig = await PipelineRig.StartAsync();
        var attempts = 0;
        var result = await SendAsync(rig, Request(), async (issue, token) =>
        {
            if (++attempts == refreshAt)
            {
                rig.Fake.Server.ChangeAdvertisedPrimaryAccount("other-account");
                Assert.True(await rig.Fake.Client.RefreshSessionAsync(token));
                Assert.Equal("other-account", rig.Fake.Client.Session.MailAccountId);
            }

            return issue();
        });

        Assert.False(result.IsOk);
        Assert.IsNotType<RawMessageOutcomeUnknown>(result.Error);
        Assert.Equal(refreshAt, attempts);
        Assert.DoesNotContain(rig.Fake.Server.ApiCalls, call => call.Contains("Email/import", StringComparison.Ordinal));
        Assert.Empty(rig.Api.Submissions);
    }

    [Fact]
    public async Task A_definite_submission_refusal_cleans_up_the_import()
    {
        var rig = await PipelineRig.StartAsync();
        rig.Api.ForbiddenMailFrom.Add(SenderAddress);
        var attempts = 0;
        var result = await SendAsync(rig, Request(), (issue, _) => Task.FromResult(++attempts <= 2 && issue()));

        Assert.IsType<SetError>(result.Error);
        Assert.Equal(2, attempts);
        Assert.Contains("Email/import,EmailSubmission/set", rig.Fake.Server.ApiCalls, StringComparer.Ordinal);
        Assert.Contains("Email/set", rig.Fake.Server.ApiCalls, StringComparer.Ordinal);
        Assert.Empty(rig.Api.Submissions);
        Assert.Empty(rig.InFolder("drafts"));
    }

    [Fact]
    public async Task A_lost_response_keeps_the_accepted_submission_and_its_copy()
    {
        var rig = await PipelineRig.StartAsync();
        var attempts = 0;
        var result = await SendAsync(rig, Request(), (issue, _) =>
        {
            if (++attempts == 2) rig.Fake.Server.FailApiResponseAfterApplyingRequest();
            return Task.FromResult(issue());
        });

        Assert.IsType<RawMessageOutcomeUnknown>(result.Error);
        Assert.Equal(2, attempts);
        var accepted = Assert.Single(rig.Api.SubmissionStates);
        Assert.Contains(accepted.EmailId, rig.InFolder("sent"), StringComparer.Ordinal);
        Assert.DoesNotContain("Email/set", rig.Fake.Server.ApiCalls, StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_one_call_limit_rejects_submission_before_upload()
    {
        var rig = await PipelineRig.StartAsync(PipelineRig.M3Options() with { MaxCallsInRequest = 1 });
        var result = await SendAsync(rig, Request(), AllowAsync);

        Assert.IsType<ValidationError>(result.Error);
        Assert.DoesNotContain(rig.Fake.Server.Requests, call => call.StartsWith("POST upload", StringComparison.Ordinal));
        Assert.Empty(rig.Api.Submissions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Only_consistent_all_refused_readback_may_remove_the_sent_copy(bool contradictory)
    {
        var rig = await PipelineRig.StartAsync();
        rig.Api.EditSubmissionGet = body =>
        {
            var entry = (JsonObject)body["list"]![0]!;
            entry["deliveryStatus"] = new JsonObject
            {
                [EnvelopeRecipient] = new JsonObject { ["delivered"] = "no", ["smtpReply"] = "550 Refused" },
            };
            if (contradictory) body["notFound"] = new JsonArray((string?)entry["id"]);
        };

        var result = await SendAsync(rig, Request(), AllowAsync);

        Assert.Equal(contradictory, result.IsOk);
        var accepted = Assert.Single(rig.Api.SubmissionStates);
        Assert.Equal(contradictory, rig.InFolder("sent").Contains(accepted.EmailId, StringComparer.Ordinal));
        if (!contradictory) Assert.Equal("recipientsRejected", Assert.IsType<SetError>(result.Error).Type);
    }

    [Theory]
    [InlineData("From: other@example.org\r\n")]
    [InlineData("From: user@example.org\r\nFrom: other@example.org\r\n")]
    [InlineData("From: user@example.org, other@example.org\r\n")]
    public async Task Invalid_or_ambiguous_from_is_rejected_before_upload(string header)
    {
        var rig = await PipelineRig.StartAsync();
        var result = await SendAsync(rig, Request(header), AllowAsync);

        Assert.IsType<ValidationError>(result.Error);
        Assert.DoesNotContain(rig.Fake.Server.Requests, call => call.StartsWith("POST upload", StringComparison.Ordinal));
        Assert.Empty(rig.Api.Submissions);
    }

    [Fact]
    public void Raw_header_envelope_uses_visible_group_and_cc_addresses()
    {
        var bytes = Encoding.UTF8.GetBytes("From: user@example.org\r\nTo: Group: one@example.net, two@example.net;\r\n" +
            "Cc: three@example.net\r\nBcc:\r\n\r\nBody");

        Assert.True(RawMessageHeaders.TryRead(bytes, SenderAddress, true, true, out var recipients));
        Assert.Equal(["one@example.net", "two@example.net", "three@example.net"], recipients, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("Bcc: blind@example.org\r\n")]
    [InlineData("Resent-Bcc:\r\n blind@example.org\r\n")]
    [InlineData("Bcc : blind@example.org\r\n")]
    [InlineData("X-Injected: text\rBcc: blind@example.org\r\n")]
    public void Raw_blind_recipient_headers_are_rejected_even_with_an_explicit_envelope(string header)
    {
        var request = Request("From: user@example.org\r\n" + header);

        Assert.False(RawMessageHeaders.TryRead(request.Bytes, SenderAddress, true, false, out _));
    }

    private static RawMessageRequest Request(string from = "From: Test Sender <user@example.org>\r\n") => new(
        PipelineRig.Account, "i1",
        Encoding.UTF8.GetBytes(from + "To: header-only@example.org\r\nSubject: Raw synthetic message\r\n" +
            "MIME-Version: 1.0\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nByte-exact synthetic payload.\r\n"),
        [EnvelopeRecipient]);

    private static Task<bool> AllowAsync(Func<bool> issue, CancellationToken _) => Task.FromResult(issue());

    private static Task<Result<RawMessageReceipt>> SendAsync(PipelineRig rig, RawMessageRequest request,
        Func<Func<bool>, CancellationToken, Task<bool>> authority) =>
        new RawMessageSender(rig.Accounts, rig.Clock)
            .SendAsync(rig.Accounts.SessionFor(PipelineRig.Account)!, request, authority, Ct);
}

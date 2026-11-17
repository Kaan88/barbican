using System.Text.Json.Nodes;
using Barbican.Core.Compose;
using Barbican.Core.Editor;
using Barbican.Core.Storage;

namespace Barbican.Core.Tests.Compose;

/// <summary>Rust compose.rs pre_send_warning (no Rust test) and Bulwark's external-recipient confirm (G-054), as pure checks; ComposeSettings.Read defaults equal Rust's.</summary>
public sealed class SendWarningsTests
{
    private static ComposeDraft Draft(string subject = "Hi", string body = "Hello", string to = "a@own.example", IReadOnlyList<DraftAttachment>? attachments = null)
    {
        var doc = new Doc([Block.Para(body)]);
        return new ComposeDraft { AccountId = "a", IdentityId = "i", Subject = subject, Body = doc, To = to, Attachments = attachments ?? [] };
    }

    private static readonly string[] Own = ["own.example"];

    private static IReadOnlyList<SendWarningKind> Kinds(ComposeDraft d, ComposeSettings? s = null) => [.. SendWarnings.Check(d, s ?? new ComposeSettings(), Own).Select(w => w.Kind)];

    [Fact]
    public void Nothing_to_warn_about()
    {
        Assert.Empty(SendWarnings.Check(Draft(), new ComposeSettings(), Own));
    }

    [Fact]
    public void Empty_subject_warns_unless_switched_off()
    {
        Assert.Equal([SendWarningKind.EmptySubject], Kinds(Draft(subject: "  ")));
        Assert.Empty(Kinds(Draft(subject: ""), new ComposeSettings { EmptySubjectWarning = false }));
    }

    [Fact]
    public void Placeholders_in_what_the_user_wrote_not_in_the_quote()
    {
        var d = Draft(body: "Meet at [TIME]");
        var w = Assert.Single(SendWarnings.Check(d, new ComposeSettings(), Own));
        Assert.Equal(SendWarningKind.UnfilledPlaceholders, w.Kind);
        Assert.Equal(["[TIME]"], w.Items);
        Assert.Empty(Kinds(d, new ComposeSettings { PlaceholderWarning = false }));

        var quoted = new Doc([Block.Para("fine"), new Block(new BlockKind.Quoted("<p>[TIME]</p>", "[TIME]", false, true))]);
        Assert.Empty(Kinds(Draft() with { Body = quoted }));
    }

    [Fact]
    public void Mentioned_attachment_with_none_attached()
    {
        Assert.Equal([SendWarningKind.MissingAttachment], Kinds(Draft(body: "see the file Attached")));
        Assert.Equal([SendWarningKind.MissingAttachment], Kinds(Draft(body: "ich schicke es anbei")));
        Assert.Empty(Kinds(Draft(body: "see attached"), new ComposeSettings { AttachmentReminder = false }));
        Assert.Empty(Kinds(Draft(body: "see attached", attachments: [new DraftAttachment { Name = "a.pdf", BlobId = "b" }])));

        // an inline image resent from a quote is no attachment
        Assert.Equal([SendWarningKind.MissingAttachment], Kinds(Draft(body: "attached", attachments: [new DraftAttachment { Name = "i.png", BlobId = "b", Cid = "i@x" }])));
    }

    [Fact]
    public void External_recipients_by_identity_domain()
    {
        var w = Assert.Single(SendWarnings.Check(Draft(to: "a@own.example, Bob <b@OTHER.example>", body: "x") with { Cc = "c@other.example", Bcc = "B@other.example" }, new ComposeSettings(), Own));
        Assert.Equal(SendWarningKind.ExternalRecipients, w.Kind);
        Assert.Equal(["b@OTHER.example", "c@other.example"], w.Items);
    }

    [Fact]
    public void Warnings_come_in_the_dialogs_order()
    {
        var d = Draft(subject: "", body: "TODO see attached", to: "x@other.example");
        Assert.Equal([SendWarningKind.EmptySubject, SendWarningKind.UnfilledPlaceholders, SendWarningKind.MissingAttachment, SendWarningKind.ExternalRecipients], Kinds(d));
    }

    [Fact]
    public void Compose_settings_defaults_equal_rusts()
    {
        var s = ComposeSettings.Read(PrefsSnapshot.FromJson(new JsonObject()));
        Assert.Equal(5, s.SendDelaySeconds);
        Assert.False(s.AutoSelectReplyIdentity);
        Assert.Equal("domain", s.ReplyIdentityMatch);
        Assert.True(s.ReplyUnderMessage);
        Assert.True(s.SignatureNewMail);
        Assert.True(s.SignatureReplyForward);
        Assert.Equal("below_quote", s.SignaturePosition);
        Assert.True(s.SignatureSeparator);
        Assert.False(s.PlainTextMode);
        Assert.True(s.AttachmentReminder);
        Assert.True(s.EmptySubjectWarning);
        Assert.True(s.PlaceholderWarning);
        Assert.False(s.SendConfirmation);
        Assert.Equal("+", s.SubAddressDelimiter);
        Assert.Equal("auto", s.WritingLanguage);

        var set = ComposeSettings.Read(PrefsSnapshot.FromJson(JsonNode.Parse("""{"sendDelaySeconds": 0, "signaturePosition": "above_quote", "plainTextMode": true}""")!.AsObject()));
        Assert.Equal(0, set.SendDelaySeconds);
        Assert.Equal("above_quote", set.SignaturePosition);
        Assert.True(set.PlainTextMode);
    }
}

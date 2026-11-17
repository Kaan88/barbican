// Ported from Rust src/msgload.rs tests (:386-:405).
// Rust's None and Some(Value::Null) both become a null Keywords (a copy that is not an object is skipped either way).

using Barbican.Core.Mail.Model;
using Barbican.Presentation.Mail.Loading;

namespace Barbican.Presentation.Tests.Mail.Loading;

public sealed class MessageLoadRulesTests
{
    [Fact]
    [RustTest("msgload::unread_state_comes_from_the_list_first")]
    public void Unread_state_comes_from_the_list_first()
    {
        var unread = Keywords.None;
        var read = Keywords.Of("$seen");
        Assert.True(MessageLoadRules.WasUnseen(unread, read), "the list's copy wins");
        Assert.False(MessageLoadRules.WasUnseen(read, unread));
        Assert.True(MessageLoadRules.WasUnseen(null, unread), "a sibling not in the list: the cached copy");
        Assert.False(MessageLoadRules.WasUnseen(null, read));
        Assert.Null(MessageLoadRules.WasUnseen(null, null));
    }

    [Fact]
    [RustTest("msgload::inline_image_size")]
    public void Inline_image_size()
    {
        var m = new EmailBody
        {
            Id = "1",
            Attachments =
            [
                new Attachment { Cid = "<a>", Type = "image/png", Size = 3_000_000 },
                new Attachment { Cid = "<b>", Type = "image/jpeg", Size = 2_000_000 },
                new Attachment { Type = "application/pdf", Size = 9_000_000 },
            ],
        };
        Assert.Equal(5_000_000, MessageLoadRules.InlineBytes(m));
    }
}

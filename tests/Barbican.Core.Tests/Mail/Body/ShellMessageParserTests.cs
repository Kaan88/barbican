// Ported from Rust src/mailview.rs test (:312). Red until the body stream implements ShellMessageParser.
// The Slint Key characters become the same code points (Escape U+001B, Delete U+007F); modifiers are a flags enum instead of a Vec.

using Barbican.Core.Mail.Body;

namespace Barbican.Core.Tests.Mail.Body;

public sealed class ShellMessageParserTests
{
    [Fact]
    [RustTest("mailview::forwarded_keys_parse")]
    [Red("body")]
    public void Forwarded_keys_parse()
    {
        Assert.Equal(new ForwardedKey(KeyModifiers.Control, "k"), ShellMessageParser.ParseKey("c|k"));
        Assert.Equal(new ForwardedKey(KeyModifiers.Shift, "?"), ShellMessageParser.ParseKey("s|?"));
        Assert.Equal(new ForwardedKey(KeyModifiers.Control | KeyModifiers.Shift, "m"), ShellMessageParser.ParseKey("cs|m"));
        Assert.Equal(new ForwardedKey(KeyModifiers.None, "j"), ShellMessageParser.ParseKey("|j"));
        Assert.Equal("\u001b", ShellMessageParser.ParseKey("|Escape")?.Text);
        Assert.Equal("\u007f", ShellMessageParser.ParseKey("|Delete")?.Text);

        // anything else the page sends is dropped
        Assert.Null(ShellMessageParser.ParseKey("|Enter"));
        Assert.Null(ShellMessageParser.ParseKey("nokey"));
        Assert.Null(ShellMessageParser.ParseKey("|"));
    }
}

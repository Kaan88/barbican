// Rust mailview.rs forward_key :295 (tag rust-final): a key the mail view forwarded reaches the KeyRouter as the chord a real key would be. Owner body. Spec: B-READ-043.

using Barbican.Core.Mail.Body;
using Barbican.Presentation.Input;
using Barbican.Presentation.Mail.Loading;

namespace Barbican.Presentation.Tests.Mail.Loading;

public sealed class ForwardedKeyChordsTests
{
    [Theory]
    [InlineData("|j", "j", false, false)]
    [InlineData("s|G", "G", false, true)]
    [InlineData("c|k", "k", true, false)]
    [InlineData("|Escape", "Escape", false, false)]
    [InlineData("|Delete", "Delete", false, false)]
    [InlineData("|F5", "F5", false, false)]
    public void Forwarded_keys_become_chords(string spec, string key, bool control, bool shift)
    {
        var parsed = ShellMessageParser.ParseKey(spec);
        Assert.NotNull(parsed);
        Assert.Equal(new KeyChord(key, control, shift), ForwardedKeyChords.ToChord(parsed));
    }
}

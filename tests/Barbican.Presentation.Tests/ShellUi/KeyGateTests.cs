// Fable review 2 R2-05: Enter and Space belong to the focused control; nothing is routed while the sign-in screen shows.

using Barbican.App.Shell;
using Barbican.Presentation.Input;

namespace Barbican.Presentation.Tests.ShellUi;

public sealed class KeyGateTests
{
    [Theory]
    [InlineData("Enter")]
    [InlineData("Space")]
    public void EnterAndSpaceOnAButtonPressTheButton(string key) => Assert.False(KeyGate.Routes(new KeyChord(key), signInShown: false, focusActivates: true));

    [Theory]
    [InlineData("Enter")]
    [InlineData("j")]
    [InlineData("ArrowDown")]
    public void TheMessageListOrNoFocusRoutes(string key) => Assert.True(KeyGate.Routes(new KeyChord(key), signInShown: false, focusActivates: false));

    [Fact]
    public void OtherKeysStillRouteFromAButton() => Assert.True(KeyGate.Routes(new KeyChord("j"), signInShown: false, focusActivates: true));

    [Theory]
    [InlineData("/")]
    [InlineData("1", true)]
    [InlineData("g")]
    [InlineData("Enter")]
    public void NothingRoutesBehindTheSignInScreen(string key, bool control = false) =>
        Assert.False(KeyGate.Routes(new KeyChord(key, control), signInShown: true, focusActivates: false));

    // M2 hotfix 2: a held z walked back through the whole undo history (Bulwark use-undo-shortcut.ts:17 ignores e.repeat)
    [Theory]
    [InlineData("z")]
    [InlineData("Delete")]
    public void AHeldUndoOrDeleteActsOnce(string key)
    {
        Assert.False(KeyGate.RepeatRoutes(new KeyChord(key)));
        Assert.False(KeyGate.RepeatRoutes(new KeyChord(key, Control: true)));
    }

    [Theory]
    [InlineData("j")]
    [InlineData("ArrowDown")]
    public void AHeldMoveKeyRepeats(string key) => Assert.True(KeyGate.RepeatRoutes(new KeyChord(key)));
}

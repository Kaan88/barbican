using Barbican.App.Shell;
using Barbican.Presentation.Input;

namespace Barbican.Presentation.Tests.ShellUi;

/// <summary>The real KeyDown becomes the chord the KeyRouter reads (Rust shortcuts.rs: Slint key text with shift applied).</summary>
public sealed class KeyChordMapperTests
{
    [Theory]
    [InlineData(74, false, "j")]
    [InlineData(74, true, "J")]
    [InlineData(71, true, "G")]
    [InlineData(13, false, "Enter")]
    [InlineData(27, false, "Escape")]
    [InlineData(40, false, "ArrowDown")]
    [InlineData(191, false, "/")]
    [InlineData(191, true, "?")]
    [InlineData(96, false, "0")]
    public void AKeyBecomesItsChord(int virtualKey, bool shift, string key) =>
        Assert.Equal(new KeyChord(key, false, shift, false), KeyChordMapper.Map(virtualKey, false, shift, false));

    [Fact]
    public void ModifiersAreKept() => Assert.Equal(new KeyChord("k", true, false, false), KeyChordMapper.Map(75, true, false, false));

    [Fact]
    public void AKeyTheRouterNeverHandlesIsNull() => Assert.Null(KeyChordMapper.Map(112, false, false, false));
}

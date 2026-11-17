// Ported from Rust src/jmap.rs test keywords (:1857).

using Barbican.Core.Jmap.KeywordSyntax;

namespace Barbican.Core.Tests.Jmap;

public sealed class KeywordRulesTests
{
    [Fact]
    [RustTest("jmap::keywords")]
    public void Keywords()
    {
        Assert.True(KeywordRules.IsValid("$label:red"));
        Assert.True(KeywordRules.IsValid("$seen"));
        Assert.False(KeywordRules.IsValid(string.Empty));
        Assert.False(KeywordRules.IsValid("a b"));
        Assert.False(KeywordRules.IsValid("a(b"));
        Assert.False(KeywordRules.IsValid("ü"));
        Assert.False(KeywordRules.IsValid("a]"));
        Assert.Equal("keywords/a~1b~0c", KeywordRules.Path("A/b~c"));
        Assert.Equal("a%20b%2F%C3%BC", KeywordRules.PercentEncode("a b/ü"));
    }
}

using Barbican.Core.Compose;
using Barbican.Core.OrganizationPolicy;

namespace Barbican.Core.Tests.Compose;

public sealed class ComposerClassificationTests
{
    private static readonly ClassificationLabel Label = new("confidential", "Confidential", "#d97706", "", "", "Private", "[Confidential]", " Internal ");

    [Fact]
    public void Label_headers_use_jmap_raw_and_text_forms()
    {
        var headers = ComposerClassificationHeaders.Create(Label, "X-Classification");
        Assert.Equal("marking=\"Confidential\"; fgcolor=#000000; bgcolor=#d97706", headers["header:SIO-Label:asRaw"]);
        Assert.Equal("Private", headers["header:Sensitivity:asText"]);
        Assert.Equal("Internal", headers["header:X-Classification:asText"]);
        Assert.Empty(ComposerClassificationHeaders.Create(null, "X-Classification"));
    }

    [Fact]
    public void Non_ascii_marking_uses_rfc2231()
    {
        var headers = ComposerClassificationHeaders.Create(Label with { Marking = "Gizli ü" }, "");
        Assert.StartsWith("marking*=utf-8''Gizli%20%C3%BC;", headers["header:SIO-Label:asRaw"], StringComparison.Ordinal);
    }

    [Fact]
    public void Replacing_a_label_preserves_reply_chain_and_handles_international_prefixes()
    {
        var old = Label with { SubjectPrefix = "[Old]" };
        Assert.Equal("[Confidential] Re: AW[2]: subject", ComposerClassificationHeaders.ApplySubjectPrefix(
            "Re: [Old] AW[2]: subject", Label, [old, Label]));
        Assert.Equal("Re: subject", ComposerClassificationHeaders.ApplySubjectPrefix("Re: [Old] subject", null, [old]));
    }
}

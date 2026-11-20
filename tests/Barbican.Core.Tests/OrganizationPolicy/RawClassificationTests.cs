using System.Text;
using Barbican.Core.OrganizationPolicy;

namespace Barbican.Core.Tests.OrganizationPolicy;

public sealed class RawClassificationTests
{
    private static readonly ClassificationLabel Label = new("id", "Confidential", "#d97706", "", "", "private", "", "Internal");
    private static readonly ClassificationPolicy Policy = new([Label], "id", false, "X-Classification");

    [Fact]
    public void DefaultReplacesForgedHeadersAndPreservesBodyBytes()
    {
        var head = Encoding.Latin1.GetBytes("From: a@example.com\r\nSIO-Label: forged\r\n continued\r\nx-classification: forged\r\nX-Other: retained\r\n\r\n");
        byte[] body = [0, 255, 13, 10, 1];
        var result = RawClassification.Apply([.. head, .. body], Policy);
        Assert.True(result.IsOk);
        var text = Encoding.Latin1.GetString(result.Value!);
        Assert.Contains("SIO-Label: marking=\"Confidential\"; fgcolor=#000000; bgcolor=#d97706", text, StringComparison.Ordinal);
        Assert.Contains("Sensitivity: private\r\nX-Classification: Internal", text, StringComparison.Ordinal);
        Assert.DoesNotContain("forged", text, StringComparison.Ordinal);
        Assert.DoesNotContain(" continued", text, StringComparison.Ordinal);
        Assert.Equal(body, result.Value![^body.Length..]);
    }

    [Fact]
    public void RequiredDirectSendFailsEvenWhenNoLabelsAreConfigured()
    {
        var raw = Encoding.ASCII.GetBytes("From: a@example.com\r\n\r\nbody");
        Assert.False(RawClassification.Apply(raw, Policy with { Required = true }).IsOk);
        Assert.False(RawClassification.Apply(raw, Policy with { Required = true, List = [] }).IsOk);
    }

    [Fact]
    public void NoDefaultStripsReservedLabelsAndPreservesLfBody()
    {
        var raw = Encoding.ASCII.GetBytes("From: a@example.com\nX-Bulwark-Label: forged\n\tcontinued\n\nbody\n");
        var result = RawClassification.Apply(raw, Policy with { DefaultId = "" });
        Assert.True(result.IsOk);
        Assert.Equal("From: a@example.com\n\nbody\n", Encoding.ASCII.GetString(result.Value!), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("From: a@example.com\r\nX: x\n\nbody")]
    [InlineData("From: a@example.com\nX: x\r\n\r\nbody")]
    [InlineData("From: a@example.com\rX: x\n\nbody")]
    [InlineData("From: a@example.com\0x\r\n\r\nbody")]
    [InlineData(" folded\r\nFrom: a@example.com\r\n\r\nbody")]
    [InlineData("From : a@example.com\r\n\r\nbody")]
    [InlineData("From: a@example.com\r\nmalformed\r\n\r\nbody")]
    public void AmbiguousOrMalformedHeaderBlocksFail(string input)
    {
        Assert.False(RawClassification.Apply(Encoding.Latin1.GetBytes(input), Policy).IsOk);
    }

    [Fact]
    public void UnmodifiedFoldedHeadersAndOpaqueBodyArePreservedExactly()
    {
        byte[] header = [.. Encoding.ASCII.GetBytes("From: a@example.com\r\nX-Bytes: "), 0xff, 0x80,
            .. Encoding.ASCII.GetBytes("\r\n\tsecond\r\nSIO-Label: forged\r\n folded\r\nX-Last: yes\r\n")];
        byte[] body = [0, 13, 255, 10, 4];
        byte[] input = [.. header, 13, 10, .. body];
        var result = RawClassification.Apply(input, Policy with { DefaultId = "" });
        Assert.True(result.IsOk);
        byte[] expected = [.. Encoding.ASCII.GetBytes("From: a@example.com\r\nX-Bytes: "), 0xff, 0x80,
            .. Encoding.ASCII.GetBytes("\r\n\tsecond\r\nX-Last: yes\r\n\r\n"), .. body];
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void InputAndTransformedHeaderStayWithin64K()
    {
        var oversized = Encoding.ASCII.GetBytes("X: " + new string('a', 65536) + "\r\n\r\nbody");
        Assert.False(RawClassification.Apply(oversized, Policy).IsOk);
        var nearLimit = Encoding.ASCII.GetBytes("X: " + new string('a', 65490) + "\r\n\r\nbody");
        Assert.False(RawClassification.Apply(nearLimit, Policy).IsOk);
        Assert.True(RawClassification.Apply(nearLimit, Policy with { DefaultId = "" }).IsOk);
    }

    [Fact]
    public void SharedValidatorAcceptsExactly64KAndRejectsOneByteMore()
    {
        var maximum = Encoding.ASCII.GetBytes("X: " + new string('a', 65529) + "\r\n\r\nbody");
        var tooLong = Encoding.ASCII.GetBytes("X: " + new string('a', 65530) + "\r\n\r\nbody");
        Assert.True(RawClassification.HasValidHeaderBlock(maximum));
        Assert.False(RawClassification.HasValidHeaderBlock(tooLong));
        Assert.False(RawClassification.HasValidHeaderBlock(Encoding.ASCII.GetBytes("X: ok\r\nY: bad\n\nbody")));
    }

    [Fact]
    public void UnicodeMarkingUsesRfc2231AndInvalidHeadersFail()
    {
        var label = Label with { Marking = "GİZLİ" };
        var result = RawClassification.Apply(Encoding.ASCII.GetBytes("From: a@example.com\r\n\r\nbody"), Policy with { List = [label] });
        Assert.Contains("marking*=utf-8''G%C4%B0ZL%C4%B0", Encoding.ASCII.GetString(result.Value!), StringComparison.Ordinal);
        Assert.False(RawClassification.Apply(Encoding.ASCII.GetBytes("no delimiter"), Policy).IsOk);
    }

    [Fact]
    public void DisabledPolicyReturnsDetachedUnchangedBytes()
    {
        byte[] bytes = [0, 255, 7];
        var result = RawClassification.Apply(bytes, Policy with { List = [] });
        Assert.True(result.IsOk);
        Assert.Equal(bytes, result.Value);
        Assert.NotSame(bytes, result.Value);
    }
}

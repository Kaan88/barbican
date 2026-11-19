// M6 compose-side hooks: execute real reply seeding, then check that plugin transforms affect the result.
using Barbican.Core.Compose;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Compose;

public sealed class M6ComposeHookExecutionTests
{
    [Fact]
    public async Task Reply_identity_selection_and_signature_transforms_execute_and_shape_the_seed()
    {
        var rig = await PipelineRig.StartAsync();
        var selected = 0;
        var signed = 0;
        using var choose = rig.Hooks.Transform<SelectReplyIdentityPayload>(HookNames.OnSelectReplyIdentity, (payload, _) =>
        {
            Assert.Contains("i1", payload.IdentityIds, StringComparer.Ordinal);
            selected++;
            return ValueTask.FromResult(payload with { IdentityId = "i1" });
        });
        using var signature = rig.Hooks.Transform<SignatureRenderPayload>(HookNames.OnSignatureRender, (payload, _) =>
        {
            signed++;
            return ValueTask.FromResult(payload with { Html = string.Empty, Text = "Transformed synthetic signature" });
        });
        var result = await rig.Seeder.SeedAsync(new ComposeRequest(PipelineRig.Account, ComposeKind.Reply)
        { SourceEmailId = rig.Seeded(FakeSeed.ReplyToSubject) }, TestContext.Current.CancellationToken);
        Assert.True(result.IsOk, result.Error?.Message);
        Assert.Equal(1, selected);
        Assert.True(signed > 0);
        Assert.Equal("i1", result.Value!.IdentityId);
        Assert.Contains("Transformed synthetic signature", result.Value.SignatureTexts, StringComparer.Ordinal);
    }
}

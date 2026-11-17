#pragma warning disable MA0002, MA0006 // asserts compare strings ordinally by nature (as the ported Rust asserts)
using System.Text.Json.Nodes;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Compose;

/// <summary>Templates shared with Bulwark through the Inbox metadata: create, sorted list, delete with a tombstone and ifInState (three tries). J-036 area, I-255, I-256, T-285 in context. FakeJmap-based.</summary>
public sealed class TemplateStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonObject Metadata(PipelineRig rig) => rig.Store.MailboxMetadata(rig.Store.MailboxId("inbox")!);

    [Fact]
    public async Task Created_templates_are_listed_sorted_by_lower_case_name_in_bulwarks_shape()
    {
        var rig = await PipelineRig.StartAsync();
        await rig.Templates.CreateAsync(PipelineRig.Account, "beta", "S2", "Body 2", false, Ct);
        var alpha = await rig.Templates.CreateAsync(PipelineRig.Account, "Alpha", "S1", "<p>Body 1</p>", true, Ct);
        Assert.True(alpha.IsOk, alpha.Error?.Message);
        var list = (await rig.Templates.ListAsync(PipelineRig.Account, Ct)).Value!;
        Assert.Equal(["Alpha", "beta"], list.Select(t => t.Name));
        Assert.True(list[0].IsHtml);
        Assert.Equal("S1", list[0].Subject);
        var entry = Metadata(rig)["org.bulwarkmail.template." + alpha.Value!.Id]!.AsObject();
        Assert.Equal("Alpha", (string?)entry["name"]);
        Assert.Equal(true, (bool?)entry["isHTML"]);
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}Z$", (string)entry["createdAt"]!);
    }

    [Fact]
    public async Task Delete_removes_the_template_and_writes_a_tombstone()
    {
        var rig = await PipelineRig.StartAsync();
        var t = (await rig.Templates.CreateAsync(PipelineRig.Account, "Gone", "s", "b", false, Ct)).Value!;
        Assert.True((await rig.Templates.DeleteAsync(PipelineRig.Account, t.Id, Ct)).IsOk);
        Assert.Empty((await rig.Templates.ListAsync(PipelineRig.Account, Ct)).Value!);
        var meta = Metadata(rig);
        Assert.False(meta.ContainsKey("org.bulwarkmail.template." + t.Id));
        Assert.NotNull(meta["org.bulwarkmail.templates"]!["deleted"]![t.Id]);
    }

    [Fact]
    public async Task Delete_twice_with_a_state_mismatch_in_between_keeps_the_other_tombstones()
    {
        var rig = await PipelineRig.StartAsync();
        var a = (await rig.Templates.CreateAsync(PipelineRig.Account, "A", "s", "b", false, Ct)).Value!;
        var b = (await rig.Templates.CreateAsync(PipelineRig.Account, "B", "s", "b", false, Ct)).Value!;
        Assert.True((await rig.Templates.DeleteAsync(PipelineRig.Account, a.Id, Ct)).IsOk);

        // another device deletes its own template between our read and our write: the server answers stateMismatch, we read again and keep its tombstone
        var inbox = rig.Store.MailboxId("inbox")!;
        var fired = false;
        rig.Api.BeforeMailboxSet = _ =>
        {
            if (fired)
            {
                return;
            }

            fired = true;
            var tomb = (JsonObject)Metadata(rig)["org.bulwarkmail.templates"]!.DeepClone();
            tomb["deleted"]!["other-device"] = "2026-11-01T00:00:00.000Z";
            Assert.Null(rig.Store.UpdateMailboxMetadata(inbox, new JsonObject { ["metadata/org.bulwarkmail.templates"] = tomb }));
        };
        var r = await rig.Templates.DeleteAsync(PipelineRig.Account, b.Id, Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        var deleted = Metadata(rig)["org.bulwarkmail.templates"]!["deleted"]!.AsObject();
        Assert.Equal(new[] { a.Id, "other-device", b.Id }.Order(StringComparer.Ordinal), deleted.Select(kv => kv.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Delete_gives_up_after_three_state_mismatches_in_words()
    {
        var rig = await PipelineRig.StartAsync();
        var t = (await rig.Templates.CreateAsync(PipelineRig.Account, "T", "s", "b", false, Ct)).Value!;
        var inbox = rig.Store.MailboxId("inbox")!;
        var n = 0;
        rig.Api.BeforeMailboxSet = _ => rig.Store.UpdateMailboxMetadata(inbox, new JsonObject { ["metadata/org.bulwarkmail.noise"] = JsonValue.Create(n++) });
        var r = await rig.Templates.DeleteAsync(PipelineRig.Account, t.Id, Ct);
        Assert.False(r.IsOk);
        Assert.Equal("The templates kept changing on another device; try again.", r.Error!.Message);
        Assert.Equal(3, n);
    }

    [Fact]
    public async Task A_server_without_metadata_has_no_templates_and_cannot_keep_one()
    {
        var rig = await PipelineRig.StartAsync(PipelineRig.M3Options() with { MetadataCapability = false });
        Assert.Empty((await rig.Templates.ListAsync(PipelineRig.Account, Ct)).Value!);
        Assert.False((await rig.Templates.CreateAsync(PipelineRig.Account, "x", "s", "b", false, Ct)).IsOk);
        _ = FakeSeed.IdentitiesM3;
    }
}

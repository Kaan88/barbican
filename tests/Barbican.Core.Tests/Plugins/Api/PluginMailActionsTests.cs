// Source: Bulwark lib/plugin-sandbox/host-api.ts:235-237,334-354 (permissions), host-api-mail.ts:209-302 (getAttachment, getRaw, move, archive: 100 ids, 20 moves a minute), host-api.ts:1421-1535 (keyword checks, updateKeywords),
// assistant/b.ts:59-98 (getAttachmentText), assistant/c.batch.ts (email.batch / undoBatch), lib/__tests__/plugin-host-*.test.ts; Barbican fences (stale account, revoked add-on, no server reach on denial) over FakeJmap.
using System.Text;
using Barbican.Core.Plugins.Api.MailActions;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class PluginMailActionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Msg(string id) => "\"" + id + "\"";

    [Theory]
    [InlineData("email.move", """[["x"],"y"]""", "email:read")]
    [InlineData("email.archive", """[["x"]]""", "email:read")]
    [InlineData("email.setKeyword", """["x","tag"]""", "email:read")]
    [InlineData("email.removeKeyword", """["x","tag"]""", "email:read")]
    [InlineData("email.updateKeywords", """["x",{"tag":true}]""", "email:read")]
    [InlineData("email.batch", """[{"emailIds":["x"],"action":"read"}]""", "email:read")]
    [InlineData("email.undoBatch", """["t"]""", "email:read")]
    [InlineData("drafts.create", """[{"subject":"s"}]""", "compose:open")]
    [InlineData("compose.open", """[{"subject":"s"}]""", "email:write")]
    [InlineData("compose.reply", """["x",{"text":"t"}]""", "email:write")]
    [InlineData("email.getAttachment", """["x","b"]""", "email:read")]
    [InlineData("email.getAttachmentText", """["x","b"]""", "email:read")]
    [InlineData("email.getRaw", """["x"]""", "email:read")]
    public async Task Every_method_is_denied_with_a_neighbouring_permission_and_reaches_no_server(string method, string args, string held)
    {
        var rig = await PluginMailActionsRig.StartAsync([held]);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync(method, args)).Error);
        Assert.Empty(rig.Api.F.Server.ApiCalls);
        Assert.Empty(rig.DraftService.Saved);
        Assert.Empty(rig.Compose.Opened);
        Assert.Empty(rig.Compose.Replies);
    }

    [Fact]
    public async Task Move_replaces_the_folders_with_the_target_and_runs_the_move_hooks()
    {
        var rig = await PluginMailActionsRig.StartAsync(["email:write"]);
        var id = rig.IdOf("Flagged reminder");
        var seen = new List<string>();
        using var before = rig.Hooks.Intercept<Barbican.Core.Mail.Write.Payloads.EmailMovePayload>("onBeforeEmailMove", (p, _) => { seen.Add("before:" + p.ToMailboxId); return ValueTask.FromResult(true); });
        using var after = rig.Hooks.Observe<Barbican.Core.Mail.Write.Payloads.EmailMovePayload>("onAfterEmailMove", (p, _) => { seen.Add("after:" + p.ToMailboxId); return ValueTask.CompletedTask; });
        var target = rig.Folder("trash");
        var result = await rig.CallAsync("email.move", $$"""[[{{Msg(id)}},{{Msg(id)}}],{{Msg(target)}}]""");
        Assert.True(result.IsSuccess);
        Assert.Equal(PluginValueKind.Undefined, result.Value!.Kind);
        Assert.Equal([target], rig.FoldersOf(id));
        Assert.Equal([$"before:{target}", $"after:{target}"], seen);
    }

    [Fact]
    public async Task Move_that_an_intercept_stops_changes_nothing()
    {
        var rig = await PluginMailActionsRig.StartAsync(["email:write"]);
        var id = rig.IdOf("Flagged reminder");
        var folders = rig.FoldersOf(id);
        using var stop = rig.Hooks.Intercept<Barbican.Core.Mail.Write.Payloads.EmailMovePayload>("onBeforeEmailMove", (_, _) => ValueTask.FromResult(false));
        Assert.Equal(PluginErrorCode.Failed, (await rig.CallAsync("email.move", $$"""[[{{Msg(id)}}],{{Msg(rig.Folder("archive"))}}]""")).Error);
        Assert.Equal(folders, rig.FoldersOf(id));
    }

    [Fact]
    public async Task Move_checks_its_arguments_the_folder_and_the_budget()
    {
        var rig = await PluginMailActionsRig.StartAsync(["email:write"]);
        var id = rig.IdOf("Flagged reminder");
        var archive = Msg(rig.Folder("archive"));
        var tooMany = "[" + string.Join(",", Enumerable.Range(0, 101).Select(i => Msg("m" + i))) + "]";
        foreach (var bad in new[] { $"[[],{archive}]", $"[{tooMany},{archive}]", $"[[{Msg(id)}]]", $"[\"{id}\",{archive}]", $"[[1],{archive}]", $"[[{Msg(id)}],\"\"]" })
        {
            Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("email.move", bad)).Error);
        }

        Assert.Empty(rig.Api.F.Server.ApiCalls);
        var writes = rig.Api.F.Server.ApiCalls.Count;
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("email.move", $$"""[[{{Msg(id)}}],"no-such-folder"]""")).Error);
        Assert.DoesNotContain(rig.Api.F.Server.ApiCalls.Skip(writes), c => c.Contains("Email/set", StringComparison.Ordinal));
        // 20 moves a minute, shared by move and archive
        for (var i = 0; i < 20; i++) Assert.True((await rig.CallAsync("email.move", $$"""[[{{Msg(id)}}],{{archive}}]""")).IsSuccess);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("email.move", $$"""[[{{Msg(id)}}],{{archive}}]""")).Error);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("email.archive", $$"""[[{{Msg(id)}}]]""")).Error);
        rig.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await rig.CallAsync("email.archive", $$"""[[{{Msg(id)}}]]""")).IsSuccess);
    }

    [Fact]
    public async Task Archive_files_into_the_archive_folder()
    {
        var rig = await PluginMailActionsRig.StartAsync(["email:write"]);
        var id = rig.IdOf("Flagged reminder");
        Assert.True((await rig.CallAsync("email.archive", $$"""[[{{Msg(id)}}]]""")).IsSuccess);
        Assert.Equal([rig.Folder("archive")], rig.FoldersOf(id));
    }

    [Fact]
    public async Task Keywords_are_set_removed_and_updated_in_one_write()
    {
        var rig = await PluginMailActionsRig.StartAsync(["email:write"]);
        var id = rig.IdOf("Flagged reminder");
        Assert.True((await rig.CallAsync("email.setKeyword", $$"""[{{Msg(id)}},"$label:red"]""")).IsSuccess);
        Assert.True(rig.HasKeyword(id, "$label:red"));
        Assert.True((await rig.CallAsync("email.removeKeyword", $$"""[{{Msg(id)}},"$label:red"]""")).IsSuccess);
        Assert.False(rig.HasKeyword(id, "$label:red"));
        Assert.True((await rig.CallAsync("email.setKeyword", $$"""[{{Msg(id)}},"owner-a"]""")).IsSuccess);
        var calls = rig.Api.F.Server.ApiCalls.Count;
        var swap = await rig.CallAsync("email.updateKeywords", $$"""[{{Msg(id)}},{"owner-a":false,"owner-b":true}]""");
        Assert.True(swap.Value!.BooleanValue);
        Assert.Equal(calls + 1, rig.Api.F.Server.ApiCalls.Count);
        Assert.False(rig.HasKeyword(id, "owner-a"));
        Assert.True(rig.HasKeyword(id, "owner-b"));
        // nothing to change: true, no server
        var quiet = rig.Api.F.Server.ApiCalls.Count;
        Assert.True((await rig.CallAsync("email.updateKeywords", $$"""[{{Msg(id)}},{}]""")).Value!.BooleanValue);
        Assert.Equal(quiet, rig.Api.F.Server.ApiCalls.Count);
    }

    [Theory]
    [InlineData("""["x","$junk"]""")]
    [InlineData("""["x","$DRAFT"]""")]
    [InlineData("""["x","$phishing"]""")]
    [InlineData("""["x","$hasattachment"]""")]
    [InlineData("""["x","has space"]""")]
    [InlineData("""["x",""]""")]
    [InlineData("""["x",5]""")]
    public async Task Keyword_methods_refuse_protected_server_set_and_malformed_keywords(string args)
    {
        var rig = await PluginMailActionsRig.StartAsync(["email:write"]);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("email.setKeyword", args)).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("email.removeKeyword", args)).Error);
        Assert.Empty(rig.Api.F.Server.ApiCalls);
    }

    [Fact]
    public async Task UpdateKeywords_checks_every_keyword_the_count_and_the_account()
    {
        var rig = await PluginMailActionsRig.StartAsync(["email:write"]);
        var many = "{" + string.Join(",", Enumerable.Range(0, 21).Select(i => $"\"k{i}\":true")) + "}";
        foreach (var bad in new[] { $"[\"x\",{many}]", """["x",{"$junk":true}]""", """["x",{"ok":"yes"}]""", """["x",["ok"]]""", """["x",{"ok":true},""]""" })
        {
            Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("email.updateKeywords", bad)).Error);
        }

        // an account this login does not show is not reached
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("email.updateKeywords", """["x",{"ok":true},"stranger"]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("email.setKeyword", """["x","ok","stranger"]""")).Error);
        Assert.Empty(rig.Api.F.Server.ApiCalls);
    }

    [Fact]
    public async Task A_message_the_server_does_not_have_is_a_failure_not_a_success()
    {
        var rig = await PluginMailActionsRig.StartAsync(["email:write"]);
        Assert.Equal(PluginErrorCode.Failed, (await rig.CallAsync("email.setKeyword", """["no-such-message","ok"]""")).Error);
        Assert.Equal(PluginErrorCode.Failed, (await rig.CallAsync("email.move", $$"""[["no-such-message"],{{Msg(rig.Folder("archive"))}}]""")).Error);
    }

    [Fact]
    public async Task Batch_marks_read_and_the_undo_token_takes_it_back_once()
    {
        var rig = await PluginMailActionsRig.StartAsync(["email:write"]);
        var id = rig.IdOf("Report with attachments");
        Assert.False(rig.HasKeyword(id, "$seen"));
        var result = (await rig.CallAsync("email.batch", $$"""[{"emailIds":[{{Msg(id)}},"ghost"],"action":"read"}]""")).Value!;
        Assert.Equal(["done", "failed", "undoToken"], PluginApiRig.Keys(result), StringComparer.Ordinal);
        Assert.Equal("1", result.Property("done").NumberText, StringComparer.Ordinal);
        Assert.Equal("1", result.Property("failed").NumberText, StringComparer.Ordinal);
        Assert.True(rig.HasKeyword(id, "$seen"));
        var token = result.Property("undoToken").StringValue;
        Assert.Single(rig.Undo.Entries);
        Assert.True((await rig.CallAsync("email.undoBatch", $$"""[{{Msg(token)}}]""")).IsSuccess);
        Assert.False(rig.HasKeyword(id, "$seen"));
        Assert.Empty(rig.Undo.Entries);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("email.undoBatch", $$"""[{{Msg(token)}}]""")).Error);
    }

    [Fact]
    public async Task Batch_move_and_archive_restore_the_folders_on_undo_and_the_history_entry_shares_the_undo()
    {
        var rig = await PluginMailActionsRig.StartAsync(["email:write"]);
        var id = rig.IdOf("Flagged reminder");
        var folders = rig.FoldersOf(id);
        var archived = (await rig.CallAsync("email.batch", $$"""[{"emailIds":[{{Msg(id)}}],"action":"archive"}]""")).Value!;
        Assert.Equal([rig.Folder("archive")], rig.FoldersOf(id));
        // the user's own Undo runs the same action; the add-on's undo afterwards finds it done and spends the token
        Assert.True((await rig.Undo.Entries[0].Undo(Ct)).IsOk);
        Assert.Equal(folders, rig.FoldersOf(id));
        Assert.True((await rig.CallAsync("email.undoBatch", $$"""[{{Msg(archived.Property("undoToken").StringValue)}}]""")).IsSuccess);
        Assert.Equal(folders, rig.FoldersOf(id));
        var moved = (await rig.CallAsync("email.batch", $$"""[{"emailIds":[{{Msg(id)}}],"action":"move","mailboxId":{{Msg(rig.Folder("junk"))}}}]""")).Value!;
        Assert.Equal([rig.Folder("junk")], rig.FoldersOf(id));
        Assert.True((await rig.CallAsync("email.undoBatch", $$"""[{{Msg(moved.Property("undoToken").StringValue)}}]""")).IsSuccess);
        Assert.Equal(folders, rig.FoldersOf(id));
    }

    [Fact]
    public async Task Batch_tags_only_with_the_users_tags_and_untag_removes_the_keyword()
    {
        var rig = await PluginMailActionsRig.StartAsync(["email:write"]);
        var id = rig.IdOf("Flagged reminder");
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("email.batch", $$"""[{"emailIds":[{{Msg(id)}}],"action":"tag","tagId":"not-a-tag"}]""")).Error);
        var tagged = (await rig.CallAsync("email.batch", $$"""[{"emailIds":[{{Msg(id)}}],"action":"tag","tagId":" red "}]""")).Value!;
        Assert.True(rig.HasKeyword(id, "$label:red"));
        Assert.True((await rig.CallAsync("email.undoBatch", $$"""[{{Msg(tagged.Property("undoToken").StringValue)}}]""")).IsSuccess);
        Assert.False(rig.HasKeyword(id, "$label:red"));
        _ = await rig.CallAsync("email.batch", $$"""[{"emailIds":[{{Msg(id)}}],"action":"tag","tagId":"red"}]""");
        var untagged = (await rig.CallAsync("email.batch", $$"""[{"emailIds":[{{Msg(id)}}],"action":"untag","tagId":"red"}]""")).Value!;
        Assert.False(rig.HasKeyword(id, "$label:red"));
        Assert.True((await rig.CallAsync("email.undoBatch", $$"""[{{Msg(untagged.Property("undoToken").StringValue)}}]""")).IsSuccess);
        Assert.True(rig.HasKeyword(id, "$label:red"));
    }

    [Fact]
    public async Task Batch_refuses_what_bulwark_refuses_before_it_reads_or_writes_anything()
    {
        var rig = await PluginMailActionsRig.StartAsync(["email:write"]);
        var id = Msg(rig.IdOf("Flagged reminder"));
        var many = "[" + string.Join(",", Enumerable.Range(0, 201).Select(i => Msg("m" + i))) + "]";
        foreach (var bad in new[]
        {
            "[]", "[{}]", $"[{{\"emailIds\":[],\"action\":\"read\"}}]", $"[{{\"emailIds\":{many},\"action\":\"read\"}}]", $"[{{\"emailIds\":[{id}],\"action\":\"delete\"}}]",
            $"[{{\"emailIds\":[{id}],\"action\":\"read\",\"mailboxId\":\"x\"}}]", $"[{{\"emailIds\":[{id}],\"action\":\"read\",\"tagId\":\"red\"}}]", $"[{{\"emailIds\":[{id}],\"action\":\"move\"}}]",
            $"[{{\"emailIds\":[{id}],\"action\":\"tag\"}}]", $"[{{\"emailIds\":[{id}],\"action\":\"tag\",\"tagId\":\"  \"}}]", $"[{{\"emailIds\":[{id}],\"action\":\"read\",\"extra\":1}}]",
        })
        {
            Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("email.batch", bad)).Error);
        }

        Assert.Empty(rig.Api.F.Server.ApiCalls);
        // the trash and an unknown folder are not targets
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("email.batch", $$"""[{"emailIds":[{{id}}],"action":"move","mailboxId":{{Msg(rig.Folder("trash"))}}}]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("email.batch", $$"""[{"emailIds":[{{id}}],"action":"move","mailboxId":"ghost"}]""")).Error);
        Assert.DoesNotContain(rig.Api.F.Server.ApiCalls, c => c.Contains("Email/set", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Batch_of_messages_the_server_does_not_have_answers_none_done()
    {
        var rig = await PluginMailActionsRig.StartAsync(["email:write"]);
        var result = (await rig.CallAsync("email.batch", """[{"emailIds":["g1","g2"],"action":"read"}]""")).Value!;
        Assert.Equal("0", result.Property("done").NumberText, StringComparer.Ordinal);
        Assert.Equal("2", result.Property("failed").NumberText, StringComparer.Ordinal);
        Assert.Equal(PluginValueKind.Null, result.Property("undoToken").Kind);
        Assert.Empty(rig.Undo.Entries);
    }

    [Fact]
    public async Task An_undo_token_belongs_to_the_add_on_that_got_it()
    {
        var rig = await PluginMailActionsRig.StartAsync(["email:write"]);
        var id = rig.IdOf("Report with attachments");
        var token = (await rig.CallAsync("email.batch", $$"""[{"emailIds":[{{Msg(id)}}],"action":"read"}]""")).Value!.Property("undoToken").StringValue;
        var other = rig.Api.BindOther("other", ["email:write"]);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.Api.CallAsync(other, "email.undoBatch", $$"""[{{Msg(token)}}]""")).Error);
        Assert.True(rig.HasKeyword(id, "$seen"));
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("email.undoBatch", """["forged"]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("email.undoBatch", "[]")).Error);
    }

    [Fact]
    public async Task A_session_that_goes_stale_before_the_write_changes_nothing_and_during_it_is_an_unknown_outcome()
    {
        var before = await PluginMailActionsRig.StartAsync(["email:write"]);
        var id = before.IdOf("Flagged reminder");
        var folders = before.FoldersOf(id);
        before.Api.OnMailCall = () => before.Api.Active.Valid = false;
        Assert.Equal(PluginErrorCode.StaleContext, (await before.CallAsync("email.move", $$"""[[{{Msg(id)}}],{{Msg(before.Folder("archive"))}}]""")).Error);
        Assert.Equal(folders, before.FoldersOf(id));

        var during = await PluginMailActionsRig.StartAsync(["email:write"]);
        var other = during.IdOf("Flagged reminder");
        during.OnWrite = () => during.Api.Active.Valid = false;
        Assert.Equal(PluginErrorCode.UnknownOutcome, (await during.CallAsync("email.setKeyword", $$"""[{{Msg(other)}},"ok"]""")).Error);
        Assert.True(during.HasKeyword(other, "ok"));
    }

    [Fact]
    public async Task A_revoked_add_on_writes_nothing()
    {
        var rig = await PluginMailActionsRig.StartAsync(["email:write"]);
        var id = rig.IdOf("Flagged reminder");
        rig.Api.OnMailCall = () => rig.Api.Auth.Current = PluginApiRig.Grant(["email:write"], enabled: false);
        Assert.Equal(PluginErrorCode.StaleContext, (await rig.CallAsync("email.archive", $$"""[[{{Msg(id)}}]]""")).Error);
        Assert.NotEqual([rig.Folder("archive")], rig.FoldersOf(id));
        var again = await PluginMailActionsRig.StartAsync(["email:write"]);
        again.OnWrite = () => again.Api.Auth.Current = PluginApiRig.Grant(["email:write"], enabled: false);
        Assert.Equal(PluginErrorCode.StaleContext, (await again.CallAsync("email.batch", $$"""[{"emailIds":[{{Msg(again.IdOf("Flagged reminder"))}}],"action":"read"}]""")).Error);
    }

    [Fact]
    public async Task GetAttachment_returns_the_named_attachment_bytes_and_nothing_else()
    {
        var rig = await PluginMailActionsRig.StartAsync(["email:blob-read"]);
        var bytes = Encoding.UTF8.GetBytes("plain attachment text");
        var id = rig.Api.F.Server.Store.Deliver(new FakeMail { Subject = "With text file", Text = "see file", Attachments = [new FakeAttachment("note.txt", "text/plain", bytes)] });
        var blob = (string)rig.Api.F.Server.Store.Email(id)!["attachments"]![0]!["blobId"]!;
        var data = (await rig.CallAsync("email.getAttachment", $$"""[{{Msg(id)}},{{Msg(blob)}}]""")).Value!;
        Assert.Equal(["data", "name", "size", "type"], PluginApiRig.Keys(data), StringComparer.Ordinal);
        Assert.Equal("note.txt", data.Property("name").StringValue, StringComparer.Ordinal);
        Assert.Equal("text/plain", data.Property("type").StringValue, StringComparer.Ordinal);
        Assert.Equal(bytes, data.Property("data").BinaryBytes());
        // a blob that is not an attachment of this message, an unknown message and a foreign account are Unavailable
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("email.getAttachment", $$"""[{{Msg(rig.IdOf("Flagged reminder"))}},{{Msg(blob)}}]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("email.getAttachment", $$"""["ghost",{{Msg(blob)}}]""")).Error);
        var calls = rig.Api.F.Server.ApiCalls.Count;
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("email.getAttachment", $$"""[{{Msg(id)}},{{Msg(blob)}},"stranger"]""")).Error);
        Assert.Equal(calls, rig.Api.F.Server.ApiCalls.Count);
    }

    [Fact]
    public async Task GetAttachmentText_reads_text_cuts_at_maxChars_and_is_limited_to_thirty_a_minute()
    {
        var rig = await PluginMailActionsRig.StartAsync(["email:blob-read"]);
        var id = rig.Api.F.Server.Store.Deliver(new FakeMail
        {
            Subject = "Texts",
            Attachments = [new FakeAttachment("long.txt", "text/plain", Encoding.UTF8.GetBytes(new string('a', 500))), new FakeAttachment("scan.pdf", "application/pdf", [1, 2, 3])],
        });
        var parts = rig.Api.F.Server.Store.Email(id)!["attachments"]!.AsArray();
        string Blob(int i) => (string)parts[i]!["blobId"]!;
        var cut = (await rig.CallAsync("email.getAttachmentText", $$"""[{{Msg(id)}},{{Msg(Blob(0))}},{"maxChars":100}]""")).Value!;
        Assert.Equal(["format", "name", "size", "status", "text", "truncated", "type"], PluginApiRig.Keys(cut), StringComparer.Ordinal);
        Assert.Equal("ok", cut.Property("status").StringValue, StringComparer.Ordinal);
        Assert.Equal(100, cut.Property("text").StringValue.Length);
        Assert.True(cut.Property("truncated").BooleanValue);
        var pdf = (await rig.CallAsync("email.getAttachmentText", $$"""[{{Msg(id)}},{{Msg(Blob(1))}}]""")).Value!;
        Assert.Equal("unsupported", pdf.Property("status").StringValue, StringComparer.Ordinal);
        Assert.Equal("pdf", pdf.Property("format").StringValue, StringComparer.Ordinal);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("email.getAttachmentText", $$"""[{{Msg(id)}},{{Msg(Blob(0))}},{"maxChars":0}]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("email.getAttachmentText", $$"""[{{Msg(id)}},{{Msg(Blob(0))}},{"bogus":1}]""")).Error);
        for (var i = 0; i < 28; i++) Assert.True((await rig.CallAsync("email.getAttachmentText", $$"""[{{Msg(id)}},{{Msg(Blob(0))}}]""")).IsSuccess);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("email.getAttachmentText", $$"""[{{Msg(id)}},{{Msg(Blob(0))}}]""")).Error);
    }

    [Fact]
    public async Task GetRaw_returns_the_message_source_as_bytes()
    {
        var rig = await PluginMailActionsRig.StartAsync(["email:blob-read"]);
        var id = rig.Api.F.Server.Store.Deliver(new FakeMail { Subject = "Raw one", Text = "the stored source" });
        var raw = (await rig.CallAsync("email.getRaw", $$"""[{{Msg(id)}}]""")).Value!;
        Assert.Equal(PluginValueKind.ArrayBuffer, raw.Kind);
        Assert.Equal("the stored source", Encoding.UTF8.GetString(raw.BinaryBytes()));
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("email.getRaw", """["ghost"]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("email.getRaw", $$"""[{{Msg(id)}},"stranger"]""")).Error);
    }

    [Fact]
    public async Task Drafts_create_saves_a_sanitised_draft_and_never_sends()
    {
        var rig = await PluginMailActionsRig.StartAsync(["email:write"]);
        var id = (await rig.CallAsync("drafts.create", """[{"to":["Ann <ann@example.org>","bob@example.org"],"cc":["cy@example.org"],"subject":"Hello","body":"<p>Hi <b>there</b></p><script>alert(1)</script>","isHtml":true}]""")).Value!;
        Assert.Equal("draft-1", id.StringValue, StringComparer.Ordinal);
        var saved = Assert.Single(rig.DraftService.Saved);
        Assert.Equal("a1", saved.AccountId, StringComparer.Ordinal);
        Assert.Equal("Ann <ann@example.org>, bob@example.org", saved.To, StringComparer.Ordinal);
        Assert.Equal("cy@example.org", saved.Cc, StringComparer.Ordinal);
        Assert.Equal("Hello", saved.Subject, StringComparer.Ordinal);
        Assert.False(saved.PlainText);
        var text = new Barbican.Core.Compose.DocHtmlCodec().ToText(saved.Body);
        Assert.Contains("Hi there", text, StringComparison.Ordinal);
        Assert.DoesNotContain("alert", text, StringComparison.Ordinal);
        // plain text stays plain; attachments are references only
        Assert.True((await rig.CallAsync("drafts.create", """[{"body":"line1\nline2","attachments":[{"blobId":"B1","name":"a.pdf","type":"application/pdf","size":10}]}]""")).IsSuccess);
        Assert.True(rig.DraftService.Saved[1].PlainText);
        Assert.Equal("B1", Assert.Single(rig.DraftService.Saved[1].Attachments).BlobId, StringComparer.Ordinal);
        Assert.DoesNotContain(rig.Api.F.Server.ApiCalls, c => c.Contains("EmailSubmission", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Compose_inputs_are_checked_and_drafts_are_limited_to_ten_a_minute()
    {
        var rig = await PluginMailActionsRig.StartAsync(["email:write", "compose:open"]);
        var many = "[" + string.Join(",", Enumerable.Range(0, 101).Select(i => $"\"u{i}@example.org\"")) + "]";
        foreach (var bad in new[]
        {
            """[{"to":"ann@example.org"}]""", """[{"to":["not an address"]}]""", """[{"to":["ann@example.org\r\nBcc: cy@example.org"]}]""", $"[{{\"to\":{many}}}]", """[{"subject":"a\nb"}]""", """[{"body":5}]""", """[{"isHtml":"yes"}]""",
            """[{"unknown":1}]""", """[{"attachments":[{"blobId":"B","name":"a","type":"nonsense","size":1}]}]""", """[{"attachments":[{"blobId":"B","name":"a","type":"text/plain","size":-1}]}]""", "[5]",
        })
        {
            Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("drafts.create", bad)).Error);
            Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("compose.open", bad)).Error);
        }

        Assert.Empty(rig.DraftService.Saved);
        Assert.Empty(rig.Compose.Opened);
        for (var i = 0; i < 10; i++) Assert.True((await rig.CallAsync("drafts.create", """[{"subject":"s"}]""")).IsSuccess);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("drafts.create", """[{"subject":"s"}]""")).Error);
        Assert.Equal(10, rig.DraftService.Saved.Count);
    }

    [Fact]
    public async Task Compose_open_fills_the_composer_for_the_user_and_is_limited_to_five_a_minute_with_reply()
    {
        var rig = await PluginMailActionsRig.StartAsync(["compose:open"]);
        var opened = await rig.CallAsync("compose.open", """[{"to":["ann@example.org"],"subject":"Hi","body":"<b>bold</b><img src='https://tracker.example/p.gif'>","isHtml":true}]""");
        Assert.Equal("opened", opened.Value!.StringValue, StringComparer.Ordinal);
        var message = Assert.Single(rig.Compose.Opened);
        Assert.Equal(["ann@example.org"], message.To);
        Assert.Equal("Hi", message.Subject, StringComparer.Ordinal);
        Assert.DoesNotContain("tracker.example", message.Html, StringComparison.Ordinal);
        Assert.Contains("bold", message.Text, StringComparison.Ordinal);
        rig.Compose.Result = PluginComposeOpenResult.Navigating;
        Assert.Equal("navigating", (await rig.CallAsync("compose.open", "[]")).Value!.StringValue, StringComparer.Ordinal);
        Assert.True((await rig.CallAsync("compose.reply", """["m1",{"text":"Thanks","all":true}]""")).Value!.BooleanValue);
        Assert.Equal(("m1", "Thanks", true), rig.Compose.Replies[0]);
        Assert.True((await rig.CallAsync("compose.reply", """["m1"]""")).IsSuccess);
        Assert.True((await rig.CallAsync("compose.open", "[]")).IsSuccess);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("compose.open", "[]")).Error);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("compose.reply", """["m1"]""")).Error);
        Assert.Empty(rig.Api.F.Server.ApiCalls);
    }

    [Fact]
    public async Task Compose_open_the_user_kept_the_draft_and_reply_for_a_message_that_is_not_open_are_refusals()
    {
        var rig = await PluginMailActionsRig.StartAsync(["compose:open"]);
        rig.Compose.Result = PluginComposeOpenResult.Refused;
        Assert.Equal(PluginErrorCode.Failed, (await rig.CallAsync("compose.open", "[]")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("compose.reply", """["m1"]""")).Error);
        foreach (var bad in new[] { "[]", """["m1",{"text":5}]""", """["m1",{"all":"yes"}]""", """["m1",{"bogus":1}]""", """["",{}]""" })
        {
            Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("compose.reply", bad)).Error);
        }
    }

    [Fact]
    public async Task Compose_open_for_a_session_gone_stale_opens_nothing()
    {
        var rig = await PluginMailActionsRig.StartAsync(["compose:open"]);
        rig.Api.Active.Valid = false;
        Assert.NotNull((await rig.CallAsync("compose.open", "[]")).Error);
        Assert.Empty(rig.Compose.Opened);
        // gone stale while the composer opens: the composer is open, so the add-on is told the outcome is unknown, not that nothing happened
        var during = await PluginMailActionsRig.StartAsync(["compose:open"]);
        during.Compose.OnOpen = () => during.Api.Active.Valid = false;
        Assert.Equal(PluginErrorCode.UnknownOutcome, (await during.CallAsync("compose.open", "[]")).Error);
        Assert.Single(during.Compose.Opened);
    }
}

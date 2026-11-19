// Fake-only coverage for Bulwark message-list-settings.tsx:82-145 archive reorganization.
#pragma warning disable MA0002 // xUnit string assertions compare ordinally
using System.Text.Json.Nodes;
using Barbican.Core.Jmap;
using Barbican.Core.Mail.Archive;
using Barbican.Core.Mail.Write;
using Barbican.Core.Mail.Write.Payloads;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Tests.Contacts;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Mail;

public sealed class ArchiveReorganizerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static DateTimeOffset Date(int year, int month) => new(year, month, 15, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("year")]
    [InlineData("month")]
    public async Task All_root_pages_move_to_existing_and_new_dated_folders(string mode)
    {
        var rig = ContactRig.Create();
        var (key, server, _) = await rig.AddAsync("archive@example.org", null, new FakeJmapOptions { QueryLimitCap = 2, MaxObjectsInSet = 2 });
        using (server)
        {
            var root = server.Store.MailboxId("archive")!;
            var existing = server.Store.AddMailbox("2024", null, root);
            var a = server.Store.Deliver(new FakeMail { Subject = "a", ReceivedAt = Date(2024, 3) }, root);
            var b = server.Store.Deliver(new FakeMail { Subject = "b", ReceivedAt = Date(2024, 4) }, root);
            var c = server.Store.Deliver(new FakeMail { Subject = "c", ReceivedAt = Date(2025, 1) }, root);
            var hooks = new HookBus();
            var service = new ArchiveReorganizer(rig, new MailWriterFactory(), hooks, TimeProvider.System);
            var result = await service.ReorganizeAsync(key, mode, Ct);
            Assert.True(result.IsOk, result.Error?.Message);
            Assert.Equal(4, result.Value!.Moved); // includes one seeded archive message
            Assert.Equal(0, result.Value.Failed);
            Assert.True(server.ApiCalls.Count(call => call.Contains("Email/query", StringComparison.Ordinal)) >= 2);
            var boxes = server.Store.Mailboxes();
            Assert.Single(boxes, box => string.Equals((string?)box["name"], "2024", StringComparison.Ordinal) && string.Equals((string?)box["parentId"], root, StringComparison.Ordinal));
            var aBox = (string)server.Store.Email(a)!["mailboxIds"]!.AsObject().First().Key;
            var bBox = (string)server.Store.Email(b)!["mailboxIds"]!.AsObject().First().Key;
            var cBox = (string)server.Store.Email(c)!["mailboxIds"]!.AsObject().First().Key;
            if (string.Equals(mode, "year", StringComparison.Ordinal))
            {
                Assert.Equal(existing, aBox);
                Assert.Equal(existing, bBox);
            }
            else
            {
                Assert.NotEqual(aBox, bBox);
                Assert.Equal(existing, (string?)boxes.Single(box => string.Equals((string?)box["id"], aBox, StringComparison.Ordinal))["parentId"]);
            }

            Assert.NotEqual(root, cBox);
        }
    }

    [Fact]
    public async Task Reorganization_preserves_other_mailbox_memberships()
    {
        var rig = ContactRig.Create();
        var (key, server, client) = await rig.AddAsync("labels@example.org", null);
        using (server)
        {
            var root = server.Store.MailboxId("archive")!;
            var other = server.Store.AddMailbox("Important project", null);
            var id = server.Store.Deliver(new FakeMail { Subject = "labeled", ReceivedAt = Date(2024, 3) }, root);
            var writer = new MailWriter(client, client.Session.MailAccountId);
            var assigned = await writer.SetMailboxIdsAsync(new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.Ordinal)
            {
                [id] = new Dictionary<string, bool>(StringComparer.Ordinal) { [root] = true, [other] = true },
            }, Ct);
            Assert.True(assigned.IsOk, assigned.Error?.Message);
            var result = await new ArchiveReorganizer(rig, new MailWriterFactory(), new HookBus(), TimeProvider.System).ReorganizeAsync(key, "year", Ct);
            Assert.True(result.IsOk, result.Error?.Message);
            var memberships = server.Store.Email(id)!["mailboxIds"]!.AsObject();
            Assert.True(memberships.ContainsKey(other));
            Assert.False(memberships.ContainsKey(root));
            Assert.Equal(2, memberships.Count);
        }
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.25")]
    public async Task Malformed_query_total_stops_before_any_write(string rawTotal)
    {
        var client = new ContactScriptedClient(request => new JmapResponse
        {
            MethodResponses = [.. request.MethodCalls.Select(call => new JsonArray(
                call[0]!.DeepClone(),
                string.Equals((string?)call[0], "Mailbox/get", StringComparison.Ordinal)
                    ? new JsonObject { ["list"] = new JsonArray(new JsonObject { ["id"] = "archive", ["name"] = "Archive", ["role"] = "archive" }) }
                    : new JsonObject { ["queryState"] = "q", ["total"] = JsonNode.Parse(rawTotal), ["ids"] = new JsonArray() },
                call[2]!.DeepClone()))],
        }, [JmapCapabilities.Mail]);
        var result = await new ArchiveReorganizer(client, new MailWriterFactory(), new HookBus(), TimeProvider.System).ReorganizeAsync(ContactScriptedClient.Key, "year", Ct);
        Assert.False(result.IsOk);
        Assert.DoesNotContain(client.Requests.SelectMany(request => request.MethodCalls), call => ((string?)call[0])?.EndsWith("/set", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Read_only_and_single_mode_never_send_writes()
    {
        var rig = ContactRig.Create();
        var (key, server, _) = await rig.AddAsync("read@example.org", null, readOnly: true);
        using (server)
        {
            var service = new ArchiveReorganizer(rig, new MailWriterFactory(), new HookBus(), TimeProvider.System);
            var start = server.ApiCalls.Count;
            Assert.True((await service.ReorganizeAsync(key, "single", Ct)).IsOk);
            Assert.False((await service.ReorganizeAsync(key, "year", Ct)).IsOk);
            Assert.DoesNotContain(server.ApiCalls.Skip(start), call => call.Contains("/set", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Plugin_veto_leaves_the_group_in_archive(bool folderVeto)
    {
        var rig = ContactRig.Create();
        var (key, server, _) = await rig.AddAsync("veto@example.org", null);
        using (server)
        {
            var root = server.Store.MailboxId("archive")!;
            var id = server.Store.Deliver(new FakeMail { Subject = "blocked", ReceivedAt = Date(2024, 3) }, root);
            var hooks = new HookBus();
            using var veto = folderVeto
                ? hooks.Intercept<MailboxCreatePayload>(HookNames.OnBeforeMailboxCreate, (p, _) => ValueTask.FromResult(!string.Equals(p.Name, "2024", StringComparison.Ordinal)))
                : hooks.Intercept<EmailMovePayload>(HookNames.OnBeforeEmailMove, (p, _) => ValueTask.FromResult(!p.EmailIds.Contains(id, StringComparer.Ordinal)));
            var result = await new ArchiveReorganizer(rig, new MailWriterFactory(), hooks, TimeProvider.System).ReorganizeAsync(key, "year", Ct);
            Assert.True(result.IsOk, result.Error?.Message);
            Assert.True(result.Value!.Skipped >= 1);
            Assert.True(server.Store.Email(id)!["mailboxIds"]!.AsObject().ContainsKey(root));
        }
    }

    [Fact]
    public async Task Successful_folder_and_move_hooks_report_the_actual_group()
    {
        var rig = ContactRig.Create();
        var (key, server, _) = await rig.AddAsync("hooks@example.org", null);
        using (server)
        {
            var root = server.Store.MailboxId("archive")!;
            var id = server.Store.Deliver(new FakeMail { Subject = "hooked", ReceivedAt = Date(2026, 3) }, root);
            var hooks = new HookBus();
            var folderBefore = 0;
            var folderAfter = 0;
            var moveBefore = 0;
            var done = new TaskCompletionSource<EmailMovePayload>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var beforeFolder = hooks.Intercept<MailboxCreatePayload>(HookNames.OnBeforeMailboxCreate, (_, _) => { folderBefore++; return ValueTask.FromResult(true); });
            using var afterFolder = hooks.Observe<MailboxCreatedPayload>(HookNames.OnMailboxCreate, (_, _) => { folderAfter++; return ValueTask.CompletedTask; });
            using var beforeMove = hooks.Intercept<EmailMovePayload>(HookNames.OnBeforeEmailMove, (_, _) => { moveBefore++; return ValueTask.FromResult(true); });
            using var afterMove = hooks.Observe<EmailMovePayload>(HookNames.OnAfterEmailMove, (payload, _) => { done.TrySetResult(payload); return ValueTask.CompletedTask; });
            var result = await new ArchiveReorganizer(rig, new MailWriterFactory(), hooks, TimeProvider.System).ReorganizeAsync(key, "year", Ct);
            Assert.True(result.IsOk, result.Error?.Message);
            var payload = await done.Task.WaitAsync(Ct);
            Assert.Equal(1, folderBefore);
            Assert.Equal(1, folderAfter);
            Assert.Equal(1, moveBefore);
            Assert.Contains(id, payload.EmailIds);
            Assert.Equal(root, payload.FromMailboxId);
            Assert.Equal(result.Value!.Moved, payload.EmailIds.Count);
        }
    }

    [Fact]
    public async Task A_failed_later_chunk_reports_already_completed_moves()
    {
        var rig = ContactRig.Create();
        var (key, server, _) = await rig.AddAsync("partial@example.org", null, new FakeJmapOptions { MaxObjectsInSet = 1 });
        using (server)
        {
            var root = server.Store.MailboxId("archive")!;
            server.Store.Deliver(new FakeMail { Subject = "one", ReceivedAt = Date(2026, 3) }, root);
            server.Store.Deliver(new FakeMail { Subject = "two", ReceivedAt = Date(2026, 4) }, root);
            var hooks = new HookBus();
            using var inject = hooks.Intercept<EmailMovePayload>(HookNames.OnBeforeEmailMove, (_, _) => { server.FailApiRequest(2); return ValueTask.FromResult(true); });
            var result = await new ArchiveReorganizer(rig, new MailWriterFactory(), hooks, TimeProvider.System).ReorganizeAsync(key, "year", Ct);
            Assert.False(result.IsOk);
            Assert.NotNull(result.Value);
            Assert.Equal(1, result.Value!.Moved);
            Assert.True(result.Value.SnapshotComplete);
            Assert.Equal(1, result.Value.Unknown);
            Assert.Equal(result.Value.Total, result.Value.Moved + result.Value.Skipped + result.Value.Failed + result.Value.Unknown + result.Value.Unattempted);
            Assert.Contains("outcome may be unknown", result.Value.Detail);
        }
    }

    [Fact]
    public async Task Cancellation_before_a_move_reports_unattempted_messages()
    {
        var rig = ContactRig.Create();
        var (key, server, _) = await rig.AddAsync("cancel@example.org", null);
        using (server)
        using (var cancel = new CancellationTokenSource())
        {
            var hooks = new HookBus();
            using var stop = hooks.Intercept<EmailMovePayload>(HookNames.OnBeforeEmailMove, async (_, _) => { await cancel.CancelAsync(); return true; });
            var result = await new ArchiveReorganizer(rig, new MailWriterFactory(), hooks, TimeProvider.System).ReorganizeAsync(key, "year", cancel.Token);
            Assert.False(result.IsOk);
            Assert.True(result.Value!.Cancelled);
            Assert.True(result.Value.SnapshotComplete);
            Assert.Equal(result.Value.Total, result.Value.Unattempted);
            Assert.Equal(0, result.Value.Moved);
            Assert.DoesNotContain(server.ApiCalls, call => call.Contains("Email/set", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Requested_account_is_used_even_when_another_account_is_active()
    {
        var rig = ContactRig.Create();
        var (_, first, _) = await rig.AddAsync("first@example.org", null);
        var (secondKey, second, _) = await rig.AddAsync("second@example.org", null);
        using (first)
        using (second)
        {
            var before = first.ApiCalls.Count;
            var result = await new ArchiveReorganizer(rig, new MailWriterFactory(), new HookBus(), TimeProvider.System).ReorganizeAsync(secondKey, "year", Ct);
            Assert.True(result.IsOk, result.Error?.Message);
            Assert.Equal(before, first.ApiCalls.Count);
            Assert.Contains(second.ApiCalls, call => call.Contains("Email/set", StringComparison.Ordinal));
        }
    }
}

// Exercises the host tools an add-on gets (IPluginHostTools, decision D-M6b-08; B-AI-004, B-AI-008) through the fake JMAP server and the real compose and mail writers.
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Contacts;
using Barbican.Core.Jmap;
using Barbican.Core.Mail;
using Barbican.Core.Mail.Service;
using Barbican.Core.Mail.Write;
using Barbican.Core.Plugins.Native.Host;
using Barbican.Core.Settings.Pages;
using Barbican.Core.Tests.Compose;
using Barbican.Core.Tests.Contacts;
using Barbican.Core.Tests.Calendar;
using Barbican.FakeJmap;
using Barbican.Core.Tests.Filters.Rules;
using Barbican.Core.Tests.Jmap;

namespace Barbican.Core.Tests.Plugins.Native.Host;

public sealed class HostToolsTests
{
    [Fact]
    public async Task Specs_hide_calendar_and_contact_tools_when_the_account_lacks_the_capability_and_carry_a_json_schema()
    {
        var rig = await PipelineRig.StartAsync();
        var specs = Tools(rig).Specs(PipelineRig.Account);
        Assert.Contains(specs, t => string.Equals(t.Name, "search_mail", StringComparison.Ordinal) && !t.Writes);
        Assert.Contains(specs, t => string.Equals(t.Name, "send_email", StringComparison.Ordinal) && t.Writes);
        Assert.DoesNotContain(specs, t => string.Equals(t.Name, "remember", StringComparison.Ordinal));
        Assert.All(specs, t => Assert.Equal("object", (string?)JsonNode.Parse(t.SchemaJson)!["type"]));
        Assert.DoesNotContain(Tools(rig).Specs("someone-else@example.org"), t => t.Name is "check_calendar" or "create_event" or "find_contact" or "create_contact");
    }

    [Fact]
    public async Task Arguments_that_are_not_json_are_text_the_model_can_read()
    {
        var rig = await PipelineRig.StartAsync();
        Assert.StartsWith("Error:", await Tools(rig).RunAsync(PipelineRig.Account, "search_mail", "{not json", TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tag_tool_applies_and_removes_existing_tag_on_fake_server()
    {
        var rig = await PipelineRig.StartAsync();
        var tools = Tools(rig);
        var id = rig.Store.EmailIds()[0];
        var args = new JsonObject { ["ids"] = new JsonArray(id), ["tag"] = "Red" };
        var result = await RunAsync(tools, PipelineRig.Account, "tag_messages", args, TestContext.Current.CancellationToken);
        Assert.Contains("1 updated", result, StringComparison.Ordinal);
        Assert.True((bool?)rig.Store.Email(id)!["keywords"]!["$label:red"]);
        args["remove"] = true;
        result = await RunAsync(tools, PipelineRig.Account, "tag_messages", args, TestContext.Current.CancellationToken);
        Assert.Contains("1 updated", result, StringComparison.Ordinal);
        Assert.Null(rig.Store.Email(id)!["keywords"]!["$label:red"]);
    }

    [Fact]
    public async Task Draft_tool_opens_prefilled_composer_without_saving()
    {
        var rig = await PipelineRig.StartAsync();
        var opener = new DraftOpener();
        var tools = Tools(rig, opener);
        var args = new JsonObject { ["to"] = "erin@example.net", ["subject"] = "Assistant draft", ["body"] = "Hello" };
        var before = rig.InFolder("drafts").Count;
        var result = await RunAsync(tools, PipelineRig.Account, "draft_email", args, TestContext.Current.CancellationToken);
        Assert.Contains("open in the composer", result, StringComparison.Ordinal);
        Assert.Equal((PipelineRig.Account, "erin@example.net", "Assistant draft", "Hello"), opener.Opened);
        Assert.Equal(before, rig.InFolder("drafts").Count);
    }

    [Fact]
    public async Task Filter_tool_adds_and_removes_a_rule_through_fake_server()
    {
        var rig = await FilterTestRig.StartAsync();
        var tools = new HostTools(new AssistantAccounts(rig.Fake.Client, rig.Clock), new MailWriterFactory(), new Values(), rig.Vacation, rig.Filters, null!, null!, null!, null!, null!, null!);
        var args = new JsonObject { ["name"] = "News", ["field"] = "subject", ["comparator"] = "contains", ["value"] = "digest", ["action"] = "star" };
        var added = await RunAsync(tools, FilterTestRig.Account, "add_filter_rule", args, TestContext.Current.CancellationToken);
        Assert.Equal("Rule added.", added);
        var loaded = await rig.Filters.LoadAsync(FilterTestRig.Account, TestContext.Current.CancellationToken);
        Assert.Equal("News", (string?)Assert.Single(loaded.Value!.Rules)["name"]);
        var removed = await RunAsync(tools, FilterTestRig.Account, "remove_filter_rule", new JsonObject { ["name"] = "News" }, TestContext.Current.CancellationToken);
        Assert.Equal("Rule removed.", removed);
        Assert.Empty((await rig.Filters.LoadAsync(FilterTestRig.Account, TestContext.Current.CancellationToken)).Value!.Rules);
    }

    [Fact]
    public async Task Contact_tools_create_and_find_a_card_in_the_selected_account()
    {
        var rig = ContactRig.Create();
        var data = FakeSeed.ContactsM5(0);
        var (key, _, _) = await rig.AddAsync("user@example.org", data);
        var store = new ContactStore(rig, rig.Hooks, rig.Time);
        var writer = new ContactWriter(rig, store, new AddressBooks(rig, store), rig.Hooks);
        var tools = new HostTools(rig, null!, new Values(), null!, null!, null!, null!, store, writer, null!, null!);
        var created = await RunAsync(tools, key, "create_contact", new JsonObject { ["name"] = "Assistant Contact", ["email"] = "erin@example.net" }, TestContext.Current.CancellationToken);
        Assert.Equal("Contact created.", created);
        var found = await RunAsync(tools, key, "find_contact", new JsonObject { ["query"] = "Assistant Contact" }, TestContext.Current.CancellationToken);
        Assert.Contains("Assistant Contact", found, StringComparison.Ordinal);
        Assert.Contains("<email>", found, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Calendar_tool_uses_a_writable_own_calendar_with_configured_zone(bool readOnlyDefault)
    {
        var data = new FakeCalendars();
        if (readOnlyDefault)
        {
            _ = data.AddCalendar("Read-only", isDefault: true, extra: new JsonObject
            {
                ["myRights"] = new JsonObject { ["mayReadItems"] = true, ["mayWriteAll"] = false, ["mayWriteOwn"] = false },
            });
        }

        var calendar = data.AddCalendar("Work", isDefault: !readOnlyDefault);
        var rig = CalendarRig.Create();
        var (key, _, _) = await rig.AddAsync("user@example.org", new FakeJmapOptions { Calendars = data });
        using var store = new CalendarStore(rig, rig.Hooks);
        var writer = new CalendarWriter(rig, store, rig.Hooks);
        var tools = new HostTools(rig, null!, new Values("Europe/Istanbul"), null!, null!, null!, null!, null!, null!, store, writer);
        var args = new JsonObject { ["title"] = "Assistant event", ["start"] = "2026-11-20T10:00:00", ["duration_minutes"] = 30 };
        var created = await RunAsync(tools, key, "create_event", args, TestContext.Current.CancellationToken);
        Assert.Equal("Event created.", created);
        var ev = Assert.Single(data.Events());
        Assert.Equal("Europe/Istanbul", (string?)ev["timeZone"]);
        Assert.True((bool?)ev["calendarIds"]![calendar]);
        var found = await RunAsync(tools, key, "check_calendar", new JsonObject { ["after"] = "2026-11-20", ["before"] = "2026-11-21" }, TestContext.Current.CancellationToken);
        Assert.Contains("Assistant event", found, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_tool_submits_confirmed_message_via_fake_server()
    {
        var rig = await PipelineRig.StartAsync();
        var tools = Tools(rig);
        var args = new JsonObject { ["to"] = "erin@example.net", ["subject"] = "Confirmed message", ["body"] = "Hello" };
        var result = await RunAsync(tools, PipelineRig.Account, "send_email", args, TestContext.Current.CancellationToken);
        Assert.Equal("Email sent.", result);
        Assert.Contains(rig.InFolder("sent"), id => string.Equals((string?)rig.Store.Email(id)?["subject"], "Confirmed message", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Tag_tool_respects_read_only_transport()
    {
        var fake = await Fake.StartAsync(PipelineRig.M3Options(), readOnly: true);
        var tools = new HostTools(new AssistantAccounts(fake.Client, fake.Time), new MailWriterFactory(), new Values(), null!, null!, null!, null!, null!, null!, null!, null!);
        var id = fake.Server.Store.EmailIds()[0];
        var result = await RunAsync(tools, PipelineRig.Account, "tag_messages", new JsonObject { ["ids"] = new JsonArray(id), ["tag"] = "Red" }, TestContext.Current.CancellationToken);
        Assert.StartsWith("Error:", result, StringComparison.Ordinal);
        Assert.Null(fake.Server.Store.Email(id)!["keywords"]!["$label:red"]);
    }

    [Fact]
    public async Task Text_attachment_is_bounded_and_wrapped_as_untrusted_mail()
    {
        var rig = await PipelineRig.StartAsync();
        var id = rig.Store.Deliver(new FakeMail
        {
            Subject = "Large attachment",
            FromEmail = "erin@example.net",
            Text = "See attachment",
            Attachments = [new FakeAttachment("notes.txt", "text/plain", System.Text.Encoding.UTF8.GetBytes(new string('x', 100_000)))],
        });
        var result = await RunAsync(Tools(rig), PipelineRig.Account, "read_attachment", new JsonObject { ["id"] = id, ["name"] = "notes" }, TestContext.Current.CancellationToken);
        Assert.StartsWith("<email>", result, StringComparison.Ordinal);
        Assert.EndsWith("</email>", result, StringComparison.Ordinal);
        Assert.True(result.Length <= 40_000);
        Assert.True(result.Length > 30_000);
    }

    private static Task<string> RunAsync(HostTools tools, string account, string name, JsonObject args, CancellationToken ct) => tools.RunAsync(account, name, args.ToJsonString(), ct);

    private sealed class DraftOpener : IPluginComposer
    {
        public (string, string, string, string)? Opened { get; private set; }
        public Task<Result<Unit>> OpenAsync(ComposeRequest request, CancellationToken ct)
        {
            Opened = (request.AccountId, request.Prefill!.To, request.Prefill.Subject, request.Prefill.Body);
            return Task.FromResult(Result<Unit>.Ok(default));
        }
    }

    private static HostTools Tools(PipelineRig rig, IPluginComposer? opener = null) => new(new AssistantAccounts(rig.Fake.Client, rig.Clock), new MailWriterFactory(), new Values(), null!, null!, rig.Seeder, rig.Sender, null!, null!, null!, null!, opener);

    private sealed class Values(string? timeZone = null) : ISettingValues
    {
        public JsonNode? Stored(string key) => string.Equals(key, "timeZone", StringComparison.Ordinal) && timeZone is not null ? JsonValue.Create(timeZone) : null;
        public Task<Result<Unit>> SetAsync(string key, JsonNode? value, CancellationToken ct) => throw new NotSupportedException();
        public event EventHandler<SettingChangedEventArgs>? Changed { add { } remove { } }
    }

    private sealed class AssistantAccounts(IJmapClient client, TimeProvider time) : IAccountManager
    {
        public IReadOnlyList<AccountInfo> Accounts => [];
        public AccountInfo? Active => null;
        public event EventHandler<ActiveAccountChangedEventArgs>? ActiveChanged { add { } remove { } }
        public Task<Result<AccountInfo>> SignInWithPasswordAsync(PasswordSignIn request, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<AccountInfo>> SignInWithOAuthAsync(OAuthSignIn request, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<Unit>> SwitchToAsync(string accountKey, CancellationToken ct) => throw new NotSupportedException();
        public IAccountSession? SessionFor(string accountKey) => string.Equals(accountKey, PipelineRig.Account, StringComparison.Ordinal) ? new Session(client, time) : null;
    }

    private sealed class Session(IJmapClient client, TimeProvider time) : IAccountSession
    {
        public AccountInfo Account => new(PipelineRig.Account, "user", "example.invalid");
        public IJmapClient Client => client;
        public IMailService Mail { get; } = new MailService(client, time);
        public IMailSync Sync => throw new NotSupportedException();
    }
}

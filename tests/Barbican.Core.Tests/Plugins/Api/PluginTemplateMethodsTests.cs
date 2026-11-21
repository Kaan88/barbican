// Source: Bulwark lib/plugin-sandbox/host-api.ts:322-327 (templates:read for list and get, templates:write for create, update, delete), host-api-settings.ts:29-121
// (500 templates, name 1-200, subject 998, category 100, body 100,000, only five fields, HTML bodies cleaned, patch not empty, missing id is an error) and
// plugin-projection.ts:224-233 (the template view). The service is the Templates settings page's ITemplateAdmin (an in-memory fake here), so each write is visible there.
using Barbican.Core.Compose;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Api.Templates;
using Barbican.Core.Plugins.Bridge;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class PluginTemplateMethodsTests
{
    private static Task<PluginApiRig> StartAsync(FakeTemplateAdmin admin, params string[] permissions) =>
        PluginApiRig.StartAsync(() => PluginTemplateMethods.Create(admin, new PluginMailRateLimiter(new FakeTimeProvider())), permissions);

    [Fact]
    public async Task List_and_get_project_exactly_six_fields_from_the_active_account()
    {
        var admin = new FakeTemplateAdmin();
        admin.Items.Add(new MailTemplate("t9", "Hello", "Subject", "<p>x</p>", true) { Category = "Work", IsFavorite = true });
        var rig = await StartAsync(admin, "templates:read");
        var list = (await rig.CallAsync("templates.list", "[]")).Value!;
        Assert.Equal(["body", "category", "id", "isHTML", "name", "subject"], PluginApiRig.Keys(list.Item(0)), StringComparer.Ordinal);
        Assert.Equal("a1", admin.LastAccountKey, StringComparer.Ordinal);
        var one = (await rig.CallAsync("templates.get", """["t9"]""")).Value!;
        Assert.Equal("Work", one.Property("category").StringValue, StringComparer.Ordinal);
        Assert.True(one.Property("isHTML").BooleanValue);
        Assert.Equal(Barbican.Core.Plugins.Protocol.PluginValueKind.Null, (await rig.CallAsync("templates.get", """["nope"]""")).Value!.Kind);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("templates.get", "[1]")).Error);
    }

    [Theory]
    [InlineData("templates.list", "[]", "templates:write")]
    [InlineData("templates.get", """["t1"]""", "templates:write")]
    [InlineData("templates.create", """[{"name":"N"}]""", "templates:read")]
    [InlineData("templates.update", """["t1",{"name":"N"}]""", "templates:read")]
    [InlineData("templates.delete", """["t1"]""", "templates:read")]
    public async Task Each_method_is_denied_without_its_own_permission_and_changes_nothing(string method, string args, string held)
    {
        var admin = new FakeTemplateAdmin();
        admin.Items.Add(new MailTemplate("t1", "Keep", "", "", false));
        var rig = await StartAsync(admin, held);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync(method, args)).Error);
        Assert.Equal(0, admin.Writes);
        Assert.Single(admin.Items);
        Assert.Null(admin.LastAccountKey);
    }

    [Fact]
    public async Task Create_makes_a_template_the_app_service_sees_and_sets_the_category()
    {
        var admin = new FakeTemplateAdmin();
        var rig = await StartAsync(admin, "templates:write");
        var result = await rig.CallAsync("templates.create", """[{"name":"  Greeting ","subject":"Hi","body":"Hello","category":" Work "}]""");
        Assert.True(result.IsSuccess);
        Assert.Equal(["body", "category", "id", "isHTML", "name", "subject"], PluginApiRig.Keys(result.Value!), StringComparer.Ordinal);
        var stored = Assert.Single(admin.Items);
        Assert.Equal("Greeting", stored.Name, StringComparer.Ordinal);
        Assert.Equal("Work", stored.Category, StringComparer.Ordinal);
        Assert.Equal("Hello", stored.Body, StringComparer.Ordinal);
        Assert.False(stored.IsHtml);
    }

    [Fact]
    public async Task An_html_body_is_cleaned_on_create_and_when_a_plain_template_becomes_html()
    {
        var admin = new FakeTemplateAdmin();
        var rig = await StartAsync(admin, "templates:write");
        await rig.CallAsync("templates.create", """[{"name":"H","isHTML":true,"body":"<p onclick=\"x()\">Hi</p><script>alert(1)</script>"}]""");
        Assert.DoesNotContain("script", admin.Items[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", admin.Items[0].Body, StringComparison.OrdinalIgnoreCase);
        await rig.CallAsync("templates.create", """[{"name":"P","body":"<script>alert(1)</script>"}]""");
        Assert.Contains("script", admin.Items[1].Body, StringComparison.Ordinal);
        var made = await rig.CallAsync("templates.update", """["t2",{"isHTML":true}]""");
        Assert.True(made.IsSuccess);
        Assert.DoesNotContain("script", admin.Items[1].Body, StringComparison.OrdinalIgnoreCase);
        Assert.True(admin.Items[1].IsHtml);
    }

    [Theory]
    [InlineData("""[{}]""")]
    [InlineData("""[{"name":"  "}]""")]
    [InlineData("""[{"name":"N","extra":1}]""")]
    [InlineData("""[{"name":"N","isHTML":"yes"}]""")]
    [InlineData("""[{"name":"N","subject":5}]""")]
    [InlineData("""[null]""")]
    [InlineData("[]")]
    public async Task Create_with_invalid_input_is_refused_before_any_write(string args)
    {
        var admin = new FakeTemplateAdmin();
        var rig = await StartAsync(admin, "templates:write");
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("templates.create", args)).Error);
        Assert.Equal(0, admin.Writes);
    }

    [Fact]
    public async Task Field_limits_are_enforced_at_their_edges()
    {
        var admin = new FakeTemplateAdmin();
        var rig = await StartAsync(admin, "templates:write");
        Assert.True((await rig.CallAsync("templates.create", $$"""[{"name":"{{new string('n', 200)}}","subject":"{{new string('s', 998)}}","category":"{{new string('c', 100)}}","body":"{{new string('b', 100_000)}}"}]""")).IsSuccess);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("templates.create", $$"""[{"name":"{{new string('n', 201)}}"}]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("templates.create", $$"""[{"name":"N","subject":"{{new string('s', 999)}}"}]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("templates.create", $$"""[{"name":"N","category":"{{new string('c', 101)}}"}]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("templates.create", $$"""[{"name":"N","body":"{{new string('b', 100_001)}}"}]""")).Error);
        Assert.Single(admin.Items);
    }

    [Fact]
    public async Task Create_is_refused_at_500_templates()
    {
        var admin = new FakeTemplateAdmin();
        for (var i = 0; i < 500; i++) admin.Items.Add(new MailTemplate("x" + i, "T" + i, "", "", false));
        var rig = await StartAsync(admin, "templates:write");
        Assert.Equal(PluginErrorCode.Failed, (await rig.CallAsync("templates.create", """[{"name":"One more"}]""")).Error);
        Assert.Equal(0, admin.Writes);
    }

    [Fact]
    public async Task Update_changes_the_given_fields_and_refuses_an_empty_patch_or_a_missing_id()
    {
        var admin = new FakeTemplateAdmin();
        admin.Items.Add(new MailTemplate("t1", "Old", "S", "B", false) { Category = "C", IsFavorite = true });
        var rig = await StartAsync(admin, "templates:write");
        var result = await rig.CallAsync("templates.update", """["t1",{"name":"New","subject":"S2"}]""");
        Assert.Equal("New", result.Value!.Property("name").StringValue, StringComparer.Ordinal);
        Assert.Equal(("New", "S2", "B", "C", true), (admin.Items[0].Name, admin.Items[0].Subject, admin.Items[0].Body, admin.Items[0].Category, admin.Items[0].IsFavorite));
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("templates.update", """["t1",{}]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("templates.update", """["t1",{"id":"z"}]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("templates.update", """["zz",{"name":"X"}]""")).Error);
        Assert.Equal(1, admin.Writes);
    }

    [Fact]
    public async Task Delete_removes_the_template_and_a_missing_id_is_unavailable()
    {
        var admin = new FakeTemplateAdmin();
        admin.Items.Add(new MailTemplate("t1", "Old", "", "", false));
        var rig = await StartAsync(admin, "templates:write");
        var done = await rig.CallAsync("templates.delete", """["t1"]""");
        Assert.True(done.IsSuccess);
        Assert.Equal(Barbican.Core.Plugins.Protocol.PluginValueKind.Undefined, done.Value!.Kind);
        Assert.Empty(admin.Items);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("templates.delete", """["t1"]""")).Error);
    }

    [Fact]
    public async Task Writes_are_limited_to_thirty_a_minute()
    {
        var admin = new FakeTemplateAdmin();
        var rig = await StartAsync(admin, "templates:write");
        for (var i = 0; i < 30; i++) Assert.True((await rig.CallAsync("templates.create", $$"""[{"name":"T{{i}}"}]""")).IsSuccess);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("templates.create", """[{"name":"Over"}]""")).Error);
        Assert.Equal(30, admin.Writes);
    }

    [Fact]
    public async Task A_stale_session_or_revoked_grant_before_the_write_writes_nothing()
    {
        var admin = new FakeTemplateAdmin();
        var rig = await StartAsync(admin, "templates:write");
        rig.Active.Valid = false;
        Assert.Equal(PluginErrorCode.NoSession, (await rig.CallAsync("templates.create", """[{"name":"X"}]""")).Error);
        rig.Active.Valid = true;
        admin.Hook = () => rig.Auth.Current = PluginApiRig.Grant(["templates:write"], enabled: false);
        Assert.False((await rig.CallAsync("templates.create", """[{"name":"X"}]""")).IsSuccess);
        Assert.Equal(0, admin.Writes);
    }

    [Fact]
    public async Task A_write_that_goes_stale_after_it_was_issued_reports_an_unknown_outcome_without_data()
    {
        var admin = new FakeTemplateAdmin();
        var rig = await StartAsync(admin, "templates:write");
        var calls = 0;
        admin.Hook = () =>
        {
            // the list read is call 1, the write is call 2: the login changes as the write goes out
            if (++calls == 2) rig.Active.Valid = false;
        };
        var result = await rig.CallAsync("templates.create", """[{"name":"X"}]""");
        Assert.Equal(PluginErrorCode.UnknownOutcome, result.Error);
        Assert.Null(result.Value);
        Assert.Equal(1, admin.Writes);
    }
}

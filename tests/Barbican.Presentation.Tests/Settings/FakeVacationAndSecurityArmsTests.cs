// Green: the FakeJmap arms of the M6 plan (section 4.12): VacationResponse/get and the x:AccountPassword, x:AccountSettings, x:AppPassword and x:ApiKey reads are real; their set arms and Identity/set answer unknownMethod until srv-data and set-data fill them.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap;
using Barbican.FakeJmap;

namespace Barbican.Presentation.Tests.Settings;

public sealed class FakeVacationAndSecurityArmsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(string Name, JsonObject Args)> CallAsync(FakeJmapServer server, string method, JsonObject args)
    {
        var client = await FakeJmapHarness.ConnectAsync(server, TimeProvider.System, _ => { }, false, Ct);
        args["accountId"] = client.Session.MailAccountId;
        var r = await client.SendAsync(JmapRequest.Of(JmapRequest.Call(method, args, "c")), Ct);
        var response = r.Value!.MethodResponses[0];
        return ((string)response[0]!, response[1]!.AsObject());
    }

    [Fact]
    public async Task Vacation_get_answers_the_singleton_and_set_is_a_stub()
    {
        var server = new FakeJmapServer(new FakeJmapOptions { VacationCapability = true, Vacation = new FakeVacation { IsEnabled = true, Subject = "Away", TextBody = "Back soon" } });
        var (_, got) = await CallAsync(server, "VacationResponse/get", new JsonObject { ["ids"] = null });
        Assert.Equal("singleton", (string?)got["list"]![0]!["id"]);
        Assert.Equal("Away", (string?)got["list"]![0]!["subject"]);
        Assert.Equal(true, (bool?)got["list"]![0]!["isEnabled"]);
        var (_, some) = await CallAsync(server, "VacationResponse/get", new JsonObject { ["ids"] = new JsonArray("nope") });
        Assert.Empty(some["list"]!.AsArray());
        Assert.Equal("nope", (string?)some["notFound"]![0]);
        var (name, _) = await CallAsync(server, "VacationResponse/set", new JsonObject { ["update"] = new JsonObject() });
        Assert.Equal("error", name);

        var plain = new FakeJmapServer(new FakeJmapOptions());
        var (off, _) = await CallAsync(plain, "VacationResponse/get", new JsonObject { ["ids"] = null });
        Assert.Equal("error", off);
    }

    [Fact]
    public async Task Account_security_reads_are_served()
    {
        var s = new FakeAccountSecurity { OtpUrl = "otpauth://totp/x?secret=ABC", Description = "Ann" };
        s.AppPasswords.Add(new JsonObject { ["id"] = "ap1", ["description"] = "phone" });
        s.ApiKeys.Add(new JsonObject { ["id"] = "ak1", ["description"] = "script" });
        var server = new FakeJmapServer(new FakeJmapOptions { AccountSecurity = s });
        var (_, password) = await CallAsync(server, "x:AccountPassword/get", new JsonObject { ["ids"] = new JsonArray("singleton") });
        Assert.Equal("otpauth://totp/x?secret=ABC", (string?)password["list"]![0]!["otpAuth"]!["otpUrl"]);
        var (_, settings) = await CallAsync(server, "x:AccountSettings/get", new JsonObject { ["ids"] = new JsonArray("singleton") });
        Assert.Equal("Ann", (string?)settings["list"]![0]!["description"]);
        var (_, query) = await CallAsync(server, "x:AppPassword/query", new JsonObject());
        Assert.Equal("ap1", (string?)query["ids"]![0]);
        var (_, apps) = await CallAsync(server, "x:AppPassword/get", new JsonObject { ["ids"] = new JsonArray("ap1", "zz") });
        Assert.Equal("phone", (string?)apps["list"]![0]!["description"]);
        Assert.Equal("zz", (string?)apps["notFound"]![0]);
        var (_, keys) = await CallAsync(server, "x:ApiKey/get", new JsonObject { ["ids"] = null });
        Assert.Equal("script", (string?)keys["list"]![0]!["description"]);
        // the set arms are filled since M6 set-data: an empty create is refused object by object, not as a method error
        var (set, setAnswer) = await CallAsync(server, "x:AppPassword/set", new JsonObject { ["create"] = new JsonObject { ["c"] = new JsonObject() } });
        Assert.Equal("x:AppPassword/set", set);
        Assert.Equal("invalidProperties", (string?)setAnswer["notCreated"]!["c"]!["type"]);
        var (identity, identityAnswer) = await CallAsync(server, "Identity/set", new JsonObject { ["create"] = new JsonObject { ["c"] = new JsonObject() } });
        Assert.Equal("Identity/set", identity);
        Assert.Equal("invalidProperties", (string?)identityAnswer["notCreated"]!["c"]!["type"]);

        var plain = new FakeJmapServer(new FakeJmapOptions());
        var (off, _) = await CallAsync(plain, "x:AccountPassword/get", new JsonObject { ["ids"] = new JsonArray("singleton") });
        Assert.Equal("error", off);
    }
}

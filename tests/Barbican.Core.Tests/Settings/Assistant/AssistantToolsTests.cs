// Exercises assistant tools through the fake JMAP server and the real compose and mail writers.
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Jmap;
using Barbican.Core.Mail;
using Barbican.Core.Mail.Service;
using Barbican.Core.Mail.Write;
using Barbican.Core.Settings.Assistant;
using Barbican.Core.Settings.Pages;
using Barbican.Core.Tests.Compose;

namespace Barbican.Core.Tests.Settings.Assistant;

public sealed class AssistantToolsTests
{
    [Fact]
    public async Task Tag_tool_applies_and_removes_existing_tag_on_fake_server()
    {
        var rig = await PipelineRig.StartAsync();
        var tools = Tools(rig);
        var id = rig.Store.EmailIds()[0];
        var args = new JsonObject { ["ids"] = new JsonArray(id), ["tag"] = "Red" };
        var result = await tools.RunAsync(PipelineRig.Account, "tag_messages", args, TestContext.Current.CancellationToken);
        Assert.Contains("1 updated", result, StringComparison.Ordinal);
        Assert.True((bool?)rig.Store.Email(id)!["keywords"]!["$label:red"]);
        args["remove"] = true;
        result = await tools.RunAsync(PipelineRig.Account, "tag_messages", args, TestContext.Current.CancellationToken);
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
        var result = await tools.RunAsync(PipelineRig.Account, "draft_email", args, TestContext.Current.CancellationToken);
        Assert.Contains("open in the composer", result, StringComparison.Ordinal);
        Assert.Equal((PipelineRig.Account, "erin@example.net", "Assistant draft", "Hello"), opener.Opened);
        Assert.Equal(before, rig.InFolder("drafts").Count);
    }

    private sealed class DraftOpener : IAssistantComposer
    {
        public (string, string, string, string)? Opened { get; private set; }
        public Task<Result<Unit>> OpenAsync(ComposeRequest request, CancellationToken ct)
        {
            Opened = (request.AccountId, request.Prefill!.To, request.Prefill.Subject, request.Prefill.Body);
            return Task.FromResult(Result<Unit>.Ok(default));
        }
    }

    private static AssistantTools Tools(PipelineRig rig, IAssistantComposer? opener = null) => new(new AssistantAccounts(rig.Fake.Client, rig.Clock), new MailWriterFactory(), new Values(), null!, null!, null!, rig.Seeder, rig.Sender, null!, null!, null!, null!, opener);

    private sealed class Values : ISettingValues
    {
        public JsonNode? Stored(string key) => null;
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

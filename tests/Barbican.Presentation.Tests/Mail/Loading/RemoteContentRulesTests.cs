// I-209, G-053: what externalContentPolicy does to a message, and the banner Bulwark shows when the content is held back
// (Bulwark components/email/email-viewer.tsx:3129-3146, :6343-6392; stores/settings-store.ts:1118).

using System.Text.Json.Nodes;
using Barbican.Core.Storage;
using Barbican.Presentation.Mail.Loading;

namespace Barbican.Presentation.Tests.Mail.Loading;

public sealed class RemoteContentRulesTests
{
    [Fact]
    public void The_policy_defaults_to_ask_and_reads_the_three_values()
    {
        Assert.Equal(RemoteContentPolicy.Ask, RemoteContentRules.PolicyOf(Prefs([])));
        Assert.Equal(RemoteContentPolicy.Ask, RemoteContentRules.PolicyOf(Prefs(["externalContentPolicy", "nonsense"])));
        Assert.Equal(RemoteContentPolicy.Ask, RemoteContentRules.PolicyOf(Prefs(["externalContentPolicy", "ask"])));
        Assert.Equal(RemoteContentPolicy.Block, RemoteContentRules.PolicyOf(Prefs(["externalContentPolicy", "block"])));
        Assert.Equal(RemoteContentPolicy.Allow, RemoteContentRules.PolicyOf(Prefs(["externalContentPolicy", "allow"])));
    }

    [Fact]
    public void A_trusted_sender_loads_in_every_mode_and_allow_never_blocks()
    {
        Assert.True(RemoteContentRules.MayLoad(RemoteContentPolicy.Ask, trustedSender: true, allowedForMessage: false));
        Assert.True(RemoteContentRules.MayLoad(RemoteContentPolicy.Block, trustedSender: true, allowedForMessage: false));
        Assert.True(RemoteContentRules.MayLoad(RemoteContentPolicy.Allow, trustedSender: false, allowedForMessage: false));
        Assert.True(RemoteContentRules.MayLoad(RemoteContentPolicy.Ask, trustedSender: false, allowedForMessage: true));
        Assert.False(RemoteContentRules.MayLoad(RemoteContentPolicy.Ask, trustedSender: false, allowedForMessage: false));
        Assert.False(RemoteContentRules.MayLoad(RemoteContentPolicy.Block, trustedSender: false, allowedForMessage: true),
            "Always block has no per-message load (Bulwark email-viewer.tsx:6359)");
    }

    [Fact]
    public void Ask_offers_load_then_trust_and_block_offers_trust_only()
    {
        var ask = RemoteContentRules.Notice(hasRemoteContent: true, allowed: false, blockRemote: false, RemoteContentPolicy.Ask, trustedSender: false, hasSender: true);
        Assert.NotNull(ask);
        Assert.Equal(RemoteContentNotice.Heading, ask.Label);
        Assert.Equal(RemoteContentNotice.BlockedMessage, ask.Message);
        Assert.Equal(new[] { (RemoteContentNotice.Load, "Load images"), (RemoteContentNotice.Trust, "Always trust this sender") }, ask.Actions);

        var block = RemoteContentRules.Notice(hasRemoteContent: true, allowed: false, blockRemote: false, RemoteContentPolicy.Block, trustedSender: false, hasSender: true);
        Assert.NotNull(block);
        Assert.Equal(new[] { (RemoteContentNotice.Trust, "Always trust this sender") }, block.Actions);
    }

    [Fact]
    public void Nothing_shows_when_the_content_loads_or_a_plugin_keeps_it_off()
    {
        Assert.Null(RemoteContentRules.Notice(hasRemoteContent: false, allowed: false, blockRemote: false, RemoteContentPolicy.Ask, trustedSender: false, hasSender: true));
        Assert.Null(RemoteContentRules.Notice(hasRemoteContent: true, allowed: true, blockRemote: false, RemoteContentPolicy.Ask, trustedSender: false, hasSender: true));
        Assert.Null(RemoteContentRules.Notice(hasRemoteContent: true, allowed: false, blockRemote: true, RemoteContentPolicy.Ask, trustedSender: false, hasSender: true));
        Assert.Null(RemoteContentRules.Notice(hasRemoteContent: true, allowed: false, blockRemote: false, RemoteContentPolicy.Allow, trustedSender: false, hasSender: true));
        Assert.Null(RemoteContentRules.Notice(hasRemoteContent: true, allowed: true, blockRemote: false, RemoteContentPolicy.Block, trustedSender: true, hasSender: true));
    }

    [Fact]
    public void Without_a_sender_address_there_is_nothing_to_trust()
    {
        var notice = RemoteContentRules.Notice(hasRemoteContent: true, allowed: false, blockRemote: false, RemoteContentPolicy.Ask, trustedSender: false, hasSender: false);
        Assert.NotNull(notice);
        Assert.Equal(new[] { (RemoteContentNotice.Load, "Load images") }, notice.Actions);
        Assert.Empty(RemoteContentRules.Notice(hasRemoteContent: true, allowed: false, blockRemote: false, RemoteContentPolicy.Block, trustedSender: false, hasSender: false)!.Actions);
    }

    private static PrefsSnapshot Prefs(params string[] pair)
    {
        var values = new JsonObject();
        if (pair.Length == 2)
        {
            values[pair[0]] = pair[1];
        }

        return PrefsSnapshot.FromJson(values);
    }
}

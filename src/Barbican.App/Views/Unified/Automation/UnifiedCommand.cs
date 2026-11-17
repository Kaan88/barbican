#if AUTOMATION
// Mirrors: M2 plan 4.7 (`unified on|off`), 6.5 hidden scenario ("unified on; wshot"); spec/automation-protocol.md (M2 unified rows). Owner unified.
// `unified on [local]` sets enableUnifiedMailbox and unifiedCrossAccount (not with "local": the scenario reads two logins) and opens "All inboxes"; `unified off`
// clears them; `unified` (or `state`) reports the settings and the sources; `unified inbox` opens the Inbox of the account on screen; `unified log [LOGIN]` (sample runs) the requests per login. The prefs live in the run's own profile.

using System.Text.Json.Nodes;
using Barbican.Automation;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Unified;
using Barbican.Core.Storage;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Unified;

namespace Barbican.App.Views.Unified.Automation;

/// <summary>The <c>unified</c> command.</summary>
public sealed class UnifiedCommand(UnifiedInbox unified, UnifiedSectionViewModel section, IPrefsEditor prefs, IUnifiedLogins logins, MessageListViewModel list) : IAutomationCommand
{
    public string Name => "unified";

    public async Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        var words = (args ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        switch (words.FirstOrDefault())
        {
            case "on":
                prefs.Put(UnifiedSettings.EnabledKey, JsonValue.Create(true));
                prefs.Put(UnifiedSettings.CrossAccountKey, JsonValue.Create(!words.Contains("local", StringComparer.Ordinal)));
                section.Update();
                await section.OpenAsync().ConfigureAwait(true);
                return State();
            case "off":
                prefs.Put(UnifiedSettings.EnabledKey, JsonValue.Create(false));
                prefs.Put(UnifiedSettings.CrossAccountKey, JsonValue.Create(false));
                section.Update();
                return State();
            case null or "state":
                return State();
            case "log":
                return Log(words.Length > 1 ? words[1] : null);
            case "inbox":
                return await InboxAsync(cancellationToken).ConfigureAwait(true);
            default:
                return "error unified on [local]|off|state|inbox|log [LOGIN]";
        }
    }

    // back to the Inbox of the account on screen (the list path a click on the sidebar's Inbox row takes)
    private async Task<string> InboxAsync(CancellationToken ct)
    {
        if (logins.Active is not { } active || logins.ClientOf(active) is not { } client)
        {
            return "error no account on screen";
        }

        var boxes = await UnifiedFetcher.MailboxesAsync(new UnifiedSource(active, client, client.Session.MailAccountId, false), ct).ConfigureAwait(true);
        if (!boxes.IsOk || UnifiedRows.InboxOf(boxes.Value!) is not { } inbox)
        {
            return "error no inbox";
        }

        await list.OpenFolderAsync((string)inbox["id"]!, MailboxRole.Inbox, (string?)inbox["name"] ?? "Inbox").ConfigureAwait(true);
        section.Update();
        return "ok inbox " + (string)inbox["id"]!;
    }

    // sample runs: the requests each login sent, with the Email/set count (the scenario's "request log per account")
    private string Log(string? login)
    {
        if (logins is not SampleUnifiedLogins sample)
        {
            return "error unified log needs a sample run (BARBICAN_SAMPLE=1)";
        }

        var groups = sample.Log.Where(e => login is null || string.Equals(e.Login, login, StringComparison.Ordinal)).GroupBy(e => e.Login, StringComparer.Ordinal);
        return "log " + string.Join(" | ", groups.Select(g =>
            $"{g.Key}: requests={g.Count()} Email/set={g.Count(e => e.Methods.Contains("Email/set", StringComparison.Ordinal))} last={g.Last().Methods}"));
    }

    private string State()
    {
        var s = unified.Settings;
        var sources = unified.Sources();
        return $"unified enabled={s.Enabled} cross={s.CrossAccount} group={s.IncludeGroup} sources={sources.Count} open={section.IsOpen} badge={(section.Badge.Length == 0 ? "0" : section.Badge)}";
    }
}
#endif

// Mirrors: Bulwark components/email/recipient-mention.tsx; settings recipientMentionsEnabled, mentionAddsRecipientTo (G-063, G-075). Contract from the M3 plan section 4.6; owner editor-view consumes, composer-ui implements.

namespace Barbican.Presentation.Editor;

/// <summary>What the editor's @ popup lists and does on a pick: the composer's recipients, and (setting <c>mentionAddsRecipientTo</c>) adding the mentioned contact to a recipient field.</summary>
public interface IMentionSource
{
    /// <summary>Candidates whose name or address contains <paramref name="query"/> (empty lists all), the To, Cc and Bcc recipients first.</summary>
    IReadOnlyList<MentionCandidate> Candidates(string query);

    /// <summary>The user picked <paramref name="candidate"/>: add them to the field the setting names ("to", "cc", "bcc"; "none" does nothing) when they are not a recipient yet.</summary>
    void Picked(MentionCandidate candidate);
}

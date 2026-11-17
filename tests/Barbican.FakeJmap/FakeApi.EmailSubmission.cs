// (M3: the set, get and query arms moved to FakeApi.SubmissionHold.cs, which keeps these rules and adds HOLDFOR, undoStatus, onSuccessUpdateEmail and forbiddenFrom.)
// Mirrors: RFC 8621 section 7.5 (EmailSubmission/set: create with emailId, identityId and an explicit envelope); Stalwart
// crates/jmap/src/submission/set.rs (the email and the identity must exist; created carries id and undoStatus). J-068: the receipt path of the
// reader (MdnSender). Test convention: a recipient at refused.example (RFC 2606 name) is refused with invalidRecipients, so the
// "destroy the imported copy on failure" path can be driven. Owner reader.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private readonly List<JsonObject> _submissions = [];

    /// <summary>Every accepted submission (create object as sent), in order.</summary>
    public IReadOnlyList<JsonObject> Submissions
    {
        get
        {
            lock (_lock)
            {
                return [.. _submissions.Select(s => (JsonObject)s.DeepClone())];
            }
        }
    }
}

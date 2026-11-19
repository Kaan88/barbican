#if AUTOMATION_WRITES
// L-040 safety predicates: RFC 9661 SieveScript state and RFC 8621 Inbox scans.
// The write-gated runner must preserve the exact script and never apply a plan to other mail.
using System.Text;
using Barbican.Core.Filters.Rules;
using Barbican.Core.Sieve;

namespace Barbican.App.MailBody.Automation;

/// <summary>Fail-closed checks for the synthetic filter verification.</summary>
public static class FilterWriteSafety
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Only an empty script or the Sieve keep command is provably inert while the temporary rule is installed.</summary>
    public static bool IsInertScript(string text) => string.IsNullOrWhiteSpace(text) || string.Equals(text.Trim(), "keep;", StringComparison.Ordinal);

    /// <summary>Accepts only an already active managed target whose original bytes can be restored exactly.</summary>
    public static bool CanRestore(SieveScriptState state, byte[] raw)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(raw);
        if (string.IsNullOrEmpty(state.State) || state.Scripts.Count(script => script.IsActive) != 1
            || state.Target is not { IsActive: true, Id.Length: > 0 } || raw.AsSpan().StartsWith(Encoding.UTF8.Preamble))
            return false;
        try
        {
            var decoded = StrictUtf8.GetString(raw);
            return string.Equals(decoded, state.Text, StringComparison.Ordinal) && StrictUtf8.GetBytes(decoded).AsSpan().SequenceEqual(raw);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>Allows only the single expected move of the synthetic message into its own folder.</summary>
    public static bool IsExactPlan(FilterScan scan, string emailId, string folderId)
    {
        ArgumentNullException.ThrowIfNull(scan);
        return scan.Unsupported is null && scan.Total == 1 && scan.Steps.Count == 1
            && string.Equals(scan.Steps[0].Kind, "move", StringComparison.Ordinal)
            && string.Equals(scan.Steps[0].Arg, folderId, StringComparison.Ordinal)
            && scan.Steps[0].Ids.Count == 1 && string.Equals(scan.Steps[0].Ids[0], emailId, StringComparison.Ordinal)
            && !string.IsNullOrEmpty(scan.State);
    }
}
#endif

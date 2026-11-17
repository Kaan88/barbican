// Mirrors: nothing; the M1 placeholder of ILinkRouter (docs/milestones/M1-plan.md §5 F1 step 4).
// Bulwark: none.
using Microsoft.Extensions.Logging;

namespace Barbican.Core.Mail.Body;

/// <summary>
/// The M1 link router: opens nothing and logs the scheme only (a URL from a mail can carry personal
/// data and tracking tokens, so neither host, path nor query is logged). Replaced in M2.
/// </summary>
public sealed partial class LoggingLinkRouter(ILogger<LoggingLinkRouter> logger) : ILinkRouter
{
    public void Route(Uri link)
    {
        ArgumentNullException.ThrowIfNull(link);
        LinkIgnored(logger, link.Scheme);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "link from a message not opened in M1 (scheme {Scheme})")]
    private static partial void LinkIgnored(ILogger logger, string scheme);
}

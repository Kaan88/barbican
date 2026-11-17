// Mirrors: Rust mailview.rs:369-386 (the navigation and new-window handlers: http(s) to the browser,
//   mailto: to the composer). Bulwark: lib/email-sanitization.ts:261 and :521 (every mail link gets target=_blank, rel=noopener noreferrer: it
//   opens outside the app; a desktop client routes it to the system browser instead).
namespace Barbican.Core.Mail.Body;

/// <summary>
/// Where a link clicked in a message goes after the host's scheme check
/// (<see cref="ShellMessage.TryLink"/>). M1 registers <see cref="LoggingLinkRouter"/>, which opens
/// nothing; M2 replaces it (link check, then the browser for http/https and the composer for mailto).
/// </summary>
public interface ILinkRouter
{
    void Route(Uri link);
}

// Mirrors: M1-plan §4.2 (IAccountSession: Mail, Sync) and §4.3 (ViewModels consume the active account's services). Owner mailcore.

using Barbican.Core.Mail;

namespace Barbican.Presentation.Mail;

/// <summary>The mail services of whichever account is active; ViewModels take this so tests can hand them fakes.</summary>
public interface IActiveMail
{
    /// <summary>The key of the active account, or null when none is signed in.</summary>
    string? AccountKey { get; }

    /// <summary>The login name of the active account (the rail's account button).</summary>
    string? AccountUser { get; }

    IMailService? Mail { get; }

    IMailSync? Sync { get; }

    /// <summary>The active account changed (or signed out): drop what belongs to the old one.</summary>
    event EventHandler? Changed;
}

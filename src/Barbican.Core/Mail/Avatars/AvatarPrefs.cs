// Mirrors: Rust avatars.rs:28 Prefs (which pictures may be fetched); Bulwark settings senderFavicons, gravatarAvatars, showAvatarsInJunk. Stub from stage F of M2; owner reader.

namespace Barbican.Core.Mail.Avatars;

/// <summary>Which avatar sources the user allowed.</summary>
public readonly record struct AvatarPrefs(bool Favicons, bool Gravatar, bool InJunk)
{
    /// <summary>Whether any source is on.</summary>
    public bool Any => Favicons || Gravatar;
}

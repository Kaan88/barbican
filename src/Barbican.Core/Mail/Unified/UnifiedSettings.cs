// Mirrors: Rust unified.rs:190 enabled (enableUnifiedMailbox, off), :193 cross_account (unifiedCrossAccount, off), :196 include_group
// (includeGroupInUnified, on); Bulwark stores/settings-store.ts (the same keys and defaults). spec S-enableUnifiedMailbox,
// S-unifiedCrossAccount, S-includeGroupInUnified. Owner unified.

using Barbican.Core.Storage;

namespace Barbican.Core.Mail.Unified;

/// <summary>The unified inbox settings.</summary>
/// <param name="Enabled">The unified inbox row is shown.</param>
/// <param name="CrossAccount">Every signed-in login is read, not only the active one.</param>
/// <param name="IncludeGroup">Shared (group) accounts are read too.</param>
public sealed record UnifiedSettings(bool Enabled, bool CrossAccount, bool IncludeGroup)
{
    public const string EnabledKey = "enableUnifiedMailbox";
    public const string CrossAccountKey = "unifiedCrossAccount";
    public const string IncludeGroupKey = "includeGroupInUnified";

    /// <summary>The settings as the prefs hold them now.</summary>
    public static UnifiedSettings From(IPrefsStore prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        var p = prefs.Current;
        return new UnifiedSettings(p.Bool(EnabledKey, false), p.Bool(CrossAccountKey, false), p.Bool(IncludeGroupKey, true));
    }
}

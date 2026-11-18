// Mirrors: Bulwark components/settings/filter-settings.tsx, folder-settings.tsx, vacation-settings.tsx, M6 plan section 4.11. Contract from the M6 plan; owner srv-ui.

namespace Barbican.Presentation.Settings.Server;

/// <summary>What the value box of a filter condition takes.</summary>
public enum SrvUiValueKind
{
    /// <summary>No value (all messages, has any attachment).</summary>
    None,

    /// <summary>Free text (comma separated for several values).</summary>
    Text,

    /// <summary>A size such as 500K or 10M.</summary>
    Size,

    /// <summary>An attachment type (has_type).</summary>
    AttachmentType,
}

// Mirrors: M6 plan section 3.2 (who builds which settings page). Frozen in the M6 plan commit; owner integrator.

namespace Barbican.Presentation.Settings;

/// <summary>Which M6 stream builds the view of a settings page.</summary>
public enum SettingsPageOwner
{
    /// <summary>Stream set-ui: the generic schema-driven pages and the account, security, assistant and backup pages.</summary>
    SetUi,

    /// <summary>Stream srv-ui: the pages that edit server-side objects (folders, filters, out of office, identities, templates, recover).</summary>
    SrvUi,

    /// <summary>Pages deferred to a later milestone.</summary>
    Later,

    /// <summary>The installed add-ons page.</summary>
    Plugins,
}

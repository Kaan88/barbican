// Mirrors: Bulwark components/settings/settings-app.tsx (the tab ids, also the `id` of each tab of src/Barbican.Core/Settings/settings-schema.json), plus the three pages Barbican splits out of Bulwark's sections (identities, templates, assistant). Frozen in the M6 plan commit; owner integrator.

namespace Barbican.Presentation.Settings;

/// <summary>The ids of the settings pages.</summary>
public static class SettingsPageIds
{
    public const string Account = "account";
    public const string Security = "security";
    public const string Privacy = "privacy";
    public const string Notifications = "notifications";
    public const string General = "general";
    public const string Appearance = "appearance";
    public const string Accessibility = "accessibility";
    public const string MessageList = "message_list";
    public const string Reading = "reading";
    public const string Composing = "composing";
    public const string Identities = "identities";
    public const string Templates = "templates";
    public const string Organize = "organize";
    public const string Filters = "filters";
    public const string Vacation = "vacation";
    public const string Recover = "recover";
    public const string Assistant = "assistant";
    public const string Calendar = "calendar";
    public const string Contacts = "contacts";
    public const string Files = "files";
    public const string Addons = "addons";
    public const string Backup = "backup";
    public const string Troubleshooting = "troubleshooting";
    public const string About = "about";
}

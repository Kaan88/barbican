// Mirrors: Bulwark lib/settings-schema.ts:46-58 SettingKind (boolean, choice, timeZone; the hour, days, words, list and locale kinds have no row in Barbican's settings catalog).
namespace Barbican.Core.Plugins.Api.App;

/// <summary>How an add-on may change a setting.</summary>
internal enum PluginSettingKind
{
    /// <summary>On or off (a JSON boolean), possibly in one direction only.</summary>
    Boolean,

    /// <summary>One of the row's listed values, of the row's own JSON type.</summary>
    Choice,

    /// <summary>"auto" or an IANA time zone this machine knows.</summary>
    TimeZone,
}

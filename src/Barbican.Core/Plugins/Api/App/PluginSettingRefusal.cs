// Mirrors: Bulwark assistant/c.types.ts:143 SettingRefusal ('unknown' | 'not_allowed' | 'bad_value' | 'locked' | 'hidden' | 'unavailable' | 'stopped'). Barbican has no administrator lock or hidden flag on a setting, so 'locked' and 'hidden' never occur.
namespace Barbican.Core.Plugins.Api.App;

/// <summary>Why a setting cannot be changed.</summary>
internal enum PluginSettingRefusal
{
    Unknown,
    NotAllowed,
    BadValue,
    Unavailable,
    Stopped,
}

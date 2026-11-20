using Barbican.Core.Storage;

namespace Barbican.Core.Plugins.Bridge;

/// <summary>Windows profile policy for outbound add-on requests.</summary>
public static class PluginNetworkPolicy
{
    public const string Preference = "noThirdPartyTraffic";

    public static bool IsManaged => Environment.GetEnvironmentVariable("NO_THIRD_PARTY_TRAFFIC")?.Trim().ToLowerInvariant()
        is "1" or "true" or "yes" or "on";

    public static bool IsBlocked(IPrefsStore prefs) => IsManaged || prefs.Current.Bool(Preference, true);
}

// Mirrors: M6 plan section 3.2. One page of the settings navigation. Frozen in the M6 plan commit; owner integrator.

namespace Barbican.Presentation.Settings;

/// <summary>A page of the settings screen. <paramref name="Group"/> is the schema's group (top, mail, apps, bottom); <paramref name="Keywords"/> feed the settings search for pages whose rows are not in the schema.</summary>
public sealed record SettingsPageInfo(string Id, string Title, string Group, SettingsPageOwner Owner, bool Advanced, string Keywords);

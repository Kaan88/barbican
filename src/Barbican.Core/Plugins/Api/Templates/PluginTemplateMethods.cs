// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:322-327,2432-2436 (templates.list and get need templates:read; create, update and delete need templates:write).
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Settings.Templates;

namespace Barbican.Core.Plugins.Api.Templates;

/// <summary>Factory for the templates.* methods.</summary>
public static class PluginTemplateMethods
{
    public static IReadOnlyList<IPluginApiMethod> Create(ITemplateAdmin admin, IPluginMailRateLimiter limiter)
    {
        ArgumentNullException.ThrowIfNull(admin);
        ArgumentNullException.ThrowIfNull(limiter);
        return [new PluginTemplatesList(admin), new PluginTemplatesGet(admin), new PluginTemplatesCreate(admin, limiter), new PluginTemplatesUpdate(admin, limiter), new PluginTemplatesDelete(admin, limiter)];
    }
}

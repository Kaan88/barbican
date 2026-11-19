// Mirrors: Bulwark lib/plugin-sandbox/tier.ts; native full trust is a separate host policy, never a JavaScript sandbox.
namespace Barbican.Core.Plugins.Permissions;
public enum PluginExecutionTier { Untrusted, Privileged, NativeFullTrust }

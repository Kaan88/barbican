// Source: Bulwark stores/plugin-store.ts:467-478; authenticated immutable host snapshot is an M6b addition.
using System.Collections.Immutable;
namespace Barbican.Core.Plugins.Persistence;

/// <summary>Index bytes returned only after authentication by a trusted store adapter.</summary>
public sealed record PluginStoreSnapshot(long Revision, ImmutableArray<byte> Index);

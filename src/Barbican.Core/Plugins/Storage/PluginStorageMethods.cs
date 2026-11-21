// Source: Bulwark lib/plugin-sandbox/host-api.ts:134-137,407-449 and host-api-input.ts:25-30.
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Persistence;

/// <summary>Routes storage calls to the active authorized login captured for the invocation.</summary>
public static class PluginStorageMethods
{
    public static IReadOnlyList<IPluginApiMethod> For(PluginScopedStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        return
        [
            new StorageMethod(storage, Operation.Get, "storage.get"),
            new StorageMethod(storage, Operation.Set, "storage.set"),
            new StorageMethod(storage, Operation.Remove, "storage.remove"),
            new StorageMethod(storage, Operation.Keys, "storage.keys"),
        ];
    }

    private enum Operation { Get, Set, Remove, Keys }

    private sealed class StorageMethod(PluginScopedStorage storage, Operation operation, string name) : IPluginApiMethod
    {
        // One entry is at most PluginStorageSnapshot.Quota of JSON (host-api.ts:407-447), so its value
        // may hold a string of up to 2 * Quota bytes; storage.get keeps the 1 MiB the transport also applies.
        public PluginApiDescriptor Descriptor { get; } = new(name, [], TimeSpan.FromSeconds(10),
            mayMutate: operation is Operation.Set or Operation.Remove)
        {
            ArgumentLimits = operation == Operation.Set ? PluginStorageSnapshot.ValueLimits : null,
        };

        public PluginErrorCode? ValidateArguments(PluginValue args)
        {
            if (args.Kind != PluginValueKind.Array) return PluginErrorCode.InvalidArguments;
            if (operation == Operation.Keys) return args.Count == 0 ? null : PluginErrorCode.InvalidArguments;
            if (args.Count != (operation == Operation.Set ? 2 : 1) || args.Item(0).Kind != PluginValueKind.String)
                return PluginErrorCode.InvalidArguments;
            return operation == Operation.Set && args.Item(0).StringValue.Length is 0 or > 256
                ? PluginErrorCode.InvalidArguments : null;
        }

        public async Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue args, CancellationToken ct)
        {
            var captured = context.CaptureActive();
            if (!captured.IsOk || captured.Value is not { } capture) return PluginCallOutcome.Failed(PluginErrorCode.NoSession);
            if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);

            var binding = context.Binding;
            if (operation == Operation.Keys)
            {
                var keys = await storage.KeysAsync(binding, capture, ct).ConfigureAwait(false);
                if (!keys.IsOk) return Failed();
                var builder = new PluginValueBuilder();
                return PluginCallOutcome.Succeeded(builder.Build(builder.Array([.. keys.Value!.Select(builder.String)]), PluginStorageSnapshot.ValueLimits));
            }

            var key = args.Item(0).StringValue;
            if (operation == Operation.Get)
            {
                var result = await storage.GetAsync(binding, capture, key, ct).ConfigureAwait(false);
                if (!result.IsOk) return Failed();
                if (result.Value is { } value) return PluginCallOutcome.Succeeded(value);
                var builder = new PluginValueBuilder();
                return PluginCallOutcome.Succeeded(builder.Build(builder.Null()));
            }

            var saved = operation == Operation.Set
                ? await storage.SetAsync(binding, capture, key, args.Item(1), context.MarkMutationIssued, ct).ConfigureAwait(false)
                : await storage.RemoveAsync(binding, capture, key, context.MarkMutationIssued, ct).ConfigureAwait(false);
            return saved.IsOk ? PluginCallOutcome.Succeeded() : Failed();
        }

        private static PluginCallOutcome Failed() => PluginCallOutcome.Failed(PluginErrorCode.Failed);
    }
}

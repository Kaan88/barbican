// Mirrors: lib/plugin-sandbox/host-api-checker.ts:1-101 (requirePermission: compose:check, or compose:read for an on-demand checker, checked on every call;
// getCheckText and setAnnotations name a composer the plugin was handed and a checker it registered; 60 calls a minute; at most 2 * MAX_ANNOTATIONS pushed),
// lib/proofreading/annotations.ts:96-128 (validateAnnotations, done by PluginCheckRequest.Decode). No Stalwart counterpart.
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Composer;

/// <summary>composer.getCheckText and composer.setAnnotations: an on-demand checker reads the text of a composer it was handed and pushes its results back.</summary>
public static class PluginComposerCheckMethods
{
    private const int MaxPushed = 600;

    public static IPluginApiMethod GetCheckText(IPluginComposerAccess access, PluginCheckerRegistry registry, IPluginComposerEditRateLimiter limiter) =>
        new Method(access, registry, limiter, annotations: false);

    public static IPluginApiMethod SetAnnotations(IPluginComposerAccess access, PluginCheckerRegistry registry, IPluginComposerEditRateLimiter limiter) =>
        new Method(access, registry, limiter, annotations: true);

    private sealed class Method(IPluginComposerAccess access, PluginCheckerRegistry registry, IPluginComposerEditRateLimiter limiter, bool annotations) : IPluginApiMethod
    {
        public PluginApiDescriptor Descriptor { get; } = new(annotations ? "composer.setAnnotations" : "composer.getCheckText",
            [[PluginCheckerRegistry.TypingPermission], ["compose:read"]], TimeSpan.FromSeconds(30), mayMutate: annotations);

        public PluginErrorCode? ValidateArguments(PluginValue args)
        {
            var arity = annotations ? 4 : 2;
            if (args.Kind != PluginValueKind.Array || args.Count < arity || !Id(args.Item(0)) || !Id(args.Item(1))) return PluginErrorCode.InvalidArguments;
            if (!annotations) return null;
            var list = args.Item(3);
            return Id(args.Item(2)) && list.Kind == PluginValueKind.Array && list.Count <= MaxPushed ? null : PluginErrorCode.InvalidArguments;
        }

        public async Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue args, CancellationToken ct)
        {
            var binding = context.Binding;
            var composerId = args.Item(0).StringValue;
            if (registry.Lookup(binding, args.Item(1).StringValue) is not { } spec) return PluginCallOutcome.Failed(PluginErrorCode.InvalidArguments);
            var typing = context.Grant.Authorizes(binding.Package, binding.GrantGeneration, PluginCheckerRegistry.TypingPermission, null);
            if (!typing && !(spec.OnDemand && context.Grant.Authorizes(binding.Package, binding.GrantGeneration, "compose:read", null)))
                return PluginCallOutcome.Failed(PluginErrorCode.PermissionDenied);
            var handle = access.Resolve(binding, composerId);
            if (handle is null) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
            if (!context.CaptureOwner(handle.LocalAccountKey).IsOk) return PluginCallOutcome.Failed(PluginErrorCode.NoSession);
            if (!context.Demand(typing ? PluginCheckerRegistry.TypingPermission : "compose:read", handle.LocalAccountKey).IsOk ||
                !await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
            if (!limiter.TryAcquire(binding.Profile, binding.Package.Id)) return PluginCallOutcome.Failed(PluginErrorCode.RateLimited);
            var result = new PluginValueBuilder();
            if (!annotations)
            {
                var request = await handle.BeginCheckAsync(spec, ct).ConfigureAwait(false);
                if (request is null) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
                return await CurrentAsync(context, handle, composerId, ct).ConfigureAwait(false)
                    ? PluginCallOutcome.Succeeded(request.Request) : PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
            }
            context.MarkMutationIssued();
            var kept = await handle.SetAnnotationsAsync(spec, args.Item(2).StringValue, args.Item(3), ct).ConfigureAwait(false);
            return PluginCallOutcome.Succeeded(result.Build(result.Number(kept)));
        }

        private async ValueTask<bool> CurrentAsync(IPluginInvocationContext context, IPluginComposerHandle handle, string id, CancellationToken ct) =>
            await context.CheckCurrentAsync(ct).ConfigureAwait(false) && ReferenceEquals(access.Resolve(context.Binding, id), handle);

        private static bool Id(PluginValue value) => value.Kind == PluginValueKind.String && value.StringValue.Length is > 0 and <= 256;
    }
}

// Mirrors: Bulwark lib/plugin-sandbox/checkers.ts:53-224 and host-api-checker.ts:24-73. No Stalwart counterpart.
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Plugins.Api.Composer;

public sealed partial class PluginCheckerRegistry
{
    public const string TypingPermission = "compose:check";
    private readonly ConditionalWeakTable<PluginRuntimeBinding, Dictionary<string, Entry>> _registrations = new();
    private readonly Lock _gate = new();
    private readonly IPluginComposerEditRateLimiter _limiter;
    private long _order;

    public PluginCheckerRegistry(TimeProvider time)
    {
        _limiter = PluginComposerEditRateLimiter.Create(time, 128);
        Methods = [new Method(this, "registerChecker", 1), new Method(this, "updateChecker", 2),
            new Method(this, "recheck", 1), new Method(this, "unregisterChecker", 1)];
    }

    public event EventHandler? Changed;
    public IReadOnlyList<IPluginApiMethod> Methods { get; }

    public IReadOnlyList<PluginCheckerRegistration> Current()
    {
        lock (_gate)
            return [.. _registrations.Where(pair => pair.Key.IsAlive).SelectMany(pair => pair.Value.Values)
                .OrderBy(entry => entry.Order).Select(entry => entry.Spec)];
    }

    public bool Contains(PluginCheckerRegistration spec)
        => TryRunWhileRegistered(spec, static () => true);

    public bool TryRunWhileRegistered(PluginCheckerRegistration spec, Func<bool> issue)
    {
        lock (_gate) return spec.Owner.IsAlive && ReferenceEquals(Find(spec.Owner, spec.Id)?.Spec, spec) && issue();
    }

    private Entry? Find(PluginRuntimeBinding binding, string id) =>
        _registrations.TryGetValue(binding, out var entries) ? entries.GetValueOrDefault(id) : null;

    private static bool Permitted(IPluginInvocationContext context, PluginCheckerRegistration spec) =>
        context.Grant.Authorizes(spec.Owner.Package, spec.Owner.GrantGeneration, TypingPermission, null) ||
        (spec.OnDemand && context.Grant.Authorizes(spec.Owner.Package, spec.Owner.GrantGeneration, "compose:read", null));

    private async Task<PluginCallOutcome> ChangeAsync(string name, IPluginInvocationContext context, PluginValue args, CancellationToken ct)
    {
        var binding = context.Binding;
        if (binding.Kind != PluginFrameKind.Background) return PluginCallOutcome.Failed(PluginErrorCode.PermissionDenied);
        var registration = string.Equals(name, "registerChecker", StringComparison.Ordinal);
        var first = args.Item(0);
        var idValue = registration ? first.TryGetProperty("id", out var field) ? field : null : first;
        if (idValue is not { Kind: PluginValueKind.String, StringValue: { } id } || !IdPattern().IsMatch(id))
            return PluginCallOutcome.Failed(PluginErrorCode.InvalidArguments);
        if (!_limiter.TryAcquire(binding.Profile, binding.Package.Id)) return PluginCallOutcome.Failed(PluginErrorCode.RateLimited);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
        var error = PluginErrorCode.StaleContext;
        var changed = false;
        binding.TryWriteWhileAlive(() =>
        {
            lock (_gate)
            {
                if (ct.IsCancellationRequested) return;
                var old = Find(binding, id);
                var seed = new PluginCheckerRegistration(binding, id, string.Empty, [], TimeSpan.FromMilliseconds(800), [], false, false);
                var spec = name switch
                {
                    "registerChecker" => ReadDefinition(seed, args.Item(0), patch: false),
                    "updateChecker" when old is not null => ReadDefinition(old.Spec, args.Item(1), patch: true),
                    "recheck" or "unregisterChecker" when old is not null => old.Spec with { },
                    _ => null,
                };
                if (spec is null) { error = PluginErrorCode.InvalidArguments; return; }
                if (!Permitted(context, spec)) { error = PluginErrorCode.PermissionDenied; return; }
                var entries = _registrations.GetValue(binding, _ => new(StringComparer.Ordinal));
                if (old is null && entries.Count >= 4) { error = PluginErrorCode.InvalidArguments; return; }
                if (string.Equals(name, "unregisterChecker", StringComparison.Ordinal)) entries.Remove(id);
                else entries[id] = new(spec, old?.Order ?? _order++);
                changed = true;
            }
        });
        if (!changed) return PluginCallOutcome.Failed(error);
        Changed?.Invoke(this, EventArgs.Empty);
        var result = new PluginValueBuilder();
        return PluginCallOutcome.Succeeded(result.Build(result.Boolean(true)));
    }

    private static PluginCheckerRegistration? ReadDefinition(PluginCheckerRegistration spec, PluginValue data, bool patch)
    {
        if (data.Kind != PluginValueKind.Object) return null;
        foreach (var name in data.PropertyNames)
        {
            var value = data.Property(name);
            switch (name)
            {
                case "id" when !patch: break;
                case "label" when value is { Kind: PluginValueKind.String, StringValue.Length: > 0 and <= 60 } &&
                    !string.IsNullOrWhiteSpace(value.StringValue) && PluginCheckRequest.IsSafeText(value.StringValue):
                    spec = spec with { Label = value.StringValue.Trim() };
                    break;
                case "kinds" when ReadStrings(value, 32, IsKind) is { Length: > 0 } kinds:
                    spec = spec with { Kinds = kinds };
                    break;
                case "languages" when !patch && ReadStrings(value, 100, LanguagePattern().IsMatch) is { } languages:
                    spec = spec with { Languages = languages };
                    break;
                case "mode" when !patch && value is { Kind: PluginValueKind.String, StringValue: "typing" or "on-demand" }:
                    spec = spec with { OnDemand = string.Equals(value.StringValue, "on-demand", StringComparison.Ordinal) };
                    break;
                case "debounceMs" when value.Kind == PluginValueKind.Number &&
                    double.TryParse(value.NumberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var delay) && double.IsFinite(delay):
                    spec = spec with { Debounce = TimeSpan.FromMilliseconds(Math.Clamp(Math.Round(delay, MidpointRounding.AwayFromZero), 300, 5000)) };
                    break;
                case "warnBeforeSend" when value.Kind == PluginValueKind.Boolean:
                    spec = spec with { WarnBeforeSend = value.BooleanValue };
                    break;
                default: return null;
            }
        }
        return spec.Label.Length > 0 && !spec.Kinds.IsEmpty ? spec : null;
    }

    private static ImmutableArray<string>? ReadStrings(PluginValue value, int limit, Func<string, bool> valid)
    {
        if (value.Kind != PluginValueKind.Array || value.Count > limit) return null;
        var list = ImmutableArray.CreateBuilder<string>();
        for (var index = 0; index < value.Count; index++)
        {
            if (value.Item(index) is not { Kind: PluginValueKind.String, StringValue: { } item } || !valid(item))
                return null;
            if (!list.Contains(item, StringComparer.Ordinal)) list.Add(item);
        }
        return list.ToImmutable();
    }

    private static bool IsKind(string value) => value is "spelling" or "grammar" or "style" or "other";

    [GeneratedRegex(@"\A[a-z0-9][a-z0-9-]{0,39}\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"\A[A-Za-z]{2,3}(-[A-Za-z0-9]{1,8}){0,3}\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking | RegexOptions.ExplicitCapture)]
    private static partial Regex LanguagePattern();

    private sealed record Entry(PluginCheckerRegistration Spec, long Order);

    private sealed class Method(PluginCheckerRegistry registry, string name, int arity) : IPluginApiMethod
    {
        public PluginApiDescriptor Descriptor { get; } = new("composer." + name, [], TimeSpan.FromSeconds(5));
        public PluginErrorCode? ValidateArguments(PluginValue args) =>
            args.Kind == PluginValueKind.Array && args.Count == arity ? null : PluginErrorCode.InvalidArguments;

        public Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue arguments, CancellationToken ct) =>
            registry.ChangeAsync(name, context, arguments, ct);
    }
}

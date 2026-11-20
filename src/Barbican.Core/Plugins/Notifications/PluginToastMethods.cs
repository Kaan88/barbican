// Mirrors Bulwark lib/plugin-sandbox/host-api-toasts.ts and host-api-notify.ts oneLine.
using System.Text;
using System.Globalization;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Notifications;

/// <summary>Ordinary add-on toasts with a shared per-package burst limit.</summary>
public static class PluginToastMethods
{
    /// <summary>Creates the four methods sharing one limiter and the host's UI presenter.</summary>
    public static IReadOnlyList<IPluginApiMethod> Create(TimeProvider time,
        Func<PluginToast, IPluginInvocationContext, CancellationToken, Task<PluginErrorCode?>> present)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(present);
        var limiter = new Limiter(time);
        return [new Method("toast.success", PluginToastKind.Success, limiter, present),
            new Method("toast.error", PluginToastKind.Error, limiter, present),
            new Method("toast.info", PluginToastKind.Info, limiter, present),
            new Method("toast.warning", PluginToastKind.Warning, limiter, present)];
    }

    /// <summary>Sanitizes a trusted host-provided source label; the host supplies its own ID fallback.</summary>
    public static string SourceLabel(string? displayName, string pluginId)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        var source = Clean(displayName ?? string.Empty, 60, int.MaxValue);
        return source.Length > 0 ? source : Clean(pluginId, 60, int.MaxValue);
    }

    private static string Clean(string text, int max, int inputLimit)
    {
        var output = new StringBuilder(Math.Min(text.Length, inputLimit));
        var controls = false;
        var length = Math.Min(text.Length, inputLimit);
        for (var index = 0; index < length; index++)
        {
            var c = text[index];
            if (c is <= '\u001f' or >= '\u007f' and <= '\u009f' or '\u2028' or '\u2029') { controls = true; continue; }
            if (controls) { output.Append(' '); controls = false; }
            if (c is '\u061c' or '\u200e' or '\u200f' or >= '\u202a' and <= '\u202e' or >= '\u2066' and <= '\u2069') continue;
            if (char.IsHighSurrogate(c))
            {
                if (index + 1 < length && char.IsLowSurrogate(text[index + 1])) { output.Append(c).Append(text[++index]); continue; }
                output.Append('\ufffd');
            }
            else output.Append(char.IsLowSurrogate(c) ? '\ufffd' : c);
        }
        if (controls) output.Append(' ');
        var cleaned = output.ToString().Trim();
        if (cleaned.Length <= max) return cleaned;
        var end = max - 1;
        if (char.IsHighSurrogate(cleaned[end - 1])) end--;
        return cleaned[..end] + "…";
    }

    private sealed class Method(string name, PluginToastKind kind, Limiter limiter,
        Func<PluginToast, IPluginInvocationContext, CancellationToken, Task<PluginErrorCode?>> present) : IPluginApiMethod
    {
        public PluginApiDescriptor Descriptor { get; } = new(name, [], TimeSpan.FromSeconds(30), mayMutate: true);
        public PluginErrorCode? ValidateArguments(PluginValue arguments) => Read(arguments) is null ? PluginErrorCode.InvalidArguments : null;

        public async Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue arguments, CancellationToken ct)
        {
            if (Read(arguments) is not { } message) return PluginCallOutcome.Failed(PluginErrorCode.InvalidArguments);
            if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
            if (!limiter.Take(context.Binding.Profile, context.Binding.Package.Id)) return PluginCallOutcome.Succeeded();
            var error = await present(new PluginToast(kind, Clean(message, 300, 1200)), context, ct).ConfigureAwait(false);
            return error is { } denied ? PluginCallOutcome.Failed(denied) : PluginCallOutcome.Succeeded();
        }

        private static string? Read(PluginValue args)
        {
            if (args is not { Kind: PluginValueKind.Array }) return null;
            if (args.Count == 0) return string.Empty;
            var item = args.Item(0);
            return item.Kind switch
            {
                PluginValueKind.String => item.StringValue,
                PluginValueKind.Null or PluginValueKind.Undefined => string.Empty,
                PluginValueKind.Boolean => item.BooleanValue ? "true" : "false",
                PluginValueKind.Number => Number(item.NumberText),
                PluginValueKind.BigInt => item.NumberText,
                _ => null,
            };
        }
    }

    // Uses the existing shortest-roundtrip normalization strategy from PluginStorageJson.
    private static string? Number(string text)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return null;
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";
        if (value == 0) return "0";
        var raw = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture).ToLowerInvariant();
        var parts = raw.Split('e');
        var exponent = parts.Length == 2 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
        var dot = parts[0].IndexOf('.', StringComparison.Ordinal);
        var position = (dot < 0 ? parts[0].Length : dot) + exponent;
        var digits = parts[0].Replace(".", "", StringComparison.Ordinal);
        var sign = value < 0 ? "-" : "";
        if (position > 0 && position <= 21)
            return sign + (position >= digits.Length ? digits.PadRight(position, '0') : digits.Insert(position, "."));
        if (position <= 0 && position > -6) return sign + "0." + new string('0', -position) + digits;
        var finalExponent = position - 1;
        return sign + digits[0] + (digits.Length > 1 ? "." + digits[1..] : "") + "e" +
            (finalExponent >= 0 ? "+" : "") + finalExponent.ToString(CultureInfo.InvariantCulture);
    }

    private sealed class Limiter(TimeProvider time)
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<(string Profile, string Plugin), Queue<long>> _recent = [];
        internal bool Take(string profile, string plugin)
        {
            lock (_gate)
            {
                var now = time.GetTimestamp();
                foreach (var (key, stamps) in _recent.ToArray())
                {
                    while (stamps.TryPeek(out var start) && time.GetElapsedTime(start, now) >= TimeSpan.FromSeconds(10)) _ = stamps.Dequeue();
                    if (stamps.Count == 0) _recent.Remove(key);
                }
                var owner = (profile, plugin);
                if (!_recent.TryGetValue(owner, out var queue))
                {
                    if (_recent.Count >= 256) return false;
                    _recent.Add(owner, queue = new Queue<long>());
                }
                if (queue.Count >= 5) return false;
                queue.Enqueue(now);
                return true;
            }
        }
    }
}

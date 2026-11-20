// Mirrors Bulwark lib/plugin-sandbox/runtime.tsx:653-663 and host-api.ts:883-961.
using System.Globalization;
using Barbican.Core.Compose;
using Barbican.Core.Compose.Recipients;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.RawSend;

/// <summary>Typed SDK adapters for raw message submission.</summary>
public static class PluginRawSendMethods
{
    public static IPluginApiMethod SendRaw(IPluginRawSendHost host) => new RawSendMethod(host, false);
    public static IPluginApiMethod SubmitRaw(IPluginRawSendHost host) => new RawSendMethod(host, true);

    private sealed class RawSendMethod(IPluginRawSendHost host, bool submitOnly) : IPluginApiMethod
    {
        private const int MaxBytes = 25 * 1024 * 1024;
        public PluginApiDescriptor Descriptor { get; } = new(submitOnly ? "jmap.submitRaw" : "jmap.sendRaw", [["email:raw-send"]], TimeSpan.FromMinutes(2), mayMutate: true);
        public PluginErrorCode? ValidateArguments(PluginValue arguments) => Parse(arguments, submitOnly) is null ? PluginErrorCode.InvalidArguments : null;
        public async Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue arguments, CancellationToken ct)
        {
            var parsed = Parse(arguments, submitOnly);
            if (parsed is null) return PluginCallOutcome.Failed(PluginErrorCode.InvalidArguments);
            var result = await host.SendAsync(context, parsed, ct).ConfigureAwait(false);
            if (result.Receipt is null || result.Error is not null)
                return PluginCallOutcome.Failed(result.Error ?? PluginErrorCode.Failed);
            return Project(result.Receipt);
        }
        private static PluginRawSendRequest? Parse(PluginValue args, bool submitOnly)
        {
            if (args.Kind != PluginValueKind.Array || args.Count is < 2 or > 3) return null;
            var data = args.Item(0);
            if (data.Kind is not (PluginValueKind.ArrayBuffer or PluginValueKind.View) || data.ByteLength is <= 0 or > MaxBytes) return null;
            var identity = args.Item(1);
            if (identity.Kind != PluginValueKind.String || string.IsNullOrWhiteSpace(identity.StringValue) || identity.StringValue.Length > 256 || identity.StringValue.Any(char.IsControl)) return null;
            if (args.Count < 3 || args.Item(2).Kind is PluginValueKind.Undefined or PluginValueKind.Null)
                return new(data, identity.StringValue, null, null, null, submitOnly);
            if (args.Item(2).Kind != PluginValueKind.Object) return null;
            var options = args.Item(2);
            if (options.PropertyNames.Any(name => name is not ("delayedUntil" or "envelopeRecipients" or "labelToken"))) return null;
            if (!TryRecipients(options, out var recipients) || !TrySchedule(options, out var scheduled) || !TryLabel(options, out var labelToken)) return null;
            return new(data, identity.StringValue, recipients, scheduled, labelToken, submitOnly);
        }
        private static bool TryRecipients(PluginValue options, out IReadOnlyList<string>? recipients)
        {
            recipients = null;
            if (!options.PropertyNames.Contains("envelopeRecipients", StringComparer.Ordinal)) return true;
            var addresses = options.Property("envelopeRecipients");
            if (addresses.Kind == PluginValueKind.Undefined) return true;
            if (addresses.Kind != PluginValueKind.Array || addresses.Count > 200) return false;
            if (addresses.Count == 0) return true;
            var list = new List<string>(addresses.Count);
            for (var i = 0; i < addresses.Count; i++)
            {
                var address = addresses.Item(i);
                if (address.Kind != PluginValueKind.String || address.StringValue.Length > 320 || !RecipientText.IsValidEmail(address.StringValue) || address.StringValue.Any(char.IsControl)) return false;
                list.Add(address.StringValue);
            }
            recipients = list.AsReadOnly();
            return true;
        }
        private static bool TrySchedule(PluginValue options, out DateTimeOffset? scheduled)
        {
            scheduled = null;
            if (!options.PropertyNames.Contains("delayedUntil", StringComparer.Ordinal)) return true;
            var date = options.Property("delayedUntil");
            if (date.Kind == PluginValueKind.Undefined) return true;
            if (date.Kind != PluginValueKind.String || date.StringValue.Length is < 20 or > 40 ||
                !DateTimeOffset.TryParse(date.StringValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ||
                !(date.StringValue.EndsWith("Z", StringComparison.OrdinalIgnoreCase) || HasOffset(date.StringValue))) return false;
            scheduled = parsed.ToUniversalTime();
            return true;
        }
        private static bool TryLabel(PluginValue options, out string? labelToken)
        {
            labelToken = null;
            if (!options.PropertyNames.Contains("labelToken", StringComparer.Ordinal)) return true;
            var label = options.Property("labelToken");
            if (label.Kind == PluginValueKind.Undefined) return true;
            if (label.Kind != PluginValueKind.String || label.StringValue.Length > 256 || label.StringValue.Any(char.IsControl)) return false;
            labelToken = label.StringValue;
            return true;
        }
        private static bool HasOffset(string value) => value.Length >= 6 && value[^6] is '+' or '-' && value[^3] == ':' &&
            char.IsAsciiDigit(value[^5]) && char.IsAsciiDigit(value[^4]) && char.IsAsciiDigit(value[^2]) && char.IsAsciiDigit(value[^1]);
        private static PluginCallOutcome Project(RawMessageReceipt receipt)
        {
            var builder = new PluginValueBuilder();
            var fields = new List<(string, int)> { ("scheduled", builder.Boolean(receipt.Scheduled)), ("isSmime", builder.Boolean(true)),
                ("emailSubmissionId", builder.String(receipt.SubmissionId)), ("submissionAccountId", builder.String(receipt.JmapAccountId)) };
            if (receipt.EmailId is not null)
            {
                fields.Add(("emailId", builder.String(receipt.EmailId)));
                fields.Add(("emailAccountId", builder.String(receipt.JmapAccountId)));
            }
            if (receipt.ReleaseAt is { } at) fields.Add(("sendAt", builder.String(at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))));
            if (receipt.RejectedRecipients.Count > 0)
                fields.Add(("rejectedRecipients", builder.Array([.. receipt.RejectedRecipients.Select(item => builder.Object(("email", builder.String(item.Email)), ("smtpReply", builder.String(item.SmtpReply))))])));
            return PluginCallOutcome.Succeeded(builder.Build(builder.Object([.. fields])));
        }
    }
}

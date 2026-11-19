// Mirrors: lib/plugin-sandbox/host-api-composer.ts:99-143; host-api-input.ts:15-30,57-65.
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
namespace Barbican.Core.Plugins.Api.Composer;
/// <summary>Production adapters for the three source composer operations.</summary>
public static class PluginComposerMethods
{
    public static IPluginApiMethod GetSnapshot(IPluginComposerAccess access) => new ComposerMethod(access, "composer.getSnapshot", "compose:read", 1);
    public static IPluginApiMethod SetSubject(IPluginComposerAccess access) => new ComposerMethod(access, "composer.setSubject", "compose:write", 2, true);
    public static IPluginApiMethod ReadAttachment(IPluginComposerAccess access) => new ComposerMethod(access, "composer.readAttachment", "email:blob-read", 2);

    private sealed class ComposerMethod : IPluginApiMethod
    {
        private const int MaxBytes = 25 * 1024 * 1024;
        private readonly IPluginComposerAccess _access;
        private readonly int _arguments;
        public ComposerMethod(IPluginComposerAccess access, string name, string permission, int arguments, bool writes = false)
        {
            _access = access ?? throw new ArgumentNullException(nameof(access)); _arguments = arguments;
            Descriptor = new(name, [[permission]], TimeSpan.FromSeconds(30), mayMutate: writes);
        }
        public PluginApiDescriptor Descriptor { get; }
        public PluginErrorCode? ValidateArguments(PluginValue args)
        {
            if (args.Kind != PluginValueKind.Array || args.Count < _arguments || !ValidId(args.Item(0))) return PluginErrorCode.InvalidArguments;
            if (_arguments == 1) return null;
            var value = args.Item(1);
            if (string.Equals(Descriptor.Name, "composer.readAttachment", StringComparison.Ordinal)) return ValidId(value) ? null : PluginErrorCode.InvalidArguments;
            if (value.Kind != PluginValueKind.String || value.StringValue.Length > 998 * 4 || CleanSubject(value.StringValue).Length > 998) return PluginErrorCode.InvalidArguments;
            return null;
        }
        public async Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue args, CancellationToken ct)
        {
            var id = args.Item(0).StringValue;
            var handle = _access.Resolve(context.Binding, id);
            if (handle is null) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
            if (!context.CaptureOwner(handle.LocalAccountKey).IsOk) return PluginCallOutcome.Failed(PluginErrorCode.NoSession);
            if (!context.Demand(Descriptor.PermissionAlternatives[0][0], handle.LocalAccountKey).IsOk || !await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
            var snapshot = await handle.GetSnapshotAsync(ct).ConfigureAwait(false);
            if (!await CurrentAsync(context, handle, id, ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
            if (string.Equals(Descriptor.Name, "composer.getSnapshot", StringComparison.Ordinal)) return Snapshot(snapshot);
            if (string.Equals(Descriptor.Name, "composer.setSubject", StringComparison.Ordinal))
            {
                context.MarkMutationIssued();
                var changed = await handle.SetSubjectAsync(CleanSubject(args.Item(1).StringValue), snapshot.Revision, ct).ConfigureAwait(false);
                return Boolean(changed);
            }
            var attachment = snapshot.Attachments.FirstOrDefault(x => string.Equals(x.Id, args.Item(1).StringValue, StringComparison.Ordinal));
            if (attachment is null) return PluginCallOutcome.Failed(PluginErrorCode.AttachmentNotInComposer);
            if (attachment.Size < 0 || attachment.Size > MaxBytes) return PluginCallOutcome.Failed(PluginErrorCode.InvalidArguments);
            var data = await handle.ReadAttachmentAsync(attachment.Id, ct).ConfigureAwait(false);
            if (data.Data.Length > MaxBytes) return PluginCallOutcome.Failed(PluginErrorCode.InvalidArguments);
            if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
            if (!ReferenceEquals(_access.Resolve(context.Binding, id), handle)) return PluginCallOutcome.Failed(PluginErrorCode.ComposerChanged);
            var after = await handle.GetSnapshotAsync(ct).ConfigureAwait(false);
            if (!string.Equals(after.Revision, snapshot.Revision, StringComparison.Ordinal) || !after.Attachments.Any(x => string.Equals(x.Id, attachment.Id, StringComparison.Ordinal)) || !await CurrentAsync(context, handle, id, ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.ComposerChanged);
            return Attachment(attachment, data);
        }
        private async ValueTask<bool> CurrentAsync(IPluginInvocationContext context, IPluginComposerHandle handle, string id, CancellationToken ct) =>
            await context.CheckCurrentAsync(ct).ConfigureAwait(false) && ReferenceEquals(_access.Resolve(context.Binding, id), handle);
        private static bool ValidId(PluginValue value) => value.Kind == PluginValueKind.String && value.StringValue.Length is > 0 and <= 256;
        private static string CleanSubject(string subject)
        {
            var output = new System.Text.StringBuilder(); var controls = false;
            foreach (var c in subject)
            {
                if (c <= '\u001f' || c is >= '\u007f' and <= '\u009f') { if (!controls) output.Append(' '); controls = true; }
                else { controls = false; if (c is not ('\u200e' or '\u200f' or >= '\u202a' and <= '\u202e' or >= '\u2066' and <= '\u2069')) output.Append(c); }
            }
            return output.ToString().Trim();
        }
        private static PluginCallOutcome Boolean(bool value) { var b = new PluginValueBuilder(); return PluginCallOutcome.Succeeded(b.Build(b.Boolean(value))); }
        private static PluginCallOutcome Snapshot(PluginComposerSnapshot snapshot)
        {
            var b = new PluginValueBuilder();
            int List(IEnumerable<string> values) => b.Array([.. values.Select(b.String)]);
            var files = b.Array([.. snapshot.Attachments.Select(x => b.Object(("id", b.String(x.Id)), ("name", b.String(x.Name)), ("type", b.String(x.Type)), ("size", b.Number(x.Size))))]);
            return PluginCallOutcome.Succeeded(b.Build(b.Object(("revision", b.String(snapshot.Revision)), ("subject", b.String(snapshot.Subject)), ("text", b.String(snapshot.Text)), ("to", List(snapshot.To)), ("cc", List(snapshot.Cc)), ("bcc", List(snapshot.Bcc)), ("attachments", files), ("pending", b.Boolean(snapshot.Pending)))));
        }
        private static PluginCallOutcome Attachment(PluginComposerAttachment file, PluginComposerAttachmentData data)
        {
            var bytes = data.Data; var b = new PluginValueBuilder();
            var root = b.Object(("name", b.String(file.Name)), ("type", b.String(file.Type)), ("size", b.Number(bytes.Length)), ("data", b.Buffer(bytes.Span)));
            return PluginCallOutcome.Succeeded(b.Build(root, new PluginValueLimits(MaxWireBytes: 40 * 1024 * 1024, MaxBinaryBytes: MaxBytes)));
        }
    }
}

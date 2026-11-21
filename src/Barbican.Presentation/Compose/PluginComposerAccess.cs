using System.Runtime.CompilerServices;
using Barbican.Core.Compose;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Storage;
using Barbican.Core.Plugins.Api.Composer;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Runtime;
using Barbican.Presentation.Common;

namespace Barbican.Presentation.Compose;

/// <summary>Grants a runtime access to the open composer when a host action supplies its ID.</summary>
public sealed class PluginComposerAccess : IPluginComposerAccess, IDisposable
{
    private readonly IComposerService _composer;
    private readonly IDispatcher _dispatcher;
    private readonly IPluginSessionSource _sessions;
    private readonly Lock _gate = new();
    private readonly ConditionalWeakTable<PluginRuntimeBinding, Handle> _exposures = new();

    private readonly PluginAnnotationStore? _annotations;
    private readonly IPrefsStore? _prefs;

    public PluginComposerAccess(IComposerService composer, IDispatcher dispatcher, IPluginSessionSource sessions,
        PluginAnnotationStore? annotations = null, IPrefsStore? prefs = null)
    {
        _composer = composer;
        _dispatcher = dispatcher;
        _sessions = sessions;
        _annotations = annotations;
        _prefs = prefs;
        _composer.OpenChanged += OnOpenChanged;
    }

    public bool Expose(PluginRuntimeBinding binding, string id, string ownerKey)
    {
        var captured = _sessions.CaptureOwner(ownerKey);
        return captured.IsOk && captured.Value is { } owner &&
            string.Equals(owner.LocalAccountKey, ownerKey, StringComparison.Ordinal) && Expose(binding, id, owner);
    }

    public bool Expose(PluginRuntimeBinding binding, string id, PluginSessionCapture owner)
    {
        if (!binding.IsAlive || !_sessions.IsCurrent(owner) || _composer.Current is not { } current ||
            !string.Equals(current.Id, id, StringComparison.Ordinal) ||
            !string.Equals(current.AccountKey, owner.LocalAccountKey, StringComparison.Ordinal)) return false;
        lock (_gate)
        {
            if (!binding.IsAlive || !ReferenceEquals(_composer.Current, current)) return false;
            if (!_exposures.TryGetValue(binding, out var old) || !old.Matches(current, owner))
            {
                _exposures.Remove(binding);
                _exposures.Add(binding, new Handle(_composer, current, binding, owner, _dispatcher, _sessions, _annotations, _prefs));
            }
        }
        return binding.IsAlive && ReferenceEquals(_composer.Current, current);
    }

    public IPluginComposerHandle? Resolve(PluginRuntimeBinding binding, string composerId)
    {
        lock (_gate)
        {
            return binding.IsAlive && _exposures.TryGetValue(binding, out var handle) &&
                string.Equals(composerId, handle.Id, StringComparison.Ordinal) && ReferenceEquals(_composer.Current, handle.Draft)
                ? handle : null;
        }
    }

    private void OnOpenChanged(object? sender, EventArgs args)
    {
        lock (_gate)
        {
            if (_annotations is not null) foreach (var pair in _exposures) _annotations.Forget(pair.Value.Id);
            _exposures.Clear();
        }
    }

    public void Dispose()
    {
        _composer.OpenChanged -= OnOpenChanged;
        lock (_gate) _exposures.Clear();
    }

    private sealed class Handle(IComposerService composer, ComposerViewModel draft, PluginRuntimeBinding binding,
        PluginSessionCapture owner, IDispatcher dispatcher, IPluginSessionSource sessions, PluginAnnotationStore? annotations, IPrefsStore? prefs) : IPluginComposerHandle
    {
        private readonly Dictionary<string, PluginCheckRequest> _checks = new(StringComparer.Ordinal);
        internal ComposerViewModel Draft => draft;
        internal string Id => draft.Id;
        public string LocalAccountKey => draft.AccountKey;
        internal bool Matches(ComposerViewModel candidate, PluginSessionCapture capture) =>
            ReferenceEquals(draft, candidate) && ReferenceEquals(owner.Session, capture.Session);

        public Task<PluginComposerSnapshot> GetSnapshotAsync(CancellationToken ct) => OnUiAsync(() =>
        {
            DemandCurrent();
            return draft.PluginSnapshot();
        }, ct);

        public Task<bool> SetSubjectAsync(string subject, string hostRevision, CancellationToken ct) => OnUiAsync(() =>
        {
            if (!IsOpen() || draft.IsSending || !string.Equals(hostRevision, draft.PluginRevision, StringComparison.Ordinal)) return false;
            return sessions.TryRunWhileCurrent([owner], () =>
                !ct.IsCancellationRequested && binding.TryWriteWhileAlive(() => draft.Subject = subject));
        }, ct);

        public async Task<PluginComposerAttachmentData> ReadAttachmentAsync(string attachmentId, CancellationToken ct)
        {
            var resource = await OnUiAsync(() =>
            {
                DemandCurrent();
                return draft.PluginAttachment(attachmentId)
                    ?? throw new InvalidOperationException("The attachment is unavailable.");
            }, ct).ConfigureAwait(false);
            const int limit = 25 * 1024 * 1024;
            if (resource.Size is < 0 or > limit || resource.BlobId is not { } blob ||
                !sessions.IsCurrent(owner) || !binding.IsAlive)
                throw new InvalidOperationException("The attachment is unavailable.");
            var client = owner.Session.Client;
            var opened = await client.DownloadAsync(client.Session.MailAccountId, blob, resource.Name, resource.Type, ct).ConfigureAwait(false);
            if (!opened.IsOk || opened.Value is not { } stream) throw new InvalidOperationException("The attachment is unavailable.");
            await using (stream.ConfigureAwait(false))
            {
                var bytes = await ReadBoundedAsync(stream, limit, ct).ConfigureAwait(false);
                _ = await OnUiAsync(() =>
                {
                    DemandCurrent();
                    return ReferenceEquals(draft.PluginAttachment(attachmentId), resource)
                        ? true : throw new InvalidOperationException("The attachment changed.");
                }, ct).ConfigureAwait(false);
                return new(resource.Name, resource.Type, bytes);
            }
        }

        public Task<PluginComposerBody> ReadBodyAsync(CancellationToken ct) => OnUiAsync(() =>
        {
            DemandCurrent();
            return draft.PluginBody();
        }, ct);

        public Task<PluginComposerSelection> ReadSelectionAsync(CancellationToken ct) => OnUiAsync(() =>
        {
            DemandCurrent();
            return draft.PluginSelection();
        }, ct);

        public Task<bool> ReplaceBodyAsync(PluginComposerContent content, string hostRevision, CancellationToken ct) => WriteAsync(() =>
            draft.PluginReplaceBody(content, hostRevision), ct);

        public Task<bool> ReplaceSelectionAsync(PluginComposerContent content, bool below, string hostRevision, CancellationToken ct) => WriteAsync(() =>
            draft.PluginReplaceSelection(content, below, hostRevision), ct);

        public Task<bool> AttachAsync(PluginComposerBlob blob, string hostRevision, CancellationToken ct) => WriteAsync(() =>
        {
            // The uploader's gates, for a blob that is already on the server: one file within maxSizeUpload, all files within maxSizeAttachmentsPerEmail.
            if (!PluginAttachGate.Allows(owner.Session.Client.Session, draft.PluginAttachmentTotal(), draft.PluginAttachmentCount, blob.Size)) return false;
            return draft.PluginAttach(new DraftAttachment { Name = blob.Name, Type = blob.Type, Size = blob.Size, BlobId = blob.BlobId }, hostRevision);
        }, ct);

        public Task<PluginCheckRequest?> BeginCheckAsync(PluginCheckerRegistration spec, CancellationToken ct) => OnUiAsync(() =>
        {
            DemandCurrent();
            if (!draft.PluginCheckable) return null;
            var language = prefs is null ? "en" : PluginCheckLanguage.Resolve(prefs.Current);
            var request = PluginCheckRequest.Create(draft.PluginCheckDoc, draft.Id, language, draft.PluginEditorVersion);
            if (request is null) return null;
            _checks[spec.Id] = request;
            return request;
        }, ct);

        public Task<int> SetAnnotationsAsync(PluginCheckerRegistration spec, string revision, PluginValue values, CancellationToken ct) => OnUiAsync(() =>
        {
            DemandCurrent();
            if (annotations is null || !_checks.TryGetValue(spec.Id, out var request) ||
                !string.Equals(request.Revision, revision, StringComparison.Ordinal) || request.DocVersion != draft.PluginEditorVersion) return 0;
            var issues = request.Decode(values, spec.Kinds);
            var kept = 0;
            _ = sessions.TryRunWhileCurrent([owner], () => !ct.IsCancellationRequested && binding.TryWriteWhileAlive(() =>
            {
                annotations.Set(draft.Id, spec, request.DocVersion, issues);
                kept = issues.Count;
            }));
            return kept;
        }, ct);

        private Task<bool> WriteAsync(Func<bool> edit, CancellationToken ct) => OnUiAsync(() =>
        {
            if (!IsOpen()) return false;
            var done = false;
            return sessions.TryRunWhileCurrent([owner], () => !ct.IsCancellationRequested && binding.TryWriteWhileAlive(() => done = edit())) && done;
        }, ct);

        private static async Task<byte[]> ReadBoundedAsync(Stream stream, int limit, CancellationToken ct)
        {
            using var data = new MemoryStream();
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
                if (count == 0) return data.ToArray();
                if (data.Length + count > limit) throw new InvalidOperationException("The attachment exceeds the read limit.");
                await data.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
            }
        }

        private bool IsOpen() => binding.IsAlive && ReferenceEquals(composer.Current, draft) && !draft.IsClosed;

        private void DemandCurrent()
        {
            if (!IsOpen() || !sessions.IsCurrent(owner)) throw new InvalidOperationException("The composer changed.");
        }

        private async Task<T> OnUiAsync<T>(Func<T> action, CancellationToken ct)
        {
            T result = default!;
            await dispatcher.InvokeAsync(() =>
            {
                ct.ThrowIfCancellationRequested();
                result = action();
                return Task.CompletedTask;
            }).WaitAsync(ct).ConfigureAwait(false);
            return result;
        }
    }
}

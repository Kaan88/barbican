#if AUTOMATION_WRITES
// L-040: bound to the client captured by WriteTestGuard. RFC 9661 state and RFC 8621 Inbox filters.
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Filters.Rules;
using Barbican.Core.Jmap.Transport;
using Barbican.Core.Mail.Write;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Sieve;

namespace Barbican.App.MailBody.Automation;

internal sealed partial class FilterVerificationBackend : IFilterWriteBackend
{
    private readonly Func<IAccountSession?> _authorized;
    private readonly IAccountSession _session;
    private readonly JmapClient _client;
    private readonly FilterService _filters;
    private readonly IMailWriter _writer;
    private readonly string _mail;
    private readonly string _sieve;
    private readonly TimeProvider _time;
    private readonly IHookBus _hooks;

    public FilterVerificationBackend(WriteTestGuard guard, IAccountSession session, IHookBus hooks, IMailWriterFactory writers, TimeProvider time)
        : this(() => guard.Check().Session, session, hooks, writers, time)
    {
    }

    internal FilterVerificationBackend(Func<IAccountSession?> authorized, IAccountSession session, IHookBus hooks, IMailWriterFactory writers, TimeProvider time)
    {
        _authorized = authorized;
        _session = session;
        _client = session.Client as JmapClient ?? throw new InvalidOperationException("The test session cannot upload.");
        _mail = _client.Session.MailAccountId;
        _sieve = _client.Session.AccountFor(SieveScriptStore.Capability);
        if (string.IsNullOrEmpty(_mail) || !string.Equals(_mail, _sieve, StringComparison.Ordinal))
            throw new InvalidOperationException("The mail and script accounts must be the same.");
        _filters = new FilterService(new CapturedFilterAccount(session), hooks);
        _writer = writers.ForSession(session, _mail);
        _time = time;
        _hooks = hooks;
        Guard();
    }

    public async Task<FilterWriteSnapshot> CaptureAsync(CancellationToken ct)
    {
        Guard();
        var state = Need(await new SieveScriptStore(_client).ReadAsync(ct).ConfigureAwait(false));
        var target = state.Target ?? throw new InvalidOperationException("An active managed filter script is required.");
        if (state.Text.Length > 96 * 1024 || !FilterWriteSafety.IsInertScript(state.Text) || SieveScript.Parse(state.Text).Locked)
            throw new InvalidOperationException("The filter script cannot be safely restored.");
        var raw = await ReadScriptBytesAsync(target.BlobId, ct).ConfigureAwait(false);
        if (!FilterWriteSafety.CanRestore(state, raw)) throw new InvalidOperationException("The script bytes cannot be restored exactly.");
        var loaded = Need(await _filters.LoadAsync(_session.Account.Key, ct).ConfigureAwait(false));
        if (loaded.Locked || !string.Equals(loaded.State, state.State, StringComparison.Ordinal))
            throw new InvalidOperationException("The filter script changed during capture.");
        Guard();
        return new FilterWriteSnapshot(_session.Account.Key, _mail, _sieve, target.Id, state.Text, state.State!);
    }

    private async Task<byte[]> ReadScriptBytesAsync(string blobId, CancellationToken ct)
    {
        var stream = Need(await _client.DownloadAsync(_sieve, blobId, "script.sieve", "application/sieve", ct).ConfigureAwait(false));
        using (stream)
        using (var buffer = new MemoryStream())
        {
            var chunk = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > 96 * 1024) throw new InvalidOperationException("The script is too large for recovery.");
                await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
            }
            return buffer.ToArray();
        }
    }

    private void Guard()
    {
        var current = _authorized();
        if (_hooks.HasHandlers(HookNames.OnSieveScriptGenerate) || _hooks.HasHandlers(HookNames.OnBeforeFiltersSave)
            || current is null || !ReferenceEquals(current.Client, _client) || _client.IsReadOnly
            || !string.Equals(current.Account.Key, _session.Account.Key, StringComparison.Ordinal)
            || !string.Equals(_client.Session.MailAccountId, _mail, StringComparison.Ordinal)
            || !string.Equals(_client.Session.AccountFor(SieveScriptStore.Capability), _sieve, StringComparison.Ordinal))
            throw new InvalidOperationException("The captured test account is no longer authorized.");
    }

    private static T Need<T>(Result<T> result) => result.IsOk && result.Value is { } value ? value : throw new InvalidOperationException("A verification operation was refused or not acknowledged.");
}
#endif

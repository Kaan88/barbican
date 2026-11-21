// Mirrors: Bulwark lib/plugin-sandbox/host-api-mail.ts (email.getAttachment, getRaw, move, archive), host-api.ts:1421-1580 (email.setKeyword, removeKeyword, updateKeywords), assistant/b.ts (getAttachmentText),
// assistant/c.batch.ts (email.batch, undoBatch) and host-api-compose.ts (compose.open, compose.reply, drafts.create). Bulwark runs these through the email store and the JMAP client; Barbican's equivalents are
// the same writer the list's actions use (IMailWriterFactory), the same hook bus, the same draft service and the composer service behind IPluginComposeLauncher. The add-on never gets a service, only the methods.
using Barbican.Core.Compose;
using Barbican.Core.Mail.Write;
using Barbican.Core.Plugins.Api.Calendar;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Api.MailTags;
using Barbican.Core.Plugins.Hooks;

namespace Barbican.Core.Plugins.Api.MailActions;

/// <summary>The services the mail-action add-on methods run on, and the state they share (one undo-token table, the batch restore points).</summary>
public sealed class PluginMailActionHost
{
    private const int MaxRestores = 200;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, PluginBatchRestore> _restores = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();
    private long _counter;

    public PluginMailActionHost(IPluginMailReaders readers, IMailWriterFactory writers, IPluginMailRateLimiter limiter, PluginTagDefinitions tags, TimeProvider time,
        IHookBus? hooks = null, IPluginMailUndoHistory? history = null, IDraftService? drafts = null, IPluginComposeLauncher? compose = null)
    {
        Readers = readers ?? throw new ArgumentNullException(nameof(readers));
        Writers = writers ?? throw new ArgumentNullException(nameof(writers));
        Limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
        Tags = tags ?? throw new ArgumentNullException(nameof(tags));
        Time = time ?? throw new ArgumentNullException(nameof(time));
        Hooks = hooks;
        History = history;
        Drafts = drafts;
        Compose = compose;
        Undo = new PluginUndoTokens(time);
    }

    internal IPluginMailReaders Readers { get; }

    internal IMailWriterFactory Writers { get; }

    internal IPluginMailRateLimiter Limiter { get; }

    internal PluginTagDefinitions Tags { get; }

    internal TimeProvider Time { get; }

    internal IHookBus? Hooks { get; }

    internal IPluginMailUndoHistory? History { get; }

    /// <summary>Null when the app has no draft service: drafts.create is then not offered.</summary>
    internal IDraftService? Drafts { get; }

    /// <summary>Null when the app has no composer seam: compose.open and compose.reply are then not offered.</summary>
    internal IPluginComposeLauncher? Compose { get; }

    internal PluginUndoTokens Undo { get; }

    internal string Keep(PluginBatchRestore restore)
    {
        lock (_gate)
        {
            var key = "r" + (++_counter).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _restores[key] = restore;
            _order.Enqueue(key);
            while (_order.Count > MaxRestores) _ = _restores.Remove(_order.Dequeue());
            return key;
        }
    }

    internal PluginBatchRestore? Restore(string key)
    {
        lock (_gate) return _restores.GetValueOrDefault(key);
    }
}

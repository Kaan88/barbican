// The fake server's event source: every store change becomes a StateChange on each open stream (RFC 8620 §7.1, §7.3; Stalwart sends
// `event: state` and `event: ping` with {"interval": N}, crates/jmap/src/api/event_source.rs). Owner jmap.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

/// <summary>The open event streams.</summary>
public sealed class PushHub
{
    private readonly Lock _lock = new();
    private readonly List<(PushStream Stream, HashSet<string> Types)> _open = [];
    private readonly string _accountId;

    public PushHub(FakeStore store, string accountId)
    {
        ArgumentNullException.ThrowIfNull(store);
        _accountId = accountId;
        store.Changed += Publish;
    }

    /// <summary>Follows a calendar account too: its changes become StateChanges of the user's account (M4: Calendar and CalendarEvent states).</summary>
    public void Attach(FakeCalendars calendars)
    {
        ArgumentNullException.ThrowIfNull(calendars);
        calendars.Changed += Publish;
    }

    /// <summary>Streams opened so far.</summary>
    public int Opened { get; private set; }

    /// <summary>Streams open now.</summary>
    public int OpenCount
    {
        get
        {
            lock (_lock)
            {
                return _open.Count(o => !o.Stream.IsClosed);
            }
        }
    }

    /// <summary>Opens a stream for the given types (<c>*</c>: all).</summary>
    public PushStream Open(IEnumerable<string> types)
    {
        var s = new PushStream();
        lock (_lock)
        {
            _open.Add((s, new HashSet<string>(types, StringComparer.Ordinal)));
            Opened++;
        }

        return s;
    }

    /// <summary>Sends a keep-alive on every open stream.</summary>
    public void Ping()
    {
        foreach (var s in Streams())
        {
            s.Send("ping", "{\"interval\":30}");
        }
    }

    /// <summary>Sends a raw event on every open stream (a StateChange for another account, an unknown event).</summary>
    public void Send(string eventName, string data)
    {
        foreach (var s in Streams())
        {
            s.Send(eventName, data);
        }
    }

    /// <summary>Drops every open stream (the client sees end of stream).</summary>
    public void CloseAll()
    {
        foreach (var s in Streams())
        {
            s.End();
        }
    }

    private List<PushStream> Streams()
    {
        lock (_lock)
        {
            _open.RemoveAll(o => o.Stream.IsClosed);
            return [.. _open.Select(o => o.Stream)];
        }
    }

    /// <summary>Follows a contacts account: its changes become StateChanges of <paramref name="accountId"/> (null: the user's account; a shared account's id for <see cref="FakeJmapOptions.SharedContacts"/>), M5: AddressBook and ContactCard states.</summary>
    public void Attach(FakeContacts contacts, string? accountId = null)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        var account = accountId ?? _accountId;
        contacts.Changed += (_, e) => Publish(account, e);
    }

    private void Publish(object? sender, FakeStateChangedEventArgs e) => Publish(_accountId, e);

    private void Publish(string accountId, FakeStateChangedEventArgs e)
    {
        var states = e.States;
        List<(PushStream Stream, HashSet<string> Types)> open;
        lock (_lock)
        {
            open = [.. _open.Where(o => !o.Stream.IsClosed)];
        }

        foreach (var (stream, types) in open)
        {
            var changed = new JsonObject();
            foreach (var (type, state) in states)
            {
                if (types.Contains("*") || types.Contains(type))
                {
                    changed[type] = state;
                }
            }

            if (changed.Count > 0)
            {
                var change = new JsonObject { ["@type"] = "StateChange", ["changed"] = new JsonObject { [accountId] = changed } };
                stream.Send("state", change.ToJsonString());
            }
        }
    }
}

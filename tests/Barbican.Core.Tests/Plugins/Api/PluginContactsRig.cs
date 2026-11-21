// Test wiring of the contact and address-book add-on methods: the real PluginApiDispatcher over the real contact store, writer, address-book service and hook bus on the fake JMAP server (FakeSeed.ContactsM5), with spies on the store
// and the writer and doubles for the grant and the signed-in session (the things a test flips mid-call).
#pragma warning disable MA0006, MA0002 // assertions compare string literals

using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Contacts;
using Barbican.Core.Plugins.Api.Contacts;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Tests.Contacts;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Plugins.Api;

internal sealed class PluginContactsRig
{
    internal const string PluginId = "probe";

    private PluginContactsRig()
    {
    }

    internal FakeContacts Contacts { get; private set; } = null!;

    internal FakeJmapServer Server { get; private set; } = null!;

    internal ContactRig Accounts { get; private set; } = null!;

    internal string Key { get; private set; } = "";

    internal SpyStore Store { get; private set; } = null!;

    internal SpyWriter Writer { get; private set; } = null!;

    internal PluginCalendarRig.AuthorityDouble Authority { get; private set; } = null!;

    internal PluginCalendarRig.SessionsDouble Sessions { get; private set; } = null!;

    internal PluginApiDispatcher Dispatcher { get; private set; } = null!;

    internal PluginRuntimeBinding Binding { get; private set; } = null!;

    internal int Sets => Server.Api.ContactSetRequests.Count;

    internal static PluginGrantSnapshot Grant(string key, params string[] permissions) =>
        new(new PluginPackageIdentity(PluginId, "1.0.0", new string('a', 64)), 1, true, permissions, [key]);

    internal static async Task<PluginContactsRig> StartAsync(string[]? permissions = null, FakeContacts? contacts = null)
    {
        var rig = new PluginContactsRig { Contacts = contacts ?? FakeSeed.ContactsM5(0), Accounts = ContactRig.Create() };
        var (key, server, _) = await rig.Accounts.AddAsync("user@example.org", rig.Contacts);
        rig.Key = key;
        rig.Server = server;
        var store = new ContactStore(rig.Accounts, rig.Accounts.Hooks, rig.Accounts.Time);
        var books = new AddressBooks(rig.Accounts, store);
        rig.Store = new SpyStore(store);
        rig.Writer = new SpyWriter(new ContactWriter(rig.Accounts, store, books, rig.Accounts.Hooks));
        var host = new PluginContactsHost(rig.Store, rig.Writer, books);
        rig.Sessions = new PluginCalendarRig.SessionsDouble(rig.Accounts.SessionFor(key)!);
        rig.Authority = new PluginCalendarRig.AuthorityDouble(Grant(key, permissions ?? ["contacts:read", "contacts:write"]));
        rig.Dispatcher = new("auto-contacts", PluginContactsMethods.Create(host), rig.Authority, rig.Sessions);
        rig.Binding = PluginRuntimeBinding.Create("auto-contacts", rig.Authority.Current, PluginFrameKind.Background);
        return rig;
    }

    internal async Task<PluginCallOutcome> CallAsync(string method, string args)
    {
        using var doc = JsonDocument.Parse(args);
        return await Dispatcher.DispatchAsync(Binding, new PluginApiRequest("r", method, PluginValue.FromJson(doc.RootElement)), TestContext.Current.CancellationToken);
    }

    internal static List<string> Keys(PluginValue value) => [.. value.PropertyNames.Order(StringComparer.Ordinal)];

    internal static List<string> Ids(PluginValue list) => [.. Enumerable.Range(0, list.Count).Select(i => list.Item(i).Property("id").StringValue).Order(StringComparer.Ordinal)];

    internal sealed class SpyStore(IContactStore inner) : IContactStore
    {
        public Action? OnSnapshot { get; set; }

        public event EventHandler<ContactChangedEventArgs>? Changed
        {
            add => inner.Changed += value;
            remove => inner.Changed -= value;
        }

        public Task<Result<ContactSnapshot>> LoadAsync(string accountKey, CancellationToken ct) => inner.LoadAsync(accountKey, ct);

        public ContactSnapshot? Snapshot(string accountKey)
        {
            OnSnapshot?.Invoke();
            return inner.Snapshot(accountKey);
        }

        public Task<Result<Unit>> RefreshAsync(string accountKey, CancellationToken ct) => inner.RefreshAsync(accountKey, ct);

        public JsonNode? Patch(string accountKey, string cardId, JsonObject patch) => inner.Patch(accountKey, cardId, patch);

        public void Restore(string accountKey, string cardId, JsonNode? previous) => inner.Restore(accountKey, cardId, previous);
    }

    internal sealed class SpyWriter(IContactWriter inner) : IContactWriter
    {
        public Action? Before { get; set; }

        public int Calls { get; private set; }

        public Task<Result<string>> CreateAsync(string accountKey, string? bookId, JsonObject card, CancellationToken ct) => RunAsync(() => inner.CreateAsync(accountKey, bookId, card, ct));

        public Task<Result<Unit>> UpdateAsync(string accountKey, string cardId, JsonObject patch, CancellationToken ct) => RunAsync(() => inner.UpdateAsync(accountKey, cardId, patch, ct));

        public Task<Result<Unit>> DeleteAsync(string accountKey, IReadOnlyList<string> cardIds, CancellationToken ct) => RunAsync(() => inner.DeleteAsync(accountKey, cardIds, ct));

        public Task<Result<Unit>> MoveAsync(string accountKey, IReadOnlyList<string> cardIds, string bookId, CancellationToken ct) => RunAsync(() => inner.MoveAsync(accountKey, cardIds, bookId, ct));

        public Task<Result<string>> CreateGroupAsync(string accountKey, string? bookId, string name, IReadOnlyList<string> memberUids, CancellationToken ct) =>
            RunAsync(() => inner.CreateGroupAsync(accountKey, bookId, name, memberUids, ct));

        public Task<Result<Unit>> SetMembersAsync(string accountKey, string groupId, IReadOnlyList<string> add, IReadOnlyList<string> remove, CancellationToken ct) =>
            RunAsync(() => inner.SetMembersAsync(accountKey, groupId, add, remove, ct));

        public Task<Result<Unit>> MergeAsync(string accountKey, ContactMergePlan plan, IReadOnlyList<JsonNode> groups, CancellationToken ct) => RunAsync(() => inner.MergeAsync(accountKey, plan, groups, ct));

        public Task<Result<int>> ImportAsync(string accountKey, string bookId, IReadOnlyList<JsonNode> cards, CancellationToken ct) => RunAsync(() => inner.ImportAsync(accountKey, bookId, cards, ct));

        private Task<T> RunAsync<T>(Func<Task<T>> call)
        {
            Calls++;
            Before?.Invoke();
            return call();
        }
    }
}

// Mirrors: M6 plan section 3.5 (D-M6-00): an in-memory fake of IAccountIdentities that behaves like the real one where the M5 sample stores did not: its calls complete off the caller's thread, and its Changed is raised from a pool thread, so a view built on it meets the same threading the real service has (M5 found two such bugs, the real store loading on the UI thread and raising from the pool).
// Frozen in the M6 plan commit; owner integrator. Used by view-model tests and by sample-data runs (BARBICAN_SAMPLE=1); the data stream's real class never uses it.

using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Settings.Account;

namespace Barbican.Presentation.Settings.Sample;

/// <summary>An in-memory <see cref="IAccountIdentities"/> with two identities.</summary>
public sealed class SampleAccountIdentities : IAccountIdentities
{
    private readonly Lock _lock = new();
    private readonly List<AccountIdentity> _items =
    [
        new("i1", "Sample User", "user@example.org", null, null, "Sample", string.Empty, false),
        new("i2", "Sample Work", "work@example.org", null, null, string.Empty, string.Empty, true),
    ];

    private int _next = 3;

    public event EventHandler<AccountIdentityChangedEventArgs>? Changed;

    public Task<Result<IReadOnlyList<AccountIdentity>>> ListAsync(string accountKey, CancellationToken ct) =>
        SampleThreading.OffThreadAsync(() =>
        {
            lock (_lock)
            {
                return Result<IReadOnlyList<AccountIdentity>>.Ok([.. _items]);
            }
        });

    public async Task<Result<AccountIdentity>> CreateAsync(string accountKey, AccountIdentityEdit edit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var made = await SampleThreading.OffThreadAsync(() =>
        {
            lock (_lock)
            {
                var item = new AccountIdentity("i" + _next++, edit.Name ?? string.Empty, edit.Email ?? string.Empty, edit.ReplyTo, edit.Bcc, edit.TextSignature ?? string.Empty, edit.HtmlSignature ?? string.Empty, true);
                _items.Add(item);
                return item;
            }
        }).ConfigureAwait(false);
        SampleThreading.RaiseOnPool(() => Changed?.Invoke(this, new AccountIdentityChangedEventArgs(accountKey)));
        return Result<AccountIdentity>.Ok(made);
    }

    public async Task<Result<AccountIdentity>> UpdateAsync(string accountKey, string id, AccountIdentityEdit edit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var done = await SampleThreading.OffThreadAsync<AccountIdentity?>(() =>
        {
            lock (_lock)
            {
                var at = _items.FindIndex(i => string.Equals(i.Id, id, StringComparison.Ordinal));
                if (at < 0)
                {
                    return null;
                }

                var o = _items[at];
                return _items[at] = o with
                {
                    Name = edit.Name ?? o.Name,
                    Email = edit.Email ?? o.Email,
                    ReplyTo = edit.ReplyTo ?? o.ReplyTo,
                    Bcc = edit.Bcc ?? o.Bcc,
                    TextSignature = edit.TextSignature ?? o.TextSignature,
                    HtmlSignature = edit.HtmlSignature ?? o.HtmlSignature,
                };
            }
        }).ConfigureAwait(false);
        if (done is null)
        {
            return Result<AccountIdentity>.Fail(new ValidationError("That identity no longer exists."));
        }

        SampleThreading.RaiseOnPool(() => Changed?.Invoke(this, new AccountIdentityChangedEventArgs(accountKey)));
        return Result<AccountIdentity>.Ok(done);
    }

    public async Task<Result<Unit>> DeleteAsync(string accountKey, string id, CancellationToken ct)
    {
        var removed = await SampleThreading.OffThreadAsync(() =>
        {
            lock (_lock)
            {
                return _items.RemoveAll(i => string.Equals(i.Id, id, StringComparison.Ordinal) && i.MayDelete) > 0;
            }
        }).ConfigureAwait(false);
        if (!removed)
        {
            return Result<Unit>.Fail(new ValidationError("This identity cannot be deleted."));
        }

        SampleThreading.RaiseOnPool(() => Changed?.Invoke(this, new AccountIdentityChangedEventArgs(accountKey)));
        return Result<Unit>.Ok(default);
    }

    public async Task<Result<Unit>> SetOrderAsync(string accountKey, IReadOnlyList<string> orderedIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(orderedIds);
        await SampleThreading.OffThreadAsync(() =>
        {
            lock (_lock)
            {
                var ordered = orderedIds.Select(id => _items.Find(i => string.Equals(i.Id, id, StringComparison.Ordinal))).OfType<AccountIdentity>().ToList();
                _items.RemoveAll(i => ordered.Contains(i));
                _items.InsertRange(0, ordered);
            }
        }).ConfigureAwait(false);
        SampleThreading.RaiseOnPool(() => Changed?.Invoke(this, new AccountIdentityChangedEventArgs(accountKey)));
        return Result<Unit>.Ok(default);
    }
}

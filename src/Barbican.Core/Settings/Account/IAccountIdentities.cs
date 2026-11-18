// Mirrors: see AccountIdentity. Hooks: onIdentitiesLoaded, onBeforeIdentityChange (intercept), onIdentityCreate, onIdentityUpdate, onIdentityDelete; the composer-side hooks onIdentitySelect, onSignatureRender and onSelectReplyIdentity get their call sites in src/Barbican.Core/Compose and src/Barbican.Presentation/Compose from set-data (the one allowed edit there, D-M6-A07).
// THREADING (D-M6-00): ListAsync and the writes complete off the UI thread; Changed is raised on a pool thread (a push Identity state change, or a write that finished). Contract from the M6 plan section 4.5; owner set-data.

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Account;

/// <summary>The identities of an account, for the settings page.</summary>
public interface IAccountIdentities
{
    /// <summary>The identities in the user's order (the Inbox metadata `preferredIdentityIds`), empty without the submission capability.</summary>
    Task<Result<IReadOnlyList<AccountIdentity>>> ListAsync(string accountKey, CancellationToken ct);

    /// <summary>Creates an identity (Identity/set create); the server's refusal of an address that is not the account's comes back as the failure text.</summary>
    Task<Result<AccountIdentity>> CreateAsync(string accountKey, AccountIdentityEdit edit, CancellationToken ct);

    /// <summary>Updates the changed fields (Identity/set update).</summary>
    Task<Result<AccountIdentity>> UpdateAsync(string accountKey, string id, AccountIdentityEdit edit, CancellationToken ct);

    /// <summary>Deletes an identity (refused client-side when MayDelete is false).</summary>
    Task<Result<Unit>> DeleteAsync(string accountKey, string id, CancellationToken ct);

    /// <summary>Stores the order (the first is the default sender) in the Inbox metadata with ifInState.</summary>
    Task<Result<Unit>> SetOrderAsync(string accountKey, IReadOnlyList<string> orderedIds, CancellationToken ct);

    /// <summary>Raised when the identities of an account changed (the account key is in the args), on a pool thread.</summary>
    event EventHandler<AccountIdentityChangedEventArgs>? Changed;
}

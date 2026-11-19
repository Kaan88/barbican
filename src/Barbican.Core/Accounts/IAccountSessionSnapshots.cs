// Mirrors: Bulwark lib/plugin-sandbox/host-api-input.ts:25-30 and host-api-composer.ts:29-32.
// Captures and checks are atomic under the account manager lock, independent of UI event delivery.
using Barbican.Core.Common;
namespace Barbican.Core.Accounts;

public interface IAccountSessionSnapshots
{
    Result<AccountSessionSnapshot> CaptureActiveSession();
    Result<AccountSessionSnapshot> CaptureOwnerSession(string localAccountKey);
    bool IsSessionCurrent(AccountSessionSnapshot snapshot);
}

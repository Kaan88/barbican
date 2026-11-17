// Mirrors: Rust main.rs:426-427 (a unified row is read through its owner's client; tag rust-final). Fable review 3 R3-01. Owner body.
// The unified inbox implements it (UnifiedReadRoutes); without it every id is read from the account on screen as it is.

using Barbican.Core.Common;
using Barbican.Core.Mail;

namespace Barbican.Presentation.Mail.Loading;

/// <summary>Resolves a list row id to the account, service and server id it is read from.</summary>
public interface IReadRoutes
{
    /// <summary>The route of <paramref name="rowId"/>, null for a plain id of the account on screen, or a failure when its login is not connected.</summary>
    Result<ReadRoute>? Route(string accountKey, IMailService mail, string rowId);
}

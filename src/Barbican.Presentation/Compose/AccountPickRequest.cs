// Mirrors: Bulwark mail-app.tsx:7298 (the mailto account picker: with several accounts the link asks which one writes it); I-283. A dialog request (owner composer-ui draws it, IDialogContent in the App).

using Barbican.Core.Accounts;
using Barbican.Presentation.Dialogs;

namespace Barbican.Presentation.Compose;

/// <summary>Which account writes this message? The answer is the account key, or null when cancelled.</summary>
public sealed record AccountPickRequest(IReadOnlyList<AccountInfo> Accounts, string? Preferred, string Detail) : DialogRequest<string>("Write from which account?");

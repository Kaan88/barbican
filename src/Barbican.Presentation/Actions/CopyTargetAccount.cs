// Mirrors: Bulwark hooks/use-message-file-actions.ts:156-179 (one entry per other connected account, labelled with its address, and its folders);
// components/email/email-context-menu.tsx:371-379 (a heading per account above its folders). G-048. Owner actions.

namespace Barbican.Presentation.Actions;

/// <summary>Another signed-in account the picker offers, with the folders a copy may go to.</summary>
/// <param name="AccountKey">The account key of the login.</param>
/// <param name="Label">The account's address, as Bulwark labels the heading.</param>
/// <param name="Folders">Its folders that take mail.</param>
public sealed record CopyTargetAccount(string AccountKey, string Label, IReadOnlyList<CopyTargetFolder> Folders);

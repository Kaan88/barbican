namespace Barbican.Core.Compose;

/// <summary>A recipient explicitly rejected by the submission server.</summary>
public sealed record RawRejectedRecipient(string Email, string SmtpReply);

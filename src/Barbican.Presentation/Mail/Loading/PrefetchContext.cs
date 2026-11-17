// Mirrors: Rust msgload.rs fetch_ahead :357 (client_for, want_thread; tag rust-final). Owner body.

using Barbican.Core.Mail;

namespace Barbican.Presentation.Mail.Loading;

/// <summary>The account a prefetch runs for.</summary>
public sealed record PrefetchContext(string AccountKey, IMailService Mail, bool WantThread);

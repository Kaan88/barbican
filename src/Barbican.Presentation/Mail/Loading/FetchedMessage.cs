// Mirrors: Rust msgload.rs fetch :59 result (Arc<Value>, Option<Arc<Vec<Value>>>; tag rust-final). Owner body.

using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Mail.Loading;

/// <summary>A message ready for the reader: its body (cid: images inlined) and, with threading on, its conversation.</summary>
public sealed record FetchedMessage(EmailBody Body, ConversationThread? Thread);

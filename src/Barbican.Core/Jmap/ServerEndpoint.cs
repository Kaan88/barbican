// Mirrors: Rust jmap.rs fetch_session :73 (the server URL the user typed; /.well-known/jmap discovery). Stub from F2; owner jmap.

namespace Barbican.Core.Jmap;

/// <summary>The server a client connects to (the base URL the user entered).</summary>
public sealed record ServerEndpoint(Uri BaseUri);

// Mirrors: Rust instance.rs:31 Endpoint; Rust instance.rs (tag rust-final); PORT-PLAN.md section 3.9 (single instance, mailto handoff). Contract from the M3 plan section 4.5; owner composer-ui.

namespace Barbican.Core.Instance;

/// <summary>Where a running instance listens: the loopback port, the token a client proves itself with, the greeting the server proves itself with, and its process id.</summary>
public sealed record HandoffEndpoint(int Port, string Token, string Greet, int Pid);

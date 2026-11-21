// Mirrors: Rust agent.rs tool set (B-AI-008), generalized as host tools for add-ons (decision D-M6b-08).

namespace Barbican.Plugins;

/// <summary>An operation the host offers an add-on ("search_mail").</summary>
/// <param name="Name">The tool name.</param>
/// <param name="Description">What it does, written for a model.</param>
/// <param name="SchemaJson">The JSON schema (an object) of its arguments.</param>
/// <param name="Writes">True when it changes something (the add-on asks the user first, as its mode says).</param>
public sealed record HostToolSpec(string Name, string Description, string SchemaJson, bool Writes);

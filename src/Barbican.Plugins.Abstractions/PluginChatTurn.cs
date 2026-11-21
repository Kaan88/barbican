// Mirrors: Rust assistant column (B-AI-006); the add-on chat panel contract (decision D-M6b-08).

namespace Barbican.Plugins;

/// <summary>One visible line of a conversation.</summary>
/// <param name="FromUser">True for what the user said, false for the add-on's answer.</param>
/// <param name="Text">The text.</param>
public sealed record PluginChatTurn(bool FromUser, string Text);

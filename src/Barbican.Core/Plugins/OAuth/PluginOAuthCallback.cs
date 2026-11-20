namespace Barbican.Core.Plugins.OAuth;

/// <summary>The provider's response to an add-on's own OAuth redirect.</summary>
public sealed record PluginOAuthCallback(string State, string? Code, string? Error, string? ErrorDescription);

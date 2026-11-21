// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1580-1623 assertKeywordFields (the fields of a definition an add-on may set; update sends only the changed ones).
namespace Barbican.Core.Plugins.Api.MailTags;

/// <summary>A validated definition from an add-on: <paramref name="Label"/>, <paramref name="Color"/> and <paramref name="Visibility"/> are null when not given.</summary>
internal sealed record PluginKeywordInput(string Id, string? Label, string? Color, string? Visibility);

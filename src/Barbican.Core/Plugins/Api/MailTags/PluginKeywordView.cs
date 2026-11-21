// Mirrors: Bulwark stores/settings-store.ts:578-583 KeywordDefinition { id, label, color, visibility?, addedBy? } as projected by host-api.ts:1665-1667 doKeywordsList.
using Barbican.Core.Mail.Tags;

namespace Barbican.Core.Plugins.Api.MailTags;

/// <summary>A tag definition with the add-on bookkeeping kept beside it (see <see cref="PluginTagDefinitions"/>).</summary>
internal sealed record PluginKeywordView(TagDef Def, string? Visibility, string? AddedBy);

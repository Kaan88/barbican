// Mirrors: Bulwark components/email/template-picker.tsx (applying a template: subject and body, placeholders filled), lib/template-utils.ts. Owner send-pipeline.

using Barbican.Core.Editor;

namespace Barbican.Presentation.Compose.Pipeline;

/// <summary>What inserting a template changes in the composer: the blocks to put in (above the signature) and the subject when the template has one.</summary>
public sealed record TemplateInsertion(IReadOnlyList<Block> Blocks, string? Subject);

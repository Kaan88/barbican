namespace Barbican.Core.Compose;

/// <summary>An explicit composer choice; a null label id means no label.</summary>
public sealed record ComposerClassificationChoice(string? LabelId);

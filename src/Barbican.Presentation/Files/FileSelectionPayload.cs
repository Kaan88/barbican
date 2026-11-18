// Payload of the onFileSelectionChange hook (spec/plugins/hooks.csv). Owner files-ui.

namespace Barbican.Presentation.Files;

/// <summary>Payload of onFileSelectionChange: how many entries are selected.</summary>
public sealed record FileSelectionPayload(int Count);

// Payload of the onFileNavigate hook (spec/plugins/hooks.csv). Owner files-ui.

namespace Barbican.Presentation.Files;

/// <summary>Payload of onFileNavigate: the place opened (a folder key, a "*" place, or empty for the root).</summary>
public sealed record FileNavigatePayload(string Location);

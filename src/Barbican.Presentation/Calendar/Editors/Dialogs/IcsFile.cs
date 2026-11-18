// A picked .ics file. Owner cal-ui.

namespace Barbican.Presentation.Calendar.Editors.Dialogs;

/// <summary>A picked file: its name and bytes.</summary>
public sealed record IcsFile(string Name, ReadOnlyMemory<byte> Bytes);

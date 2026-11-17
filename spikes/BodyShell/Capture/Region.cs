using Windows.Foundation;

namespace Barbican.Spike.BodyShell.Capture;

/// <summary>A rectangle of the spike root in DIPs whose pixels are classified in every frame.</summary>
internal sealed record Region(string Name, Rect Dips);

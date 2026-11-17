namespace Barbican.Automation.Capture;

/// <summary>A captured image: BGRA8, top-down, the window's client area only.</summary>
internal sealed record Shot(int Width, int Height, int Stride, byte[] Bgra);

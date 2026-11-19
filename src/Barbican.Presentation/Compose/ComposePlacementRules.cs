// Mirrors: Bulwark lib/compose-placement.ts: the desktop default is floating over the list.
namespace Barbican.Presentation.Compose;

/// <summary>Normalizes the stored Compose button location, including unknown legacy values.</summary>
public static class ComposePlacementRules
{
    public static string Normalize(string? value) => value switch
    {
        "sidebar" => "sidebar",
        "rail" => "rail",
        _ => "floating",
    };
}

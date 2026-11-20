namespace Barbican.Presentation.Compose;

/// <summary>Delivers private compose-link parameters for the exact draft opened by a request.</summary>
public interface IComposeLinkAddOns
{
    Task DeliverAsync(ComposerViewModel draft, IReadOnlyDictionary<string, string> parameters, CancellationToken ct);
}

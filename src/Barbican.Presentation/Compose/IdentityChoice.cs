// Mirrors: Bulwark components/email/email-composer.tsx From select :5813 (identity name and address, the address shown in Unicode). Owner composer-ui.

using Barbican.Core.Compose;
using Barbican.Core.Compose.Recipients;

namespace Barbican.Presentation.Compose;

/// <summary>One row of the From select.</summary>
public sealed record IdentityChoice(string Id, string Name, string Email)
{
    /// <summary>"Name &lt;address&gt;" with the domain in Unicode, as Bulwark lists it.</summary>
    public string Label => Name.Trim().Length == 0 ? RecipientText.ToUnicodeEmail(Email) : $"{Name.Trim()} <{RecipientText.ToUnicodeEmail(Email)}>";

    public static IdentityChoice Of(ComposeIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new IdentityChoice(identity.Identity.Id, identity.Identity.Name ?? string.Empty, identity.Identity.Email);
    }
}

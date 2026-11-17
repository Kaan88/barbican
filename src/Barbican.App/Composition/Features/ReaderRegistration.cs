// Mirrors: M2 plan section 3.3. Owner reader: IGuardedHttp, IAvatarService, IVerificationCodeFinder, IAttachmentSaver, ITrustedSenders, IReaderBannerSource (several), MENU-READER and MENU-ATT providers,
// the attachment and reader commands, the write-test guard. Stage F creates it empty.

using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Composition.Features;

/// <summary>Reader depth: unsubscribe, receipts, invitations, trust, source, attachments, avatars, guarded HTTP (M2 stream reader).</summary>
public static class ReaderRegistration
{
    public static IServiceCollection AddBarbicanReaderDepth(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}

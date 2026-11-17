// Mirrors: Bulwark email-composer.tsx priority option (X-Priority / Importance headers); RFC 2156 section 5.3.4. Contract from the M3 plan section 4.3; owner send-pipeline.

namespace Barbican.Core.Compose;

/// <summary>The composer's Priority option.</summary>
public enum MessagePriority
{
    Normal,
    High,
    Low,
}

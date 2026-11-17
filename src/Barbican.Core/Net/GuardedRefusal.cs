// Mirrors: M2 plan §6.3 step 2. Thrown inside GuardedHttp's ConnectCallback when a resolved address is not allowed; GuardedHttp turns it into a
// ValidationError with this message (it never leaves GuardedHttp). Owner reader.

namespace Barbican.Core.Net;

/// <summary>A request the guard refused before any byte was sent.</summary>
internal sealed class GuardedRefusal : Exception
{
    /// <summary>A refusal with the message the user sees.</summary>
    public GuardedRefusal(string message)
        : base(message)
    {
    }

    /// <summary>Unused (the analyzers ask for the standard constructors).</summary>
    public GuardedRefusal()
    {
    }

    /// <summary>Unused (the analyzers ask for the standard constructors).</summary>
    public GuardedRefusal(string message, Exception inner)
        : base(message, inner)
    {
    }
}

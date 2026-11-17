// Mirrors: M1-plan §1 (red tests): the single marker every contract stub throws until its stream implements it. Stub from F2.

namespace Barbican.Core.Common;

/// <summary>
/// Thrown by a contract stub that the owning stream has not implemented yet. A red test fails with this exception, which is how
/// tools/red-check.py tells "red for the right reason" from a test that errors for any other cause.
/// </summary>
public sealed class PortPendingException : Exception
{
    public PortPendingException()
        : base("Port pending")
    {
    }

    public PortPendingException(string message)
        : base("Port pending: " + message)
    {
    }

    public PortPendingException(string message, Exception innerException)
        : base("Port pending: " + message, innerException)
    {
    }
}

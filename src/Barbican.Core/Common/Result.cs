// Mirrors: PORT-PLAN.md §4.1 (expected failures are values, not exceptions, in the JMAP layer).
// Contract stub from F2 (M1-plan §4.1); owner jmap.

using Barbican.Core.Jmap.Errors;

// CA1000: static Ok/Fail on the generic type is the contract of PORT-PLAN.md §4.1 (Result<T>.Ok(x)); the idiom is worth the one suppression.
#pragma warning disable CA1000

namespace Barbican.Core.Common;

/// <summary>Success with a value, or a <see cref="JmapError"/>.</summary>
public readonly record struct Result<T>
{
    /// <summary>The value; meaningful only when <see cref="IsOk"/>.</summary>
    public T? Value { get; init; }

    /// <summary>The error; null when <see cref="IsOk"/>.</summary>
    public JmapError? Error { get; init; }

    /// <summary>True when the call succeeded.</summary>
    public bool IsOk => Error is null;

    /// <summary>A success.</summary>
    public static Result<T> Ok(T value) => new() { Value = value };

    /// <summary>A failure.</summary>
    public static Result<T> Fail(JmapError error) => new() { Error = error };

    /// <summary>Transforms the value of a success.</summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> map) => throw new PortPendingException("Result.Map: jmap stream");

    /// <summary>Chains a call that can fail.</summary>
    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> bind) => throw new PortPendingException("Result.Bind: jmap stream");

    /// <summary>Folds both cases into one value.</summary>
    public TOut Match<TOut>(Func<T, TOut> ok, Func<JmapError, TOut> fail) => throw new PortPendingException("Result.Match: jmap stream");
}

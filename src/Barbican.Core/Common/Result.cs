// Mirrors: PORT-PLAN.md §3.4 / §4.1 (expected failures are values, not exceptions, in the JMAP layer); Rust jmap.rs `type R<T> =
// Result<T, String>` (:27), typed here with the closed JmapError hierarchy. Owner jmap.

using Barbican.Core.Jmap.Errors;

// CA1000: static Ok/Fail on the generic type is the contract of PORT-PLAN.md §4.1 (Result<T>.Ok(x)); the idiom is worth the one suppression.
#pragma warning disable CA1000

namespace Barbican.Core.Common;

/// <summary>Success with a value, or a <see cref="JmapError"/>. Never throws for an expected failure.</summary>
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
    public static Result<T> Fail(JmapError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new() { Error = error };
    }

    /// <summary>Transforms the value of a success; a failure passes through.</summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return Error is null ? Result<TOut>.Ok(map(Value!)) : Result<TOut>.Fail(Error);
    }

    /// <summary>Chains a call that can fail; a failure passes through.</summary>
    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return Error is null ? bind(Value!) : Result<TOut>.Fail(Error);
    }

    /// <summary>Folds both cases into one value.</summary>
    public TOut Match<TOut>(Func<T, TOut> ok, Func<JmapError, TOut> fail)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ArgumentNullException.ThrowIfNull(fail);
        return Error is null ? ok(Value!) : fail(Error);
    }
}

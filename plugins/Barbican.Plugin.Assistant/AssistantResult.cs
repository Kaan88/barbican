// Mirrors: nothing (a plugin cannot see the app's Result type; it references only Barbican.Plugins.Abstractions). Same shape as the host's: IsOk, Value, Error.

namespace Barbican.Plugin.Assistant;

/// <summary>A value or a plain error message.</summary>
public sealed class AssistantResult<T>
{
    private AssistantResult(T? value, string? error)
    {
        Value = value;
        Error = error;
    }

    /// <summary>True when there is a value.</summary>
    public bool IsOk => Error is null;

    /// <summary>The value (set when <see cref="IsOk"/>).</summary>
    public T? Value { get; }

    /// <summary>The message the user reads (set when not <see cref="IsOk"/>).</summary>
    public string? Error { get; }

#pragma warning disable CA1000 // the factories read as Result<T>.Ok(...), the shape the whole app uses
    /// <summary>A success.</summary>
    public static AssistantResult<T> Ok(T value) => new(value, null);

    /// <summary>A failure.</summary>
    public static AssistantResult<T> Fail(string error) => new(default, error);
#pragma warning restore CA1000
}

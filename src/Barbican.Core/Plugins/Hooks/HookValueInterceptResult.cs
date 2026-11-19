// Mirrors: lib/plugin-hooks.ts:325-365 (interceptWithValue).
#pragma warning disable CA1000 // Factories keep impossible decision/value combinations unconstructable.
namespace Barbican.Core.Plugins.Hooks;
public sealed class HookValueInterceptResult<T>
{
    private HookValueInterceptResult(bool allowed, T? value) { Allowed = allowed; Value = value; }
    public bool Allowed { get; }
    public T? Value { get; }
    public static HookValueInterceptResult<T> Accept(T value) => new(true, value);
    public static HookValueInterceptResult<T> Veto() => new(false, default);
}

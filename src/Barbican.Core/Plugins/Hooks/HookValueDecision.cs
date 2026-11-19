// Mirrors: lib/plugin-hooks.ts:325-365 (interceptWithValue).
#pragma warning disable CA1000 // Factories keep impossible decision/value combinations unconstructable.
namespace Barbican.Core.Plugins.Hooks;
public sealed class HookValueDecision<T>
{
    private HookValueDecision(HookValueDecisionKind kind, T? value) { Kind = kind; Value = value; }
    public HookValueDecisionKind Kind { get; }
    public T? Value { get; }
    public static HookValueDecision<T> Unchanged() => new(HookValueDecisionKind.Unchanged, default);
    public static HookValueDecision<T> Veto() => new(HookValueDecisionKind.Veto, default);
    public static HookValueDecision<T> Replace(T value) => new(HookValueDecisionKind.Replace, value);
}

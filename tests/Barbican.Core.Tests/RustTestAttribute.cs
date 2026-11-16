using Xunit.v3;

namespace Barbican.Core.Tests;

/// <summary>
/// Marks a test ported from the Rust app (PORT-PLAN.md §2.4). The value is the Rust path
/// <c>module::test_name</c> at tag rust-final; it becomes the xUnit trait <c>RustTest</c>, so
/// <c>tools/build.ps1 test -- --filter-trait "RustTest=rte::model::marks_at_caret"</c> finds one test.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RustTestAttribute(string rustPath) : Attribute, ITraitAttribute
{
    public string RustPath { get; } = rustPath;

    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() => [new("RustTest", RustPath)];
}

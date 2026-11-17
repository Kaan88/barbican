using Xunit.v3;

namespace Barbican.Testing;

/// <summary>
/// Marks a test ported from the Rust app (PORT-PLAN.md §2.4). The value is <c>module::test_name</c>
/// exactly as the <c>rust_test_tag</c> column of spec/rust-tests.csv has it (module path without the
/// <c>tests::</c> segment, e.g. <c>rte::model::marks_at_caret</c>); <c>tools/spec-check.py</c> maps it to
/// its T- row. It becomes the xUnit trait <c>RustTest</c>, so
/// <c>tools/build.ps1 test -- --filter-trait "RustTest=rte::model::marks_at_caret"</c> finds one test.
/// Shared by every test project (linked in by tests/Directory.Build.props).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RustTestAttribute(string rustPath) : Attribute, ITraitAttribute
{
    public string RustPath { get; } = rustPath;

    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() => [new("RustTest", RustPath)];
}

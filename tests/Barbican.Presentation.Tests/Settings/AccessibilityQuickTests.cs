using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Settings.Pages;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Settings;
using Barbican.Presentation.Settings.Sample;

namespace Barbican.Presentation.Tests.Settings;

public sealed class AccessibilityQuickTests
{
    [Theory]
    [InlineData(null, "floating")]
    [InlineData("unknown", "floating")]
    [InlineData("floating", "floating")]
    [InlineData("sidebar", "sidebar")]
    [InlineData("rail", "rail")]
    public void Compose_placement_uses_reference_default(string? stored, string expected) =>
        Assert.Equal(expected, ComposePlacementRules.Normalize(stored));

    [Fact]
    public async Task Failed_undo_keeps_unrestored_entries_for_retry()
    {
        var values = new RejectingValues();
        var model = new AccessibilityQuickModel(values);
        var ct = TestContext.Current.CancellationToken;
        foreach (var key in new[] { "first", "second", "third" }) Assert.True((await values.SetAsync(key, JsonValue.Create("preset"), ct)).IsOk);
        var undo = new Dictionary<string, JsonNode?>(StringComparer.Ordinal)
        {
            ["first"] = JsonValue.Create("original"), ["second"] = JsonValue.Create("original"), ["third"] = JsonValue.Create("original"),
        };
        values.Reject = "second";
        var partial = await model.ApplyAsync(undo.ToArray(), ct);
        Assert.NotNull(partial.Error);
        Assert.Single(partial.Completed);
        Assert.Equal("first", partial.Completed[0]);
        foreach (var key in partial.Completed) _ = undo.Remove(key);
        Assert.Equal(2, undo.Count);
        Assert.True(undo.ContainsKey("second"));
        Assert.True(undo.ContainsKey("third"));
        Assert.Equal("original", values.Stored("first")!.GetValue<string>());
        Assert.Equal("preset", values.Stored("second")!.GetValue<string>());
        values.Reject = null;
        var retry = await model.ApplyAsync(undo.ToArray(), ct);
        Assert.Null(retry.Error);
        foreach (var key in retry.Completed) _ = undo.Remove(key);
        Assert.Empty(undo);
        Assert.Equal("original", values.Stored("third")!.GetValue<string>());
    }

    private sealed class RejectingValues : ISettingValues
    {
        private readonly SampleSettingValues _inner = new();
        public string? Reject { get; set; }
        public event EventHandler<SettingChangedEventArgs>? Changed { add => _inner.Changed += value; remove => _inner.Changed -= value; }
        public JsonNode? Stored(string key) => _inner.Stored(key);
        public Task<Result<Unit>> SetAsync(string key, JsonNode? value, CancellationToken ct) =>
            string.Equals(key, Reject, StringComparison.Ordinal)
                ? Task.FromResult(Result<Unit>.Fail(new ValidationError("Rejected")))
                : _inner.SetAsync(key, value, ct);
    }
}

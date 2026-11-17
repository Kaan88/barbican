// Ported from Rust src/jmap.rs tests stalwart_state_round_trip (:1707) and went_back_only_for_lower_stalwart_ids (:1728).
//

using Barbican.Core.Jmap.State;

namespace Barbican.Core.Tests.Jmap;

public sealed class StalwartStateTests
{
    [Fact]
    [RustTest("jmap::stalwart_state_round_trip")]
    public void Stalwart_state_round_trip()
    {
        foreach (var id in new ulong[] { 0, 1, 31, 127, 128, 300, 16_383, 16_384, 1UL << 40, ulong.MaxValue })
        {
            var s = StalwartState.Encode(id);
            Assert.StartsWith("s", s, StringComparison.Ordinal);
            Assert.Equal(id, StalwartState.ChangeId(s));
        }

        // vectors captured from Stalwart (Bulwark lib/__tests__/stalwart-state.test.ts)
        (string State, ulong Id)[] vectors =
        [
            ("sae", 1), ("sai", 2), ("saa", 0), ("sd2", 31), ("sp2", 127), ("sqaaq", 128), ("svqba", 300),
            ("s303q", 16383), ("sqcaac", 16384), ("syccd7", 1_000_000), ("sz9bpcbi", 12_345_678), ("s33333333333333yb", ulong.MaxValue),
        ];
        foreach (var (state, id) in vectors)
        {
            Assert.Equal(id, StalwartState.ChangeId(state));
            Assert.Equal(state, StalwartState.Encode(id));
        }

        Assert.Null(StalwartState.ChangeId("n"));
        Assert.Null(StalwartState.ChangeId("rabc"));
        Assert.Null(StalwartState.ChangeId("s"));
        Assert.Null(StalwartState.ChangeId("sXYZ"));

        // a non-canonical encoding is not a state Stalwart wrote
        Assert.Null(StalwartState.ChangeId(StalwartState.Encode(5) + "a"));
    }

    [Fact]
    [RustTest("jmap::went_back_only_for_lower_stalwart_ids")]
    public void Went_back_only_for_lower_stalwart_ids()
    {
        Assert.True(StalwartState.WentBack(StalwartState.Encode(500), StalwartState.Encode(20)));
        Assert.False(StalwartState.WentBack(StalwartState.Encode(20), StalwartState.Encode(500)));
        Assert.False(StalwartState.WentBack(StalwartState.Encode(20), StalwartState.Encode(20)));
        Assert.False(StalwartState.WentBack("opaque-1", "opaque-0"));
    }
}

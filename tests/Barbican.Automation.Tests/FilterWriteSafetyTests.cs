using System.Text;
using System.Text.Json.Nodes;
using Barbican.App.MailBody.Automation;
using Barbican.Core.Filters.Rules;
using Barbican.Core.Sieve;

namespace Barbican.Automation.Tests;

public sealed class FilterWriteSafetyTests
{
    [Fact]
    public void SnapshotMustBeAnActiveManagedScriptWithExactUtf8Bytes()
    {
        var active = new SieveScriptInfo("script", "filters", "blob", true);
        var state = new SieveScriptState([active], "keep;\r\n", []) { State = "v1" };
        var bytes = Encoding.UTF8.GetBytes(state.Text);
        Assert.True(FilterWriteSafety.CanRestore(state, bytes));
        Assert.False(FilterWriteSafety.CanRestore(state with { State = null }, bytes));
        Assert.False(FilterWriteSafety.CanRestore(state with { Scripts = [active with { IsActive = false }] }, bytes));
        Assert.False(FilterWriteSafety.CanRestore(state with { Scripts = [active with { Name = "vacation" }] }, bytes));
        Assert.False(FilterWriteSafety.CanRestore(state with { Scripts = [active, new SieveScriptInfo("other", "vacation", "blob2", true)] }, bytes));
        Assert.False(FilterWriteSafety.CanRestore(state, [0xef, 0xbb, 0xbf, .. bytes]));
        Assert.False(FilterWriteSafety.CanRestore(state, [0xff]));
        Assert.False(FilterWriteSafety.CanRestore(state, Encoding.UTF8.GetBytes("changed")));
    }

    [Fact]
    public void PlanCannotApplyToOtherMailOrAnotherDestination()
    {
        var plan = new FilterScan(new JsonObject(), [new FilterPlanStep("move", ["own"], "folder")], 1, null, 1, "mail-state");
        Assert.True(FilterWriteSafety.IsExactPlan(plan, "own", "folder"));
        Assert.False(FilterWriteSafety.IsExactPlan(plan with { Steps = [new FilterPlanStep("move", ["own", "other"], "folder")] }, "own", "folder"));
        Assert.False(FilterWriteSafety.IsExactPlan(plan with { Steps = [new FilterPlanStep("move", ["own"], "other")] }, "own", "folder"));
        Assert.False(FilterWriteSafety.IsExactPlan(plan with { Steps = [new FilterPlanStep("copy", ["own"], "folder")] }, "own", "folder"));
        Assert.False(FilterWriteSafety.IsExactPlan(plan with { State = "" }, "own", "folder"));
        Assert.False(FilterWriteSafety.IsExactPlan(plan with { Unsupported = "unsupported" }, "own", "folder"));
    }
}

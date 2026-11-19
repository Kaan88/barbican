// M6 exit evidence: exercise the actual identity selection and template insertion ViewModel paths.
using Barbican.Core.Compose;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;

namespace Barbican.Presentation.Tests.Compose;

public sealed class M6ComposerHookExecutionTests
{
    [Fact]
    public async Task Identity_selection_and_template_hooks_execute_with_valid_transformed_content()
    {
        var hooks = new HookBus();
        var selected = new TaskCompletionSource<IdentityViewPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        var applied = new TaskCompletionSource<TemplateAdminViewPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transformed = 0;
        using var identity = hooks.Observe<IdentityViewPayload>(HookNames.OnIdentitySelect, (payload, _) =>
        {
            selected.TrySetResult(payload);
            return ValueTask.CompletedTask;
        });
        using var render = hooks.Transform<TemplateAdminRenderPayload>(HookNames.OnTemplateRender, (payload, _) =>
        {
            transformed++;
            return ValueTask.FromResult(payload with { Subject = "Transformed subject", Text = "Transformed template body" });
        });
        using var apply = hooks.Observe<TemplateAdminViewPayload>(HookNames.OnTemplateApply, (payload, _) =>
        {
            applied.TrySetResult(payload);
            return ValueTask.CompletedTask;
        });
        var harness = new ComposerHarness(hooks: hooks);
        using var composer = harness.Open();
        composer.SelectedIdentityId = "i2";
        var chosen = await selected.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Equal("i2", chosen.Id);
        await composer.InsertTemplateAsync(new MailTemplate("t1", "Synthetic template", "Original subject", "Original body", false));
        var inserted = await applied.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Equal(1, transformed);
        Assert.Equal("Transformed subject", composer.Subject);
        Assert.Equal("Transformed template body", composer.FallbackBody);
        Assert.Equal("t1", inserted.Id);
        Assert.Equal("Transformed subject", inserted.Subject);
        Assert.Equal("Transformed template body", inserted.Body);
    }
}

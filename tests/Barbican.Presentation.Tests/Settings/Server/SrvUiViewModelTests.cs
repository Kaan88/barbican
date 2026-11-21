// Not Rust rows: I-291, I-432..I-436 (the srv-ui view models on the Sample* fakes, which complete and raise Changed off the caller: D-M6-00). M6 stream srv-ui.

using Barbican.Core.Settings.Folders;
using Barbican.Core.Settings.Vacation;
using Barbican.Core.Common;
using Barbican.Presentation.Settings.Sample;
using Barbican.Presentation.Settings.Server;

namespace Barbican.Presentation.Tests.Settings.Server;

public sealed class SrvUiViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_filters_page_lists_enables_and_deletes_a_rule()
    {
        var d = new SrvUiQueuedDispatcher();
        var vm = new SrvUiFiltersViewModel(new SampleFilterService(), null, d, new SrvUiSampleFolders());
        await d.RunAsync(vm.LoadAsync(Ct));
        var row = Assert.Single(vm.Rows);
        Assert.Equal("Newsletters", row.Name);
        Assert.True(row.Enabled);
        await d.RunAsync(vm.SetEnabledAsync(row.Id, false, Ct));
        Assert.False(Assert.Single(vm.Rows).Enabled);
        await d.RunAsync(vm.DeleteAsync(row.Id, Ct));
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task The_filters_page_raises_on_the_dispatcher_not_on_the_pool()
    {
        var dispatcher = new SrvUiQueuedDispatcher();
        var vm = new SrvUiFiltersViewModel(new SampleFilterService(), null, dispatcher);
        var raised = new List<int>();
        vm.PropertyChanged += (_, _) => raised.Add(Environment.CurrentManagedThreadId);
        var loading = vm.LoadAsync(Ct);
        Assert.True(SpinWait.SpinUntil(() => dispatcher.Pending >= 2, TimeSpan.FromSeconds(5)));
        Assert.Empty(vm.Rows);
        Assert.Empty(raised);
        var pumpThread = Environment.CurrentManagedThreadId;
        dispatcher.Pump();
        await loading;
        Assert.Single(vm.Rows);
        Assert.NotEmpty(raised);
        Assert.All(raised, id => Assert.Equal(pumpThread, id));
    }

    [Fact]
    public async Task The_rule_editor_saves_a_new_rule_into_the_list()
    {
        var d = new SrvUiQueuedDispatcher();
        var vm = new SrvUiFiltersViewModel(new SampleFilterService(), null, d, new SrvUiSampleFolders());
        await d.RunAsync(vm.LoadAsync(Ct));
        var editor = new SrvUiFilterEditorModel(vm, null) { Name = "Invoices" };
        editor.SetValue(0, "billing@example.org");
        editor.SetActionKind(0, "star");
        await d.RunAsync(editor.SaveAsync(Ct));
        Assert.NotNull(editor.Saved);
        Assert.Contains(vm.Rows, r => string.Equals(r.Name, "Invoices", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_apply_dialog_scans_then_applies_after_the_user_confirms()
    {
        var d = new SrvUiQueuedDispatcher();
        var vm = new SrvUiFiltersViewModel(new SampleFilterService(), null, d);
        await d.RunAsync(vm.LoadAsync(Ct));
        var apply = new SrvUiFilterApplyModel(vm, vm.RuleOf(vm.Rows[0].Id)!);
        await d.RunAsync(apply.StartAsync(Ct));
        Assert.Equal(SrvUiApplyPhase.Ready, apply.Phase);
        Assert.Equal(2, apply.Total);
        await d.RunAsync(apply.ApplyAsync(Ct));
        Assert.Equal(SrvUiApplyPhase.Done, apply.Phase);
        Assert.Contains("2", apply.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_saved_out_of_office_is_what_the_next_load_returns_with_its_status()
    {
        var d = new SrvUiQueuedDispatcher();
        var service = new SampleVacationService();
        var time = new FixedTime(new DateTimeOffset(2026, 12, 22, 10, 0, 0, TimeSpan.Zero));
        var vm = new SrvUiVacationViewModel(service, null, d, time);
        await d.RunAsync(vm.LoadAsync(Ct));
        Assert.Equal("Off", vm.Status);
        vm.Enabled = true;
        vm.Subject = "Away";
        vm.Body = "Back soon.";
        vm.ToText = "2026-12-24";
        await d.RunAsync(vm.SaveAsync(Ct));
        Assert.Equal("Active until 2026-12-24", vm.Status);
        var again = new SrvUiVacationViewModel(service, null, d, time);
        await d.RunAsync(again.LoadAsync(Ct));
        Assert.Equal("Back soon.", again.Body);
        again.ToText = "24 December";
        await d.RunAsync(again.SaveAsync(Ct));
        Assert.Equal("Enter days as yyyy-MM-dd.", again.Error);
    }

    [Fact]
    public async Task Vacation_html_round_trips_and_can_be_cleared_without_changing_plain_text()
    {
        var d = new SrvUiQueuedDispatcher();
        var service = new SampleVacationService();
        var initial = new VacationSettings(false, "Away", "Plain reply", "<p>Existing</p>", null, null, null, null, false);
        _ = await service.SaveAsync(SrvUiPageModel.SampleKey, initial, Ct);
        var vm = new SrvUiVacationViewModel(service, null, d);
        await d.RunAsync(vm.LoadAsync(Ct));
        Assert.Equal("<p>Existing</p>", vm.HtmlBody);
        vm.HtmlBody = "<p>Edited <b>HTML</b></p>";
        await d.RunAsync(vm.SaveAsync(Ct));
        var saved = await service.LoadAsync(SrvUiPageModel.SampleKey, Ct);
        Assert.Equal("Plain reply", saved.Value!.TextBody);
        Assert.Equal("<p>Edited <b>HTML</b></p>", saved.Value.HtmlBody);
        vm.HtmlBody = string.Empty;
        await d.RunAsync(vm.SaveAsync(Ct));
        saved = await service.LoadAsync(SrvUiPageModel.SampleKey, Ct);
        Assert.Null(saved.Value!.HtmlBody);
        Assert.Equal("Plain reply", saved.Value.TextBody);
    }

    [Fact]
    public async Task Vacation_save_keeps_html_edits_made_while_the_service_is_pending()
    {
        var d = new SrvUiQueuedDispatcher();
        var service = new DelayedVacationService();
        var vm = new SrvUiVacationViewModel(service, null, d);
        await d.RunAsync(vm.LoadAsync(Ct));
        vm.Body = "Plain";
        vm.HtmlBody = "<p>First</p>";
        var saving = vm.SaveAsync(Ct);
        vm.HtmlBody = "<p>Newer</p>";
        vm.ToText = "2026-12-31";
        vm.ForwardKeep = false;
        service.Release();
        await d.RunAsync(saving);
        Assert.Equal("<p>Newer</p>", vm.HtmlBody);
        Assert.Equal("2026-12-31", vm.ToText);
        Assert.False(vm.ForwardKeep);
        Assert.Contains("not saved yet", vm.Message, StringComparison.Ordinal);
        var saved = await service.LoadAsync(SrvUiPageModel.SampleKey, Ct);
        Assert.Equal("<p>First</p>", saved.Value!.HtmlBody);
    }

    [Fact]
    public async Task Identities_create_show_in_the_list_and_a_default_one_cannot_be_deleted()
    {
        var d = new SrvUiQueuedDispatcher();
        var vm = new SrvUiIdentitiesViewModel(new SampleAccountIdentities(), null, d);
        await d.RunAsync(vm.LoadAsync(Ct));
        Assert.Equal(2, vm.Items.Count);
        vm.StartNew();
        vm.Email = "new@example.org";
        vm.Name = "New";
        await d.RunAsync(vm.SaveAsync(Ct));
        Assert.Equal(3, vm.Items.Count);
        Assert.Equal("new@example.org", vm.Email);
        vm.Select("i1");
        Assert.False(vm.CanDelete);
        await d.RunAsync(vm.DeleteAsync(Ct));
        Assert.Equal(3, vm.Items.Count);
        await d.RunAsync(vm.MoveAsync("i2", -1, Ct));
        Assert.Equal("i2", vm.Items[0].Id);
    }

    [Fact]
    public async Task Templates_are_created_listed_and_report_their_placeholders()
    {
        var d = new SrvUiQueuedDispatcher();
        var vm = new SrvUiTemplatesViewModel(new SampleTemplateAdmin(), null, d);
        await d.RunAsync(vm.LoadAsync(Ct));
        Assert.Equal("Thanks", Assert.Single(vm.Items).Name);
        vm.StartNew();
        vm.Name = "Follow up";
        vm.Subject = "Re: {{topic}}";
        vm.Body = "Hi {{name}}, about {{topic}}.";
        Assert.Equal("name,topic", string.Join(',', vm.Placeholders.Order(StringComparer.Ordinal)));
        await d.RunAsync(vm.SaveAsync(Ct));
        Assert.Equal(2, vm.Items.Count);
    }

    [Fact]
    public async Task The_folders_page_keeps_prefs_and_refuses_a_move_under_itself()
    {
        var d = new SrvUiQueuedDispatcher();
        var vm = new SrvUiFoldersViewModel(new SampleFolderAdmin(), new SrvUiSampleFolders(), null, d);
        await d.RunAsync(vm.LoadAsync(Ct));
        Assert.Equal("f-inbox", vm.Rows[0].Node.Id);
        vm.Select("f-projects");
        Assert.DoesNotContain(vm.MoveTargets(), n => n.Id is "f-projects" or "f-alpha" or "f-beta");
        Assert.True(await d.RunAsync(vm.SetPrefAsync(FolderAdminPref.Pin, "true", Ct)));
        await d.RunAsync(vm.LoadAsync(Ct));
        Assert.Contains("f-projects", vm.Prefs.Pinned);
        Assert.False(await d.RunAsync(vm.CreateAsync("a/b", false, Ct)));
    }

    [Fact]
    public async Task Recover_lists_and_restores_an_item()
    {
        var d = new SrvUiQueuedDispatcher();
        var vm = new SrvUiRecoverViewModel(new SrvUiSampleRecover(), null, d);
        await d.RunAsync(vm.LoadAsync(Ct));
        Assert.Equal(3, vm.Items.Count);
        await d.RunAsync(vm.RestoreAsync("a2", Ct));
        Assert.Equal(2, vm.Items.Count);
    }

    private sealed class DelayedVacationService : IVacationService
    {
        private readonly SampleVacationService _inner = new();
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<VacationChangedEventArgs>? Changed
        {
            add => _inner.Changed += value;
            remove => _inner.Changed -= value;
        }

        public Task<Result<VacationSettings>> LoadAsync(string accountKey, CancellationToken ct) => _inner.LoadAsync(accountKey, ct);

        public async Task<Result<VacationSettings>> SaveAsync(string accountKey, VacationSettings settings, CancellationToken ct)
        {
            await _release.Task.WaitAsync(ct);
            return await _inner.SaveAsync(accountKey, settings, ct);
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}

// Not Rust rows: I-291, I-432..I-436 (the srv-ui view models on the Sample* fakes, which complete and raise Changed off the caller: D-M6-00). M6 stream srv-ui.

using Barbican.Core.Settings.Folders;
using Barbican.Presentation.Common;
using Barbican.Presentation.Settings.Sample;
using Barbican.Presentation.Settings.Server;

namespace Barbican.Presentation.Tests.Settings.Server;

public sealed class SrvUiViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_filters_page_lists_enables_and_deletes_a_rule()
    {
        var vm = new SrvUiFiltersViewModel(new SampleFilterService(), null, new InlineDispatcher(), new SrvUiSampleFolders());
        await vm.LoadAsync(Ct);
        var row = Assert.Single(vm.Rows);
        Assert.Equal("Newsletters", row.Name);
        Assert.True(row.Enabled);
        await vm.SetEnabledAsync(row.Id, false, Ct);
        Assert.False(Assert.Single(vm.Rows).Enabled);
        await vm.DeleteAsync(row.Id, Ct);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task The_filters_page_raises_on_the_dispatcher_not_on_the_pool()
    {
        var dispatcher = new SrvUiQueuedDispatcher();
        var vm = new SrvUiFiltersViewModel(new SampleFilterService(), null, dispatcher);
        var raised = new List<int>();
        vm.PropertyChanged += (_, _) => raised.Add(Environment.CurrentManagedThreadId);
        await vm.LoadAsync(Ct);
        Assert.Empty(raised);
        Assert.True(dispatcher.Pending > 0);
        dispatcher.Pump();
        Assert.NotEmpty(raised);
        Assert.All(raised, id => Assert.Equal(Environment.CurrentManagedThreadId, id));
    }

    [Fact]
    public async Task The_rule_editor_saves_a_new_rule_into_the_list()
    {
        var vm = new SrvUiFiltersViewModel(new SampleFilterService(), null, new InlineDispatcher(), new SrvUiSampleFolders());
        await vm.LoadAsync(Ct);
        var editor = new SrvUiFilterEditorModel(vm, null) { Name = "Invoices" };
        editor.SetValue(0, "billing@example.org");
        editor.SetActionKind(0, "star");
        await editor.SaveAsync(Ct);
        Assert.NotNull(editor.Saved);
        Assert.Contains(vm.Rows, r => string.Equals(r.Name, "Invoices", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_apply_dialog_scans_then_applies_after_the_user_confirms()
    {
        var vm = new SrvUiFiltersViewModel(new SampleFilterService(), null, new InlineDispatcher());
        await vm.LoadAsync(Ct);
        var apply = new SrvUiFilterApplyModel(vm, vm.RuleOf(vm.Rows[0].Id)!);
        await apply.StartAsync(Ct);
        Assert.Equal(SrvUiApplyPhase.Ready, apply.Phase);
        Assert.Equal(2, apply.Total);
        await apply.ApplyAsync(Ct);
        Assert.Equal(SrvUiApplyPhase.Done, apply.Phase);
        Assert.Contains("2", apply.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_saved_out_of_office_is_what_the_next_load_returns_with_its_status()
    {
        var service = new SampleVacationService();
        var time = new FixedTime(new DateTimeOffset(2026, 12, 22, 10, 0, 0, TimeSpan.Zero));
        var vm = new SrvUiVacationViewModel(service, null, new InlineDispatcher(), time);
        await vm.LoadAsync(Ct);
        Assert.Equal("Off", vm.Status);
        vm.Enabled = true;
        vm.Subject = "Away";
        vm.Body = "Back soon.";
        vm.ToText = "2026-12-24";
        await vm.SaveAsync(Ct);
        Assert.Equal("Active until 2026-12-24", vm.Status);
        var again = new SrvUiVacationViewModel(service, null, new InlineDispatcher(), time);
        await again.LoadAsync(Ct);
        Assert.Equal("Back soon.", again.Body);
        again.ToText = "24 December";
        await again.SaveAsync(Ct);
        Assert.Equal("Enter days as yyyy-MM-dd.", again.Error);
    }

    [Fact]
    public async Task Identities_create_show_in_the_list_and_a_default_one_cannot_be_deleted()
    {
        var vm = new SrvUiIdentitiesViewModel(new SampleAccountIdentities(), null, new InlineDispatcher());
        await vm.LoadAsync(Ct);
        Assert.Equal(2, vm.Items.Count);
        vm.StartNew();
        vm.Email = "new@example.org";
        vm.Name = "New";
        await vm.SaveAsync(Ct);
        Assert.Equal(3, vm.Items.Count);
        Assert.Equal("new@example.org", vm.Email);
        vm.Select("i1");
        Assert.False(vm.CanDelete);
        await vm.DeleteAsync(Ct);
        Assert.Equal(3, vm.Items.Count);
        await vm.MoveAsync("i2", -1, Ct);
        Assert.Equal("i2", vm.Items[0].Id);
    }

    [Fact]
    public async Task Templates_are_created_listed_and_report_their_placeholders()
    {
        var vm = new SrvUiTemplatesViewModel(new SampleTemplateAdmin(), null, new InlineDispatcher());
        await vm.LoadAsync(Ct);
        Assert.Equal("Thanks", Assert.Single(vm.Items).Name);
        vm.StartNew();
        vm.Name = "Follow up";
        vm.Subject = "Re: {{topic}}";
        vm.Body = "Hi {{name}}, about {{topic}}.";
        Assert.Equal("name,topic", string.Join(',', vm.Placeholders.Order(StringComparer.Ordinal)));
        await vm.SaveAsync(Ct);
        Assert.Equal(2, vm.Items.Count);
    }

    [Fact]
    public async Task The_folders_page_keeps_prefs_and_refuses_a_move_under_itself()
    {
        var vm = new SrvUiFoldersViewModel(new SampleFolderAdmin(), new SrvUiSampleFolders(), null, new InlineDispatcher());
        await vm.LoadAsync(Ct);
        Assert.Equal("f-inbox", vm.Rows[0].Node.Id);
        vm.Select("f-projects");
        Assert.DoesNotContain(vm.MoveTargets(), n => n.Id is "f-projects" or "f-alpha" or "f-beta");
        Assert.True(await vm.SetPrefAsync(FolderAdminPref.Pin, "true", Ct));
        await vm.LoadAsync(Ct);
        Assert.Contains("f-projects", vm.Prefs.Pinned);
        Assert.False(await vm.CreateAsync("a/b", false, Ct));
    }

    [Fact]
    public async Task Recover_lists_and_restores_an_item()
    {
        var vm = new SrvUiRecoverViewModel(new SrvUiSampleRecover(), null, new InlineDispatcher());
        await vm.LoadAsync(Ct);
        Assert.Equal(3, vm.Items.Count);
        await vm.RestoreAsync("a2", Ct);
        Assert.Equal(2, vm.Items.Count);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}

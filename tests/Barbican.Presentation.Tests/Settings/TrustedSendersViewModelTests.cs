// I-209, G-053: the trusted senders list of Settings (Bulwark components/trusted-senders-modal.tsx; stores/settings-store.ts:2171-2201).

using Barbican.Core.Mail.Trust;
using Barbican.Presentation.Settings;

namespace Barbican.Presentation.Tests.Settings;

public sealed class TrustedSendersViewModelTests
{
    [Fact]
    public async Task Adding_keeps_the_address_lower_cased_and_refuses_a_duplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        var vm = new TrustedSendersViewModel(new TrustedSenders(null));
        Assert.True(vm.IsEmpty);
        Assert.Equal("None", vm.CountText);

        vm.BeginAdd();
        vm.NewAddress = " Anna@Shop.Example.Com ";
        Assert.True(await vm.AddAsync(ct));
        Assert.False(vm.IsAdding);
        Assert.Equal(["anna@shop.example.com"], vm.Addresses);
        Assert.Equal("1 sender", vm.CountText);

        vm.BeginAdd();
        vm.NewAddress = "ANNA@shop.example.com";
        Assert.False(await vm.AddAsync(ct));
        Assert.Equal(TrustedSendersRules.AlreadyAdded, vm.Error);
        Assert.Equal("1 sender", vm.CountText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not an address")]
    [InlineData("a@b")]
    [InlineData("@shop.example.com")]
    [InlineData("anna@shop")]
    public async Task A_malformed_address_is_refused_with_the_bulwark_wording(string address)
    {
        var vm = new TrustedSendersViewModel(new TrustedSenders(null));
        vm.BeginAdd();
        vm.NewAddress = address;
        Assert.False(await vm.AddAsync(TestContext.Current.CancellationToken));
        Assert.Equal(TrustedSendersRules.InvalidEmail, vm.Error);
        Assert.Empty(vm.Addresses);
    }

    [Fact]
    public async Task Removing_a_sender_takes_it_off_the_list()
    {
        var ct = TestContext.Current.CancellationToken;
        var trust = new TrustedSenders(null);
        Assert.True((await trust.AddAsync("a@example.org", ct)).IsOk);
        Assert.True((await trust.AddAsync("b@example.org", ct)).IsOk);
        var vm = new TrustedSendersViewModel(trust);

        Assert.True(await vm.RemoveAsync("A@Example.org", ct));
        Assert.Equal(["b@example.org"], vm.Addresses);
        Assert.Equal("1 sender", vm.CountText);

        // an address that is not there changes nothing (Bulwark filters the list) and reports no error
        Assert.True(await vm.RemoveAsync("missing@example.org", ct));
        Assert.Equal(["b@example.org"], vm.Addresses);
        Assert.Empty(vm.Error);
    }

    [Fact]
    public async Task Search_appears_with_five_senders_and_filters_the_list()
    {
        var ct = TestContext.Current.CancellationToken;
        var trust = new TrustedSenders(null);
        for (var i = 0; i < 4; i++)
        {
            Assert.True((await trust.AddAsync($"person{i}@shop.example", ct)).IsOk);
        }

        var vm = new TrustedSendersViewModel(trust);
        Assert.False(vm.ShowSearch);

        Assert.True((await trust.AddAsync("news@paper.example", ct)).IsOk);
        Assert.True(vm.ShowSearch);
        Assert.Equal(
            ["person0@shop.example", "person1@shop.example", "person2@shop.example", "person3@shop.example", "news@paper.example"],
            vm.Addresses);

        vm.Query = "PAPER";
        Assert.Equal(["news@paper.example"], vm.Addresses);
        vm.Query = "nobody";
        Assert.Empty(vm.Addresses);
        Assert.True(vm.NoResults);
        Assert.False(vm.IsEmpty);
    }
}

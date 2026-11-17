// ISecretStore: the fake, and the real OS stores on a throwaway profile (Barbican.Next/test-<guid>/…) that the test deletes again. ADR 0008:
// UTF-8 values, never under the Rust app's target names. The Linux store needs a Secret Service on the session bus; without one it is
// skipped (the VM's headless session has none unless gnome-keyring runs).

using Barbican.Platform.Linux;

namespace Barbican.Platform.Tests;

public sealed class SecretStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task RoundTripAsync(ISecretStore store)
    {
        var name = new SecretName("probe", "ana@example.org");
        try
        {
            Assert.Null((await store.ReadAsync(name, Ct)).Value);
            Assert.True((await store.WriteAsync(name, "first ✓ ünïcode", Ct)).IsOk);
            Assert.Equal("first ✓ ünïcode", (await store.ReadAsync(name, Ct)).Value);
            Assert.True((await store.WriteAsync(name, "second", Ct)).IsOk);
            Assert.Equal("second", (await store.ReadAsync(name, Ct)).Value);
        }
        finally
        {
            Assert.True((await store.DeleteAsync(name, Ct)).IsOk);
        }

        Assert.Null((await store.ReadAsync(name, Ct)).Value);
        Assert.True((await store.DeleteAsync(name, Ct)).IsOk);
    }

    [Fact]
    public async Task The_in_memory_store_round_trips()
    {
        var store = new InMemorySecretStore();
        await RoundTripAsync(store);
        Assert.Equal(2, store.Writes);
    }

    [Fact]
    public async Task The_windows_credential_store_round_trips_on_a_throwaway_profile()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
#if WINDOWS
        var store = new Barbican.Platform.Windows.WindowsSecretStore("test-" + Guid.NewGuid().ToString("N")[..12]);
        await RoundTripAsync(store);
#else
        await Task.CompletedTask;
#endif
    }

    [Fact]
    public async Task The_windows_store_refuses_a_value_over_the_blob_limit()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
#if WINDOWS
        var store = new Barbican.Platform.Windows.WindowsSecretStore("test-" + Guid.NewGuid().ToString("N")[..12]);
        var r = await store.WriteAsync(new SecretName("big"), new string('x', 4000), Ct);
        Assert.False(r.IsOk);
        Assert.Null((await store.ReadAsync(new SecretName("big"), Ct)).Value);
#else
        await Task.CompletedTask;
#endif
    }

    [Fact]
    public async Task The_secret_service_store_round_trips_when_a_service_runs()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") is { Length: > 0 }, "needs a Linux session bus");
        var store = new SecretServiceStore("test-" + Guid.NewGuid().ToString("N")[..12]);
        var probe = await store.ReadAsync(new SecretName("probe"), Ct);
        Assert.SkipWhen(!probe.IsOk, "no Secret Service on the session bus: " + probe.Error?.Message);
        await RoundTripAsync(store);
    }

    [Fact]
    public void Secret_service_attributes_name_the_profile_and_entry()
    {
        var a = new SecretServiceStore("dev").Attributes(new SecretName("refresh", "ana"));
        Assert.Equal(SecretServiceStore.Schema, a["schema"]);
        Assert.Equal("dev", a["profile"]);
        Assert.Equal("refresh", a["name"]);
        Assert.Equal("ana", a["account"]);
    }
}

// New tests (fable-review-2 R2-09; user decision D-10): plain http only for this computer, by address and the name localhost, never the
// name "loopback" that Uri.IsLoopback also accepts; a user part in the address is refused. Owner unified.

using Barbican.Core.Jmap;

namespace Barbican.Core.Tests.Jmap;

public sealed class ServerEndpointTests
{
    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("http://LOCALHOST")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://127.4.5.6")]
    [InlineData("http://[::1]:8080")]
    public void Plain_http_on_this_computer_is_accepted(string address) => Assert.True(ServerEndpoint.Read(address).IsOk, address);

    [Theory]
    [InlineData("http://loopback")]
    [InlineData("http://loopback:8080")]
    [InlineData("http://localhost.example.org")]
    [InlineData("http://128.0.0.1")]
    [InlineData("http://[::2]")]
    [InlineData("http://mail.example.org")]
    public void Plain_http_anywhere_else_is_refused(string address)
    {
        var r = ServerEndpoint.Read(address);
        Assert.False(r.IsOk, address);
        Assert.Equal(ServerEndpoint.InsecureMessage, r.Error!.Message);
        Assert.False(ServerEndpoint.IsLoopback(new Uri(address)));
    }

    [Theory]
    [InlineData("https://ana@mail.example.org")]
    [InlineData("https://ana:secret@mail.example.org")]
    [InlineData("ana@mail.example.org")]
    [InlineData("http://ana@localhost")]
    public void An_address_with_a_user_part_is_refused_with_a_plain_message(string address)
    {
        var r = ServerEndpoint.Read(address);
        Assert.False(r.IsOk, address);
        Assert.Equal(ServerEndpoint.UserInfoMessage, r.Error!.Message);
    }

    [Fact]
    public void An_https_address_without_a_user_part_is_kept()
    {
        var r = ServerEndpoint.Read("mail.example.org/");
        Assert.True(r.IsOk);
        Assert.Equal("https://mail.example.org/", r.Value!.BaseUri.ToString());
    }
}

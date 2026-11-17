// Ported from Rust src/oauth.rs tests (:418-:460: the two test modules). Red until the accounts stream implements OAuthRefresh.

using Barbican.Core.Auth;

namespace Barbican.Core.Tests.Auth;

public sealed class OAuthRefreshTests
{
    [Fact]
    [RustTest("oauth::rotated_elsewhere_retries_with_stored")]
    [Red("accounts")]
    public void Rotated_elsewhere_retries_with_stored()
    {
        Assert.Equal(new RetryWithStored("new"), OAuthRefresh.Decide("old", "new", true));
    }

    [Fact]
    [RustTest("oauth::unchanged_rejection_needs_sign_in")]
    [Red("accounts")]
    public void Unchanged_rejection_needs_sign_in()
    {
        Assert.Equal(new NeedsSignIn(), OAuthRefresh.Decide("old", "old", true));
        Assert.Equal(new NeedsSignIn(), OAuthRefresh.Decide("old", null, true));
        Assert.Equal(new NeedsSignIn(), OAuthRefresh.Decide("old", "", true));
    }

    [Fact]
    [RustTest("oauth::network_error_keeps_and_retries_later")]
    [Red("accounts")]
    public void Network_error_keeps_and_retries_later()
    {
        Assert.Equal(new TransientFailure(), OAuthRefresh.Decide("old", "new", false));
        Assert.Equal(new TransientFailure(), OAuthRefresh.Decide("old", "old", false));
        Assert.False(OAuthRefresh.IsRejection("error sending request"));
        Assert.True(OAuthRefresh.IsRejection("Token request failed (400 Bad Request): invalid_grant "));
        Assert.False(OAuthRefresh.IsRejection("Token request failed (503 Service Unavailable):  "));
    }

    [Fact]
    [RustTest("oauth::token_changed_retries_with_stored")]
    [Red("accounts")]
    public void Token_changed_retries_with_stored()
    {
        Assert.Equal(new RetryWithStored("new"), OAuthRefresh.Decide("old", "new", true));
    }

    [Fact]
    [RustTest("oauth::unchanged_invalid_grant_needs_sign_in")]
    [Red("accounts")]
    public void Unchanged_invalid_grant_needs_sign_in()
    {
        Assert.Equal(new NeedsSignIn(), OAuthRefresh.Decide("old", "old", true));
        Assert.Equal(new NeedsSignIn(), OAuthRefresh.Decide("old", null, true));
        Assert.Equal(new NeedsSignIn(), OAuthRefresh.Decide("old", "", true));
    }

    [Fact]
    [RustTest("oauth::network_error_is_transient")]
    [Red("accounts")]
    public void Network_error_is_transient()
    {
        Assert.Equal(new TransientFailure(), OAuthRefresh.Decide("old", "new", false));
        Assert.Equal(new TransientFailure(), OAuthRefresh.Decide("old", "old", false));
    }

    [Fact]
    [RustTest("oauth::rejection_detection")]
    [Red("accounts")]
    public void Rejection_detection()
    {
        Assert.True(OAuthRefresh.IsRejection("Token request failed (400 Bad Request): invalid_grant "));
        Assert.False(OAuthRefresh.IsRejection("error sending request: timed out"));
        Assert.False(OAuthRefresh.IsRejection("Token request failed (503): "));
    }
}

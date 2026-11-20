// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts oauth.createState, oauth.getRedirectUri and its own OAuth sign-in launch.
using Barbican.Core.Auth;
using Barbican.Core.Common;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Plugins.OAuth;

/// <summary>Exports the reference OAuth methods to the add-on dispatcher.</summary>
public static class PluginOAuthMethods
{
    public static IEnumerable<IPluginApiMethod> For(PluginOAuthCallbacks callbacks, PluginOAuthRedirect redirect,
        IPluginDeliveryAuthority authority, IAuthorizationLauncher? launcher,
        Func<PluginRuntimeBinding, Uri, CancellationToken, Task<bool>>? confirm)
    {
        yield return new OAuthMethod(callbacks, redirect, createState: true);
        yield return new OAuthMethod(callbacks, redirect, createState: false);
        if (launcher is not null && confirm is not null) yield return new LaunchMethod(callbacks, redirect, authority, launcher, confirm);
    }

    private sealed class OAuthMethod(PluginOAuthCallbacks callbacks, PluginOAuthRedirect redirect, bool createState) : IPluginApiMethod
    {
        public PluginApiDescriptor Descriptor { get; } = new(createState ? "oauth.createState" : "oauth.getRedirectUri",
            [[PluginOAuthCallbacks.Permission]], TimeSpan.FromSeconds(5));

        public PluginErrorCode? ValidateArguments(PluginValue arguments) =>
            arguments is { Kind: PluginValueKind.Array, Count: 0 } ? null : PluginErrorCode.InvalidArguments;

        public async Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue arguments, CancellationToken ct)
        {
            var value = createState
                ? await callbacks.CreateStateAsync(context.Binding, ct).ConfigureAwait(false)
                : context.Binding.IsAlive && !ct.IsCancellationRequested ? redirect.GetOrStart()?.AbsoluteUri : null;
            if (value is null) return PluginCallOutcome.Failed(PluginErrorCode.Failed);
            if (createState) redirect.RenewActive();
            var builder = new PluginValueBuilder();
            return PluginCallOutcome.Succeeded(builder.Build(builder.String(value)));
        }
    }

    private sealed class LaunchMethod(PluginOAuthCallbacks callbacks, PluginOAuthRedirect redirect,
        IPluginDeliveryAuthority authority, IAuthorizationLauncher launcher,
        Func<PluginRuntimeBinding, Uri, CancellationToken, Task<bool>> confirm) : IPluginApiMethod
    {
        public PluginApiDescriptor Descriptor { get; } = new("ui.openExternalUrl", [], TimeSpan.FromSeconds(60), mayMutate: true);

        public PluginErrorCode? ValidateArguments(PluginValue arguments) =>
            arguments is { Kind: PluginValueKind.Array, Count: 1 } && arguments.Item(0).Kind == PluginValueKind.String &&
            TryAuthorizationUri(arguments.Item(0).StringValue, out _) ? null : PluginErrorCode.InvalidArguments;

        public async Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue arguments, CancellationToken ct)
        {
            if (!TryAuthorizationUri(arguments.Item(0).StringValue, out var uri) ||
                LoopbackListener.ReadQuery(uri!.Query, rejectDuplicates: true) is not { } query ||
                !IsAuthorizationCodeRequest(query) ||
                !query.TryGetValue("state", out var state) || redirect.Current is not { } callback ||
                !string.Equals(query.GetValueOrDefault("redirect_uri"), callback.AbsoluteUri, StringComparison.Ordinal) ||
                !context.Demand(PluginOAuthCallbacks.Permission, null).IsOk)
                return PluginCallOutcome.Failed(PluginErrorCode.PermissionDenied);

            var binding = context.Binding;
            var claimed = false;
            if (!await authority.TryPostAsync(binding, grant =>
                Current(grant, binding, ct) && binding.TryWriteWhileAlive(() => claimed = callbacks.TryClaimLaunch(binding, state)),
                ct).ConfigureAwait(false) || !claimed)
                return PluginCallOutcome.Failed(PluginErrorCode.PermissionDenied);
            if (!await confirm(binding, uri, ct).ConfigureAwait(false))
                return PluginCallOutcome.Failed(PluginErrorCode.Cancelled);
            if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false))
                return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);

            Task<Result<Unit>>? launched = null;
            var accepted = await authority.TryPostAsync(binding, grant =>
            {
                if (!Current(grant, binding, ct)) return false;
                return binding.TryWriteWhileAlive(() =>
                {
                    if (!callbacks.OwnsState(binding, state) || redirect.Current != callback || !redirect.RenewActive()) return;
                    context.MarkMutationIssued();
                    launched = Task.Run(() => LaunchAsync(binding, uri, ct), CancellationToken.None);
                });
            }, ct).ConfigureAwait(false);
            if (!accepted || launched is null) return PluginCallOutcome.Failed(PluginErrorCode.PermissionDenied);
#pragma warning disable VSTHRD003 // The host launcher is queued on the pool after synchronous authority admission.
            var result = await launched.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            return result.IsOk ? PluginCallOutcome.Succeeded() : PluginCallOutcome.Failed(PluginErrorCode.Failed);
        }

        private Task<Result<Unit>> LaunchAsync(PluginRuntimeBinding binding, Uri uri, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!binding.IsAlive) throw new OperationCanceledException("The add-on closed before launch.");
            return launcher.OpenAsync(uri, ct);
        }

        private static bool Current(PluginGrantSnapshot grant, PluginRuntimeBinding binding, CancellationToken ct) =>
            !ct.IsCancellationRequested && grant.Tier == binding.Tier &&
            grant.Authorizes(binding.Package, binding.GrantGeneration, PluginOAuthCallbacks.Permission, null);

        private static bool IsAuthorizationCodeRequest(IReadOnlyDictionary<string, string> query) =>
            string.Equals(query.GetValueOrDefault("response_type"), "code", StringComparison.Ordinal) &&
            query.GetValueOrDefault("client_id") is { Length: > 0 and <= 1024 } client && !client.Any(char.IsControl) &&
            query.GetValueOrDefault("response_mode") is null or "query" &&
            string.Equals(query.GetValueOrDefault("code_challenge_method"), "S256", StringComparison.Ordinal) &&
            query.GetValueOrDefault("code_challenge") is { Length: 43 } challenge &&
            challenge.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

        private static bool TryAuthorizationUri(string value, out Uri? uri)
        {
            uri = null;
            return value.Length <= 8192 && Uri.TryCreate(value, UriKind.Absolute, out uri) && !uri.IsLoopback &&
                string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) && uri.HostNameType == UriHostNameType.Dns &&
                string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment);
        }
    }
}

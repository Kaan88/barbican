#if WINDOWS
// Mirrors: Bulwark components/email/reader-selection-menu.tsx:190-195 (PluginActions surface "reader-selection-menu", variant menu, context { emailId, selection: { text } }) and
// lib/plugin-sandbox/loader.ts:444-452; the guards are those of ReaderView.AddOns.cs (the exact running frame, the grant generation, the open message and account unchanged).
// RD-08. The rows an add-on put in 'reader-selection-menu' and running one on the selected text; the menu itself is Presentation's ReaderSelectionMenu.
using Barbican.App.Plugins.Sandbox;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Plugins.Slots;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Reader;

namespace Barbican.App.Plugins;

/// <summary>The add-on rows of the reader's selection menu.</summary>
internal sealed class ReaderSelectionAddOns(
    PluginActionRegistration registrations, PluginBackgroundRuntimes runtimes, PluginPackageHost packages, IPluginSessionSource sessions,
    IActiveMail active, ReaderViewModel reader) : IReaderSelectionAddOns
{
    private static readonly string[] Permissions = ["ui:context-menu", "ui:toolbar"];
    private const char KeySeparator = '\u001f';

    public IReadOnlyList<ReaderSelectionAddOn> Current()
    {
        if (OpenAccount(reader.Current) is not { } account)
        {
            return [];
        }

        return [.. Contributions(account).Select(c => new ReaderSelectionAddOn(KeyOf(c), c.Action.Label, c.Action.Icon, c.Action.Danger))];
    }

    public async Task<Result<Unit>> RunAsync(string key, string text, CancellationToken ct)
    {
        var snapshot = reader.Current;
        var mail = active.Mail;
        if (mail is null || OpenAccount(snapshot) is not { } account)
        {
            return Gone();
        }

        var action = Contributions(account).FirstOrDefault(c => string.Equals(KeyOf(c), key, StringComparison.Ordinal));
        if (action is null || RuntimeOf(action, account) is not var (runtime, permission) || runtime is not IPluginFencedHookRuntime fenced)
        {
            return Gone();
        }

        var captured = sessions.CaptureActive();
        if (!captured.IsOk || captured.Value is not { } owner || !string.Equals(owner.LocalAccountKey, account, StringComparison.Ordinal) || !ReferenceEquals(owner.Session.Mail, mail))
        {
            return Gone();
        }

        bool Current() => ReferenceEquals(reader.Current, snapshot) && ReferenceEquals(active.Mail, mail) && string.Equals(active.AccountKey, account, StringComparison.Ordinal);
        IReadOnlyList<PluginSessionCapture> owners = [owner];
        var lifetime = action.Owner.Lifetime;
        try
        {
            var installed = await packages.FindAsync(action.Owner.Package.Id, lifetime).ConfigureAwait(true);
            if (!installed.IsOk || installed.Value is not { Enabled: true, Grant: { Enabled: true } grant } ||
                grant.Package != action.Owner.Package || grant.Generation != action.Owner.GrantGeneration ||
                !grant.AccountKeys.Contains(account) || !grant.Permissions.Contains(permission) ||
                !Current() || !action.Owner.IsAlive || !sessions.IsCurrent(owner) || !registrations.Contains(action) ||
                !ReferenceEquals(runtimes.ActiveRuntime(action.Owner, account, permission), runtime))
            {
                return Gone();
            }

            var values = new PluginValueBuilder();
            // Upstream gives { emailId, selection: { text } }; projection drops both without email:read (spec/plugins/slots.csv).
            var context = grant.Permissions.Contains("email:read")
                ? values.Object(("emailId", values.String(snapshot.EmailId)), ("selection", values.Object(("text", values.String(text)))))
                : values.Object();
            var payload = values.Object(("surface", values.String(PluginActionRegistration.ReaderSelection)), ("actionId", values.String(action.Action.Id)), ("context", context));
            var result = await fenced.InvokeHookWhileCurrentAsync("onAction", values.Build(values.Array(payload)), TimeSpan.FromSeconds(30),
                write => sessions.TryRunWhileCurrent(owners, () => Current() && registrations.Contains(action) && write()), lifetime).ConfigureAwait(true);
            return result.IsOk ? Result<Unit>.Ok(default) : Result<Unit>.Fail(new ValidationError("The add-on action could not run."));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            return Gone();
        }
    }

    private static Result<Unit> Gone() => Result<Unit>.Fail(new ValidationError("The add-on action is no longer available."));

    private static string KeyOf(PluginActionContribution c) => c.Owner.Package.Id + KeySeparator + c.Action.Id;

    private string? OpenAccount(ReaderSnapshot snapshot) =>
        active.Mail is not null && snapshot.Body is not null && !string.IsNullOrEmpty(snapshot.EmailId) && snapshot.ListAccountKey is { } account &&
        string.Equals(account, active.AccountKey, StringComparison.Ordinal) ? account : null;

    private (IPluginRuntimeInstance Runtime, string Permission)? RuntimeOf(PluginActionContribution action, string account)
    {
        foreach (var permission in Permissions)
        {
            if (runtimes.ActiveRuntime(action.Owner, account, permission) is { } runtime && runtime.Activation.Hooks.Contains("onAction", StringComparer.Ordinal))
            {
                return (runtime, permission);
            }
        }

        return null;
    }

    private IEnumerable<PluginActionContribution> Contributions(string account) =>
        registrations.Actions(PluginActionRegistration.ReaderSelection).Where(action => RuntimeOf(action, account) is not null);
}
#endif

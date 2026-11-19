// Mirrors: Bulwark components/settings/settings-app.tsx (open a tab, back, forward, Show advanced settings), M6 plan section 3.4. Owner set-ui.

using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Shell;

namespace Barbican.Presentation.Settings.Ui;

/// <summary>Creates the settings commands.</summary>
public static class SetUiCommandFactory
{
    public static IReadOnlyList<IAppCommand> Create(ShellViewModel shell, ISettingsNavigation navigation, ISetUiViewModel screen, IDialogService dialogs, AccessibilityQuickModel quick)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(quick);
        return
        [
            new DelegateAppCommand(
                SetUiCommandIds.OpenPage,
                _ => true,
                (c, _) =>
                {
                    var arg = c.Argument ?? SettingsPageIds.Account;
                    var split = arg.IndexOf(':', StringComparison.Ordinal);
                    var page = split < 0 ? arg : arg[..split];
                    if (SettingsNav.Find(page) is null)
                    {
                        return Task.FromResult(Result<Unit>.Fail(new ValidationError("There is no settings page " + page + ".")));
                    }

                    if (!shell.TryActivate(AppId.Settings))
                    {
                        return Task.FromResult(Result<Unit>.Fail(new ValidationError("Settings is not available.")));
                    }

                    navigation.Open(page, split < 0 ? null : arg[(split + 1)..]);
                    return Task.FromResult(Result<Unit>.Ok(default));
                }),
            DelegateAppCommand.Sync(SetUiCommandIds.Back, c => { _ = navigation.Back(); }, _ => navigation.CanGoBack),
            DelegateAppCommand.Sync(SetUiCommandIds.Forward, c => { _ = navigation.Forward(); }, _ => navigation.CanGoForward),
            DelegateAppCommand.Sync(SetUiCommandIds.ToggleAdvanced, _ => screen.ShowAdvanced = !screen.ShowAdvanced),
            new DelegateAppCommand(CommandIds.OpenA11y, _ => true, async (context, ct) =>
            {
                _ = await dialogs.ShowAsync(new AccessibilityQuickRequest(quick), ct).ConfigureAwait(true);
                return Result<Unit>.Ok(default);
            }),
        ];
    }
}

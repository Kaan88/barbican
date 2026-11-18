// Holder so the DI registration builds the settings commands once. Owner set-ui.

using Barbican.Presentation.Commands;

namespace Barbican.Presentation.Settings.Ui;

/// <summary>The settings commands, created once.</summary>
public sealed record SetUiCommandSet(IReadOnlyList<IAppCommand> Commands);

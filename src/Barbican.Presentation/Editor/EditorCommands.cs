// Mirrors: M2 plan 4.1 (IAppCommand; a command id without a handler shows disabled); Rust src/rte/mod.rs on_cmd :707.

using Barbican.Core.Common;
using Barbican.Presentation.Commands;

namespace Barbican.Presentation.Editor;

/// <summary>One <see cref="IAppCommand"/> per editor command, acting on <see cref="ActiveEditor.Current"/>.</summary>
public static class EditorCommands
{
    public static IReadOnlyList<IAppCommand> Create(ActiveEditor active)
    {
        ArgumentNullException.ThrowIfNull(active);
        return [.. Enum.GetValues<EditorCommand>().Select(c => (IAppCommand)new DelegateAppCommand(
            EditorCommandIds.For(c),
            _ => CanRun(active.Current, c),
            (ctx, _) =>
            {
                active.Current?.Execute(c, ctx.Argument);
                return Task.FromResult(Result<Unit>.Ok(default));
            }))];
    }

    /// <summary>Evaluated when a menu opens (no stale state): the table commands only in a table, plain-text mode only the editing ones, undo and redo by history.</summary>
    public static bool CanRun(IEditorSurface? editor, EditorCommand command)
    {
        if (editor is null)
        {
            return false;
        }

        if (editor.Plain && !EditorCommandRunner.AllowedInPlain(command))
        {
            return false;
        }

        var t = editor.Toolbar;
        return command switch
        {
            EditorCommand.Undo => t.CanUndo,
            EditorCommand.Redo => t.CanRedo,
            EditorCommand.Cut or EditorCommand.Copy => t.HasSelection,
            EditorCommand.TableRowAbove or EditorCommand.TableRowBelow or EditorCommand.TableColLeft or EditorCommand.TableColRight or EditorCommand.TableDelRow
                or EditorCommand.TableDelCol or EditorCommand.TableToggleHeader or EditorCommand.TableDelete => t.InTable,
            EditorCommand.ImageSize => t.OnImage,
            EditorCommand.LinkOpen or EditorCommand.LinkCopy or EditorCommand.LinkRemove => t.Link is not null,
            _ => true,
        };
    }
}

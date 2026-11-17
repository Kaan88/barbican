#if AUTOMATION
// Mirrors: spec/automation-protocol.md (composer commands, M3 composer-ui): compose-open, compose-state, compose-type, compose-key, compose-set, compose-do, compose-dialog, mailto. No input is produced; the commands drive the view models the
// composer draws (ADR 0007), on the UI thread, and answer one line. The sample services (BARBICAN_SAMPLE=1 only) stand in for the send pipeline so a hidden run can compose, autosave, discard and send without a server.

using System.Globalization;
using Barbican.App.Views.Menus.Automation;
using Barbican.Automation;
using Barbican.Core.Compose;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Mail;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Views.Compose.Automation;

/// <summary>Registers the composer's automation commands (and, in a sample run, its in-memory pipeline).</summary>
public static class ComposerAutomation
{
    public static IServiceCollection AddBarbicanComposerAutomation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE"), "1", StringComparison.Ordinal))
        {
            SampleComposeServices.Register(services);
        }

        Add(services, "compose-open", ComposeOpenAsync);
        Add(services, "compose-state", (sp, _) => Task.FromResult(Describe(sp.GetRequiredService<IComposerService>())));
        Add(services, "compose-type", (sp, args) => Task.FromResult(Type(sp, args)));
        Add(services, "compose-key", (sp, args) => Task.FromResult(Key(sp, args)));
        Add(services, "compose-set", (sp, args) => Task.FromResult(SetField(sp, args)));
        Add(services, "compose-dialog", (sp, args) => Task.FromResult(Dialog(sp, args)));
        Add(services, "compose-do", DoAsync);
        Add(services, "mailto", async (sp, args) => await sp.GetRequiredService<MailtoRouter>().HandleAsync(args.Trim(), CancellationToken.None) ? "ok" : "error not opened");
        return services;
    }

    private static void Add(IServiceCollection services, string name, Func<IServiceProvider, string, Task<string>> run) =>
        services.AddSingleton<IAutomationCommand>(sp => new LambdaAutomationCommand(name, args => run(sp, args)));

    private static ComposerViewModel? Current(IServiceProvider sp) => sp.GetRequiredService<IComposerService>().Current;

    private static string Text(string raw) => raw.Replace("\\n", "\n", StringComparison.Ordinal);

    // compose-open [new|reply|replyall|forward|draft ID|to ADDRESS]
    private static async Task<string> ComposeOpenAsync(IServiceProvider sp, string args)
    {
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : "new";
        var arg = parts.Length > 1 ? parts[1] : null;
        var key = sp.GetRequiredService<IActiveMail>().AccountKey;
        if (key is null)
        {
            return "error no account";
        }

        var service = sp.GetRequiredService<IComposerService>();
        var request = verb switch
        {
            "reply" => new ComposeRequest(key, ComposeKind.Reply) { SourceEmailId = arg },
            "replyall" => new ComposeRequest(key, ComposeKind.ReplyAll) { SourceEmailId = arg },
            "forward" => new ComposeRequest(key, ComposeKind.Forward) { SourceEmailId = arg },
            "draft" => new ComposeRequest(key, ComposeKind.EditDraft) { SourceEmailId = arg },
            "to" => new ComposeRequest(key, ComposeKind.New) { Prefill = new ComposePrefill { To = arg ?? string.Empty } },
            _ => new ComposeRequest(key, ComposeKind.New),
        };
        await service.OpenAsync(request, CancellationToken.None);
        return service.IsOpen ? "ok" : "error not opened";
    }

    private static string Describe(IComposerService service)
    {
        if (service.Current is not { } c)
        {
            return "closed";
        }

        var attachments = string.Join(" | ", c.Attachments.Select(a => $"{a.Name}:{a.State}"));
        var dialog = c.Dialog is { } d ? $"{d.Id}[{string.Join(",", d.Choices.Select(x => x.Id))}]{{{string.Join(" / ", d.Lines)}}}" : "none";
        return string.Create(CultureInfo.InvariantCulture, $"open kind={c.Kind} docked={c.IsDocked} from=\"{c.FromText}\" to=[{Chips(c.To)}] cc=[{Chips(c.Cc)}] bcc=[{Chips(c.Bcc)}] toinput=\"{c.To.Input}\" sugg={c.To.Suggestions.Count} hl={c.To.Highlight} subject=\"{c.Subject}\" body=\"{BodyOf(c)}\" status=\"{c.Status}\" attachments=[{attachments}] draft={c.DraftId ?? "none"} dialog={dialog} schedule={(c.Schedule is null ? "no" : "yes")} templates={(c.Templates is null ? "no" : c.Templates.Items.Count.ToString(CultureInfo.InvariantCulture))} sending={c.IsSending}");
    }

    private static string BodyOf(ComposerViewModel c) => c.BodyText().Replace("\n", "\\n", StringComparison.Ordinal);

    private static string Chips(RecipientFieldViewModel f) => string.Join(" | ", f.Chips.Select(x => (x.Kind == ChipKind.Invalid ? "!" : x.Kind == ChipKind.Group ? "#" : string.Empty) + x.Full));

    private static RecipientFieldViewModel? FieldOf(ComposerViewModel c, string name) => name.ToLowerInvariant() switch
    {
        "to" => c.To,
        "cc" => c.Cc,
        "bcc" => c.Bcc,
        _ => null,
    };

    // compose-type FIELD TEXT: text typed into a recipient input (the autocomplete follows)
    private static string Type(IServiceProvider sp, string args)
    {
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (Current(sp) is not { } c || parts.Length < 2 || FieldOf(c, parts[0]) is not { } f)
        {
            return "error usage: compose-type to|cc|bcc TEXT (a composer must be open)";
        }

        f.Edited(Text(parts[1]));
        return "ok " + f.Suggestions.Count + " suggestions";
    }

    // compose-key FIELD KEY: Enter, Tab, Escape, ArrowDown, ArrowUp, Backspace, or a typed character
    private static string Key(IServiceProvider sp, string args)
    {
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (Current(sp) is not { } c || parts.Length < 2 || FieldOf(c, parts[0]) is not { } f)
        {
            return "error usage: compose-key to|cc|bcc KEY";
        }

        return f.Key(parts[1], false) ? "handled" : "not handled";
    }

    // compose-set subject|body|from-tag|plain|receipt|priority VALUE
    private static string SetField(IServiceProvider sp, string args)
    {
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (Current(sp) is not { } c || parts.Length < 1)
        {
            return "error usage: compose-set subject|body|tag|plain|receipt|priority VALUE (a composer must be open)";
        }

        var value = parts.Length > 1 ? Text(parts[1]) : string.Empty;
        switch (parts[0].ToLowerInvariant())
        {
            case "subject":
                c.Subject = value;
                break;
            case "body":
                if (c.Editor is { } editor)
                {
                    editor.Session.Load(Barbican.Core.Editor.Doc.FromText(value));
                }
                else
                {
                    c.FallbackBody = value;
                }

                break;
            case "tag":
                c.SubAddressTag = value;
                break;
            case "plain":
                c.PlainText = value is "1" or "on" or "true";
                break;
            case "receipt":
                c.ReadReceipt = value is "1" or "on" or "true";
                break;
            case "priority":
                c.Priority = Enum.TryParse<MessagePriority>(value, true, out var p) ? p : MessagePriority.Normal;
                break;
            default:
                return "error unknown field";
        }

        return "ok";
    }

    // compose-dialog: the question on screen; compose-dialog ID answers it (esc cancels)
    private static string Dialog(IServiceProvider sp, string args)
    {
        if (Current(sp) is not { } c)
        {
            return "error no composer";
        }

        var answer = args.Trim();
        if (answer.Length == 0)
        {
            return c.Dialog is { } d ? $"{d.Id}: {d.Title} | {string.Join(" / ", d.Lines)} | {string.Join(",", d.Choices.Select(x => x.Id))}" : "none";
        }

        if (c.Dialog is null)
        {
            return "error no question";
        }

        c.AnswerDialog(string.Equals(answer, "esc", StringComparison.OrdinalIgnoreCase) ? null : answer);
        return "ok";
    }

    // compose-do VERB [ARG]: send | schedule [N|DATE TIME] | save | discard | close | flush | templates | template N | tpl-save NAME | cc | bcc | pick N | chip-remove FIELD N | attach NAME TEXT | cancel-upload N | tick | undo-toast
    private static async Task<string> DoAsync(IServiceProvider sp, string args)
    {
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var service = sp.GetRequiredService<IComposerService>();
        if (parts.Length == 0 || service.Current is not { } c)
        {
            return "error usage: compose-do VERB [ARG] (a composer must be open)";
        }

        var arg = parts.Length > 1 ? parts[1] : string.Empty;
        switch (parts[0].ToLowerInvariant())
        {
            case "send":
                return await c.SendAsync() ? "sent" : "not sent";
            case "schedule":
                return await ScheduleAsync(c, arg);
            case "save":
                return await c.SaveDraftAsync(close: false) ? "saved" : "not saved";
            case "discard":
                return await c.DiscardAsync() ? "closed" : "kept";
            case "close":
                return await service.TryCloseAsync(CancellationToken.None) ? "closed" : "kept";
            case "flush":
                await service.FlushAsync(CancellationToken.None);
                return "ok";
            case "tick":
                c.Tick();
                return "ok";
            case "templates":
                await c.OpenTemplatesAsync();
                return c.Templates is null ? "error no templates" : $"ok {c.Templates.Items.Count} templates: {string.Join(" | ", c.Templates.Items.Select(t => t.Name))}";
            case "template" when c.Templates is { } picker && int.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 0 && n < picker.Items.Count:
                await picker.InsertAsync(picker.Items[n]);
                return "ok";
            case "tpl-search" when c.Templates is { } picker:
                picker.Search = arg;
                return $"ok {picker.Items.Count} templates";
            case "tpl-save" when c.Templates is { } picker:
                picker.SaveName = arg;
                await picker.SaveAsync();
                return picker.Error ?? "ok";
            case "cc":
                c.RevealCc();
                return "ok";
            case "bcc":
                c.RevealBcc();
                return "ok";
            case "pick" when int.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out var i):
                c.To.Pick(i);
                return "ok";
            case "chip-remove":
                return ChipRemove(c, arg);
            case "attach":
                return await AttachAsync(c, arg);
            case "cancel-upload" when int.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out var u) && u >= 0 && u < c.Attachments.Count:
                c.RemoveAttachment(c.Attachments[u]);
                return "ok";
            default:
                return "error unknown verb";
        }
    }

    private static string ChipRemove(ComposerViewModel c, string arg)
    {
        var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 2 || FieldOf(c, p[0]) is not { } f || !int.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out var i))
        {
            return "error usage: compose-do chip-remove to|cc|bcc N";
        }

        f.Remove(i);
        return "ok";
    }

    private static async Task<string> AttachAsync(ComposerViewModel c, string arg)
    {
        var p = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (p.Length == 0)
        {
            return "error usage: compose-do attach NAME [TEXT]";
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(p.Length > 1 ? Text(p[1]) : "sample attachment");
        var add = c.AddFilesAsync([new AttachmentUpload(p[0], "text/plain", bytes)]);
        await Task.WhenAny(add, Task.Delay(1500));
        return "ok";
    }

    private static async Task<string> ScheduleAsync(ComposerViewModel c, string arg)
    {
        var open = c.OpenScheduleAsync();
        await Task.Yield();
        if (c.Schedule is not { } state)
        {
            return "error no schedule dialog";
        }

        if (string.Equals(arg, "cancel", StringComparison.OrdinalIgnoreCase))
        {
            state.Cancel();
            return "cancelled";
        }

        if (arg.Length == 0)
        {
            return $"dialog {state.Presets.Count} presets: {string.Join(" | ", state.Presets.Select(p => p.Label))}";
        }

        if (int.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
        {
            state.Choose(n);
        }
        else
        {
            var dt = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            state.Date = dt[0];
            state.Time = dt.Length > 1 ? dt[1] : string.Empty;
            state.ChooseTyped();
        }

        if (state.Error.Length > 0)
        {
            return "error " + state.Error;
        }

        return await open ? "scheduled" : "not sent";
    }
}
#endif

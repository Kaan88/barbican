// Reference: lib/plugin-sandbox/protocol.ts:14-106,115-336; host-theme.ts:17-24;
// plugin-types.ts:26,1036-1067; plugin-slot-catalog.ts:51-130.
using System.Globalization;

namespace Barbican.Core.Plugins.Protocol.Messages;

/// <summary>Validates an already bounded immutable clone graph. Does not authenticate or authorize messages.</summary>
public static class PluginMessageValidator
{
    public static bool Validate(PluginValue message, PluginMessageDirection direction, PluginMessageLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        var p = limits ?? new();
        if (p.MaxStringLength < 0 || p.MaxIdLength < 1 || p.MaxSequenceLength < 0 || p.MaxOperations < 1 || p.MaxGraphNodes < 1 || p.MaxGraphEdges < 0 || p.MaxGraphStringLength < 0 || p.MaxGraphBinaryBytes < 0) return false;
        return GraphBounded(message, p) && new Check(p).Message(message, direction);
    }

    private static bool GraphBounded(PluginValue root, PluginMessageLimits limits)
    {
        var seen = new HashSet<int>();
        var pending = new Stack<int>();
        pending.Push(root.Id);
        long edges = 0, strings = 0, binary = 0;
        while (pending.Count > 0)
        {
            var id = pending.Pop();
            if (!seen.Add(id)) continue;
            if (seen.Count > limits.MaxGraphNodes) return false;
            var node = root.Nodes[id];
            strings += node.Text?.Length ?? 0;
            if (node.Keys is not null) foreach (var key in node.Keys) strings += key.Length;
            binary += node.Bytes?.Length ?? 0;
            if (strings > limits.MaxGraphStringLength || binary > limits.MaxGraphBinaryBytes) return false;
            if (node.Links is null) continue;
            edges += node.Links.Length;
            if (edges > limits.MaxGraphEdges) return false;
            foreach (var link in node.Links) pending.Push(link);
        }
        return true;
    }
    private sealed class Check(PluginMessageLimits limits)
    {
        private int _ops = limits.MaxOperations;
        private bool Tick() => --_ops >= 0;
        private bool Obj(PluginValue x) => Tick() && x.Kind == PluginValueKind.Object && x.Count <= limits.MaxSequenceLength;
        private bool Str(PluginValue x) => Tick() && x.Kind == PluginValueKind.String && x.StringValue.Length <= limits.MaxStringLength;
        private bool Id(PluginValue x) => Str(x) && x.StringValue.Length is > 0 && x.StringValue.Length <= limits.MaxIdLength;
        private bool Bool(PluginValue x) => Tick() && x.Kind == PluginValueKind.Boolean;
        private bool Number(PluginValue x) => Tick() && x.Kind == PluginValueKind.Number && double.TryParse(x.NumberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n);
        private bool Any(PluginValue x) => Tick();
        private bool Array(PluginValue x, Func<PluginValue, bool> item)
        {
            if (!Tick() || x.Kind != PluginValueKind.Array || x.Count > limits.MaxSequenceLength) return false;
            for (var i = 0; i < x.Count; i++) if (!item(x.Item(i))) return false;
            return true;
        }
        private bool Required(PluginValue x, string key, Func<PluginValue, bool> test) => Obj(x) && x.PropertyNames.Contains(key, StringComparer.Ordinal) && test(x.Property(key));
        private bool Optional(PluginValue x, string key, Func<PluginValue, bool> test) => Obj(x) && (!x.PropertyNames.Contains(key, StringComparer.Ordinal) || x.Property(key).Kind == PluginValueKind.Undefined || test(x.Property(key)));
        private bool Strings(PluginValue x) => Array(x, Str);
        private bool Values(PluginValue x) => Array(x, Any);
        private bool StringMap(PluginValue x) => Map(x, Str);
        private bool Locales(PluginValue x) => Map(x, StringMap);
        private bool Map(PluginValue x, Func<PluginValue, bool> value)
        {
            if (!Obj(x)) return false;
            foreach (var key in x.PropertyNames) if (key.Length > limits.MaxStringLength || !value(x.Property(key))) return false;
            return true;
        }
        private bool Regional(PluginValue x) => Required(x, "locale", Str) && Required(x, "dateLocale", Str) && Required(x, "hour12", Bool) && Required(x, "timeZone", Str) && Required(x, "firstDayOfWeek", Day);
        private bool Day(PluginValue x) => Number(x) && (double.Parse(x.NumberText, CultureInfo.InvariantCulture) is 0 or 1 or 6);
        private bool Theme(PluginValue x) => Required(x, "dark", Bool) && Required(x, "fontFamily", Str) && Required(x, "vars", StringMap);
        private bool Features(PluginValue x) => Required(x, "externalSenderLabel", Bool) && Required(x, "externalRecipientWarning", Bool) && Required(x, "noThirdPartyTraffic", Bool) && Optional(x, "keystore", Bool) && Optional(x, "blockExternalContent", Bool) && Optional(x, "attachmentText", Bool) && Optional(x, "attachmentImages", Bool) && Optional(x, "threads", Bool) && Optional(x, "listSelection", Bool) && Optional(x, "answerInvitation", Bool) && Optional(x, "filterRules", Bool) && Optional(x, "batchMail", Bool) && Optional(x, "changeSettings", Bool) && Optional(x, "files", Bool) && Optional(x, "sidePanel", Bool);
        private bool Manifest(PluginValue x) => Required(x, "id", Id) && Required(x, "version", Str) && Required(x, "permissions", Strings) && Required(x, "settings", Obj) && Optional(x, "locales", Locales) && Optional(x, "httpOrigins", Strings);
        private bool Init(PluginValue x)
        {
            if (!Required(x, "mode", Str) || !Required(x, "pluginId", Id) || !Required(x, "tier", Tier) || !Required(x, "manifest", Manifest) || !Required(x, "code", Str) || !Required(x, "locale", Str) || !Optional(x, "regional", Regional) || !Optional(x, "hostFeatures", Features)) return false;
            return x.Property("mode").StringValue switch { "background" => true, "slot" => Required(x, "slot", Slot) && Optional(x, "fill", Bool) && Required(x, "extraProps", Obj) && Required(x, "theme", Theme), _ => false };
        }
        private bool Tier(PluginValue x) => Str(x) && x.StringValue is "untrusted" or "privileged";
        private bool Slot(PluginValue x) => Str(x) && Slots.Contains(x.StringValue);
        private bool SlotClaim(PluginValue x) => Required(x, "name", Slot) && Required(x, "hasShouldShow", Bool) && Required(x, "order", Number) && Optional(x, "fill", Bool) && Optional(x, "startFolded", Bool);
        private bool Shortcut(PluginValue x) => Required(x, "id", Id) && Required(x, "keys", Str) && Required(x, "label", Str) && Optional(x, "category", Str);
        private bool Rect(PluginValue x) => Required(x, "left", Number) && Required(x, "top", Number) && Required(x, "bottom", Number) && Required(x, "width", Number) && Required(x, "height", Number);
        private bool Option(PluginValue x) => Required(x, "value", Str) && Required(x, "label", Str) && Optional(x, "disabled", Bool);
        private bool Options(PluginValue x) => Array(x, Option);
        private bool Response(PluginValue x) => Required(x, "id", Id) && Required(x, "ok", Bool) && Optional(x, "result", Any) && Optional(x, "error", Str);
        private bool Dropdown(PluginValue x, bool open) => Required(x, "id", Id) && (open ? Required(x, "rect", Rect) : Optional(x, "rect", Rect)) && (open ? Required(x, "options", Options) : Optional(x, "options", Options)) && Optional(x, "multiple", Bool) && (open ? Required(x, "selectedValues", Strings) : Optional(x, "selectedValues", Strings)) && Optional(x, "singleValue", Str) && (open ? Required(x, "highlightedIndex", Number) : Optional(x, "highlightedIndex", Number)) && (!open || Optional(x, "ariaLabel", Str));
        public bool Message(PluginValue x, PluginMessageDirection direction)
        {
            if (!Required(x, "type", Str)) return false;
            var t = x.Property("type").StringValue;
            if (direction == PluginMessageDirection.SandboxToHost) return t switch
            {
                "sandbox-ready" or "frame-escape" => true,
                "init-done" => Required(x, "hooks", Strings) && Required(x, "slots", y => Array(y, SlotClaim)) && Required(x, "shortcuts", y => Array(y, Shortcut)),
                "init-error" => Required(x, "error", Str),
                "api-request" => Required(x, "id", Id) && Required(x, "method", Str) && Required(x, "args", Values),
                "callback-invoke" => Required(x, "id", Id) && Required(x, "callbackId", Id) && Required(x, "args", Values),
                "hook-result" => Response(x),
                "theme-applied" => Required(x, "dark", Bool),
                "slot-resize" => Required(x, "height", Number) && Optional(x, "width", Number),
                "slot-should-show-result" => Required(x, "id", Id) && Required(x, "show", Bool),
                "floating-dropdown-open" => Dropdown(x, true), "floating-dropdown-update" => Dropdown(x, false),
                "floating-dropdown-close" => Required(x, "id", Id), _ => false,
            };
            if (direction != PluginMessageDirection.HostToSandbox) return false;
            return t switch
            {
                "init" => Required(x, "payload", Init),
                "api-response" or "callback-response" => Response(x),
                "hook-invoke" => Required(x, "id", Id) && Required(x, "hookName", Str) && Required(x, "args", Values),
                "locale-change" => Required(x, "locale", Str) && Optional(x, "regional", Regional) && Optional(x, "locales", Locales),
                "theme-change" => Required(x, "theme", Theme), "settings-change" => Required(x, "settings", Obj),
                "host-change" => Required(x, "features", Features), "props-update" => Required(x, "props", Obj),
                "slot-should-show" => Required(x, "id", Id) && Required(x, "slot", Slot) && Required(x, "context", Any),
                "stream-chunk" => Required(x, "streamId", Id) && Required(x, "chunk", y => Tick() && y.Kind == PluginValueKind.ArrayBuffer),
                "stream-end" => Required(x, "streamId", Id) && Optional(x, "error", Str),
                "stream-progress" => Required(x, "streamId", Id) && Required(x, "loaded", Number) && Required(x, "total", Number),
                "floating-dropdown-select" => Required(x, "id", Id) && Required(x, "option", Option),
                "floating-dropdown-highlight" => Required(x, "id", Id) && Required(x, "index", Number),
                "floating-dropdown-dismiss" => Required(x, "id", Id), _ => false,
            };
        }
    }
    private static readonly HashSet<string> Slots = new(StringComparer.Ordinal)
    {
        "app-top-banner",
        "plugin-dialog",
        "command-palette",
        "app-side-panel",
        "navigation-rail-bottom",
        "sidebar-widget",
        "sidebar-section",
        "side-panel",
        "sidebar-section-actions",
        "top-bar-actions",
        "app-page",
        "message-list-header",
        "message-list-empty",
        "message-row-actions",
        "message-bulk-actions",
        "context-menu-email",
        "toolbar-actions",
        "search-results",
        "reader-header",
        "reader-below-subject",
        "email-banner",
        "reader-above-quote",
        "email-footer",
        "thread-summary",
        "reader-actions",
        "email-detail-sidebar",
        "email-details-section",
        "attachment-actions",
        "reader-selection-menu",
        "attachment-menu",
        "composer-toolbar",
        "composer-above-body",
        "composer-below-body",
        "composer-sidebar",
        "composer-sidebar-right",
        "composer-attachment-source",
        "composer-send-menu",
        "composer-attach-menu",
        "composer-options-menu",
        "composer-toolbar-actions",
        "composer-header-actions",
        "composer-selection-menu",
        "calendar-event-actions",
        "calendar-event-detail",
        "calendar-event-form",
        "calendar-day-header",
        "mini-calendar-marks",
        "contact-cryptokeys",
        "contact-detail-section",
        "contact-list-actions",
        "task-detail-section",
        "task-list-actions",
        "calendar-task-view",
        "file-actions",
        "file-preview-section",
        "settings-section",
        "settings-page",
        "admin-plugin-page",
    };
}

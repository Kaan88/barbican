// The card a saved form builds: the properties the form owns, made from the form's single fields and entry rows on top of the card's own entries so what the form does not show survives (Rust contacts_x.rs:190 build_card; Bulwark contact-form).

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Contacts;

public static partial class ContactFields
{
    private static readonly string[] OwnedNameKinds = ["title", "given", "given2", "surname", "generation", "prefix", "suffix", "middle", "additional", "separator"];

    /// <summary>
    /// The form's properties as a patch of the card: the original's entries with the edits merged in (unknown properties of an entry, the name components the form has no field for and every untouched entry kept), new entries get the next free key,
    /// a property the form emptied is null (removed), bad input (an address without an @, a date that is not a date) is a ValidationError. Properties the form does not own (kind, uid, media, keywords, ...) are not in the result.
    /// </summary>
    public static Result<JsonObject> Build(JsonNode? original, ContactForm form, IReadOnlyList<ContactField> fields)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(fields);
        var o = original ?? new JsonObject();
        var output = new JsonObject
        {
            ["name"] = NameOf(o, form),
            ["nicknames"] = FirstEdit(o, "nicknames", "k", form.Nickname.Trim() is { Length: > 0 } nick ? new JsonObject { ["@type"] = "Nickname", ["name"] = nick } : null),
        };

        output["organizations"] = OrganizationsOf(o, form.Org.Trim(), form.Dept.Trim());
        output["titles"] = TitlesOf(o, form);
        var note = form.Notes.TrimEnd();
        output["notes"] = FirstEdit(o, "notes", "n", note.Trim().Length > 0 ? new JsonObject { ["@type"] = "Note", ["note"] = note } : null);

        var sections = new (string Section, string Map, string Prefix, Func<ContactField, JsonObject, (JsonObject? Entry, string? Problem)> Make)[]
        {
            ("email", "emails", "e", MakeEmail),
            ("phone", "phones", "p", MakePhone),
            ("address", "addresses", "a", MakeAddress),
            ("url", "links", "l", MakeLink),
            ("im", "onlineServices", "s", MakeOnline),
            ("date", "anniversaries", "a", MakeDate),
        };
        foreach (var (section, map, prefix, make) in sections)
        {
            var built = Section(o, fields, section, map, prefix, make);
            if (!built.IsOk)
            {
                return Result<JsonObject>.Fail(built.Error!);
            }

            output[map] = built.Value;
        }

        output["relatedTo"] = Related(o, fields);
        return Result<JsonObject>.Ok(output);
    }

    private static JsonObject? NameOf(JsonNode o, ContactForm f)
    {
        var comps = new JsonArray();
        foreach (var (kind, v) in new[] { ("title", f.Prefix), ("given", f.Given), ("given2", f.Middle), ("surname", f.Surname), ("generation", f.Suffix) })
        {
            if (v.Trim().Length > 0)
            {
                comps.Append(new JsonObject { ["@type"] = "NameComponent", ["kind"] = kind, ["value"] = v.Trim() });
            }
        }

        if (ContactJson.Get(ContactJson.Get(o, "name"), "components") is JsonArray old)
        {
            foreach (var c in old)
            {
                if (c is not null && !OwnedNameKinds.Contains(c.Str("kind") ?? string.Empty, StringComparer.Ordinal))
                {
                    comps.Append(c.DeepClone());
                }
            }
        }

        var full = string.Join(' ', new[] { f.Prefix, f.Given, f.Middle, f.Surname, f.Suffix }.Select(s => s.Trim()).Where(s => s.Length > 0));
        if (comps.Count == 0 && full.Length == 0)
        {
            return null;
        }

        var n = ContactJson.Get(o, "name") is JsonObject existing ? (JsonObject)existing.DeepClone() : [];
        n["@type"] = "Name";
        n["components"] = comps;
        n["isOrdered"] = true;
        n["full"] = full;
        _ = n.Remove("sortAs");
        return n;
    }

    /// <summary>The first organisation edited in place: its name and its first unit (the form's department); other units, other organisations and unknown properties are kept.</summary>
    private static JsonObject? OrganizationsOf(JsonNode o, string org, string dept)
    {
        var m = ContactJson.Get(o, "organizations") is JsonObject existing ? (JsonObject)existing.DeepClone() : [];
        var first = m.Select(kv => kv.Key).FirstOrDefault();
        if (org.Length == 0 && dept.Length == 0)
        {
            if (first is not null)
            {
                _ = m.Remove(first);
            }

            return m.Count == 0 ? null : m;
        }

        if (first is null)
        {
            first = ContactJson.FreeKey(m, "o");
            m[first] = new JsonObject { ["@type"] = "Organization" };
        }

        var e = m[first] as JsonObject ?? [];
        e["name"] = org;
        var units = e["units"] as JsonArray ?? [];
        if (dept.Length > 0)
        {
            if (units.Count > 0 && units[0] is JsonObject unit)
            {
                unit["name"] = dept;
            }
            else
            {
                units.Insert(0, new JsonObject { ["@type"] = "OrgUnit", ["name"] = dept });
            }

            e["units"] = units;
        }
        else if (units.Count > 0)
        {
            units.RemoveAt(0);
            _ = e.Remove("units");
            if (units.Count > 0)
            {
                e["units"] = units;
            }
        }

        m[first] = e;
        return m;
    }

    /// <summary>The first entry of a map edited in place, the rest kept: with a value the entry takes its properties, without one it goes; null when nothing is left.</summary>
    private static JsonObject? FirstEdit(JsonNode o, string map, string prefix, JsonObject? value)
    {
        var m = ContactJson.Get(o, map) is JsonObject existing ? (JsonObject)existing.DeepClone() : [];
        var first = m.Select(kv => kv.Key).FirstOrDefault();
        if (first is not null && value is not null)
        {
            var e = m[first] is JsonObject entry ? entry : [];
            foreach (var (k, v) in value)
            {
                e[k] = v?.DeepClone();
            }

            m[first] = e;
        }
        else if (first is not null)
        {
            _ = m.Remove(first);
        }
        else if (value is not null)
        {
            m[ContactJson.FreeKey(m, prefix)] = ContactJson.Clean(value);
        }

        return m.Count == 0 ? null : m;
    }

    /// <summary>One title and one role (Bulwark t0 and t1), the other titles kept.</summary>
    private static JsonObject? TitlesOf(JsonNode o, ContactForm f)
    {
        var titles = ContactJson.Get(o, "titles") is JsonObject existing ? (JsonObject)existing.DeepClone() : [];
        foreach (var (kind, v) in new[] { ("title", f.JobTitle.Trim()), ("role", f.Role.Trim()) })
        {
            var k = titles.FirstOrDefault(kv => string.Equals(kv.Value.Str("kind") ?? "title", kind, StringComparison.Ordinal)).Key;
            if (k is not null)
            {
                if (v.Length == 0)
                {
                    _ = titles.Remove(k);
                }
                else if (titles[k] is JsonObject t)
                {
                    t["name"] = v;
                }
            }
            else if (v.Length > 0)
            {
                titles[ContactJson.FreeKey(titles, "t")] = new JsonObject { ["@type"] = "Title", ["name"] = v, ["kind"] = kind };
            }
        }

        return titles.Count == 0 ? null : titles;
    }

    private static Result<JsonNode?> Section(JsonNode o, IReadOnlyList<ContactField> fields, string section, string map, string prefix, Func<ContactField, JsonObject, (JsonObject? Entry, string? Problem)> make)
    {
        var old = ContactJson.Get(o, map) as JsonObject ?? [];
        var m = new JsonObject();
        foreach (var field in fields.Where(x => string.Equals(x.Section, section, StringComparison.Ordinal)))
        {
            var baseEntry = field.Key is not null && old[field.Key] is JsonObject b ? (JsonObject)b.DeepClone() : [];
            var (entry, problem) = make(field, baseEntry);
            if (problem is not null)
            {
                return Result<JsonNode?>.Fail(new ValidationError(problem));
            }

            if (entry is null)
            {
                continue;
            }

            var key = field.Key is not null && !m.ContainsKey(field.Key) ? field.Key : FreeAmong(old, m, prefix);
            m[key] = entry;
        }

        return Result<JsonNode?>.Ok(m.Count == 0 ? null : m);
    }

    private static string FreeAmong(JsonObject old, JsonObject made, string prefix)
    {
        for (var i = 0; ; i++)
        {
            var k = prefix + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!old.ContainsKey(k) && !made.ContainsKey(k))
            {
                return k;
            }
        }
    }

    private static JsonObject? Related(JsonNode o, IReadOnlyList<ContactField> fields)
    {
        var old = ContactJson.Get(o, "relatedTo") as JsonObject ?? [];
        var rel = new JsonObject();
        foreach (var field in fields.Where(x => string.Equals(x.Section, "related", StringComparison.Ordinal)))
        {
            var a = field.A.Trim();
            if (a.Length == 0)
            {
                continue;
            }

            var e = field.Key is not null && old[field.Key] is JsonObject b ? (JsonObject)b.DeepClone() : [];
            e["@type"] = "Relation";
            var relation = field.Kind >= 0 && field.Kind < ContactOptions.Relations.Count ? ContactOptions.Relations[field.Kind].Key : "contact";
            e["relation"] = new JsonObject { [relation] = true };
            rel[a] = e;
        }

        return rel.Count == 0 ? null : rel;
    }

    private static bool LabelPicked(ContactField f)
    {
        var n = f.Section switch
        {
            "phone" => ContactOptions.PhoneFeatures.Count,
            "email" or "address" or "url" or "im" => ContactOptions.Contexts.Count,
            _ => int.MaxValue,
        };
        return f.Label is not null && f.Kind == n;
    }

    private static void ContextOrLabel(ContactField f, JsonObject e)
    {
        if (LabelPicked(f))
        {
            return;
        }

        _ = e.Remove("label");
        _ = e.Remove("contexts");
        if (f.Kind == 0)
        {
            e["contexts"] = new JsonObject { ["private"] = true };
        }
        else if (f.Kind == 1)
        {
            e["contexts"] = new JsonObject { ["work"] = true };
        }
    }

    private static (JsonObject? Entry, string? Problem) MakeEmail(ContactField f, JsonObject e)
    {
        var a = f.A.Trim();
        if (a.Length == 0)
        {
            return (null, null);
        }

        if (!a.Contains('@', StringComparison.Ordinal) || a.Any(char.IsWhiteSpace))
        {
            return (null, $"\"{a}\" is not an email address");
        }

        e["@type"] = "EmailAddress";
        e["address"] = a;
        ContextOrLabel(f, e);
        return (e, null);
    }

    private static (JsonObject? Entry, string? Problem) MakePhone(ContactField f, JsonObject e)
    {
        var a = f.A.Trim();
        if (a.Length == 0)
        {
            return (null, null);
        }

        e["@type"] = "Phone";
        e["number"] = a;
        if (!LabelPicked(f))
        {
            _ = e.Remove("label");
            var feature = f.Kind >= 0 && f.Kind < ContactOptions.PhoneFeatures.Count ? ContactOptions.PhoneFeatures[f.Kind].Key : "voice";
            e["features"] = new JsonObject { [feature] = true };
        }

        _ = e.Remove("contexts");
        if (f.Kind2 == 1)
        {
            e["contexts"] = new JsonObject { ["private"] = true };
        }
        else if (f.Kind2 == 2)
        {
            e["contexts"] = new JsonObject { ["work"] = true };
        }

        return (e, null);
    }

    private static (JsonObject? Entry, string? Problem) MakeAddress(ContactField f, JsonObject e)
    {
        var parts = new[] { ("name", f.A), ("locality", f.B), ("region", f.C), ("postcode", f.D), ("country", f.E) };
        if (parts.All(p => p.Item2.Trim().Length == 0))
        {
            return (null, null);
        }

        var comps = new JsonArray();
        foreach (var (kind, v) in parts.Where(p => p.Item2.Trim().Length > 0))
        {
            comps.Append(new JsonObject { ["@type"] = "AddressComponent", ["kind"] = kind, ["value"] = v.Trim() });
        }

        e["@type"] = "Address";
        e["components"] = comps;
        e["isOrdered"] = false;
        foreach (var k in new[] { "street", "locality", "region", "postcode", "country", "full", "fullAddress" })
        {
            _ = e.Remove(k);
        }

        ContextOrLabel(f, e);
        return (e, null);
    }

    private static (JsonObject? Entry, string? Problem) MakeLink(ContactField f, JsonObject e)
    {
        var a = f.A.Trim();
        if (a.Length == 0)
        {
            return (null, null);
        }

        e["@type"] = "Link";
        e["uri"] = a.Contains("://", StringComparison.Ordinal) || a.StartsWith("mailto:", StringComparison.Ordinal) ? a : "https://" + a;
        ContextOrLabel(f, e);
        return (e, null);
    }

    private static (JsonObject? Entry, string? Problem) MakeOnline(ContactField f, JsonObject e)
    {
        var a = f.A.Trim();
        if (a.Length == 0)
        {
            return (null, null);
        }

        e["@type"] = "OnlineService";
        var colon = a.IndexOf(':', StringComparison.Ordinal);
        var isUri = colon > 0 && a[..colon].All(c => char.IsAsciiLetterOrDigit(c) || "+.-".Contains(c, StringComparison.Ordinal));
        if (isUri)
        {
            e["uri"] = a;
            _ = e.Remove("user");
        }
        else
        {
            e["user"] = a;
            _ = e.Remove("uri");
        }

        if (f.B.Trim().Length == 0)
        {
            _ = e.Remove("service");
        }
        else
        {
            e["service"] = f.B.Trim();
        }

        ContextOrLabel(f, e);
        return (e, null);
    }

    private static (JsonObject? Entry, string? Problem) MakeDate(ContactField f, JsonObject e)
    {
        var a = f.A.Trim();
        if (a.Length == 0)
        {
            return (null, null);
        }

        e["@type"] = "Anniversary";
        e["kind"] = f.Kind >= 0 && f.Kind < ContactOptions.DateKinds.Count ? ContactOptions.DateKinds[f.Kind].Key : "other";
        var date = ParseDateText(a);
        if (!date.IsOk)
        {
            return (null, date.Error!.Message);
        }

        e["date"] = date.Value;
        return (e, null);
    }
}

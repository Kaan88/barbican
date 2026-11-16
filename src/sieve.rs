//! Filter rules compatible with Bulwark (lib/sieve/generator.ts, parser.ts): rules are kept as
//! JSON in a `/* @metadata:begin ... @metadata:end */` block at the top of the Sieve script,
//! followed by the generated Sieve and, unchanged, any rules other clients wrote.
use serde_json::{json, Value};

const BEGIN: &str = "/* @metadata:begin";
const END: &str = "@metadata:end */";
const EXTERNAL: &str = "# --- External rules (managed outside Bulwark) ---";

pub struct Parsed {
    pub metadata: Value,
    /// Everything that is not Bulwark's (other clients' rules), kept verbatim.
    pub external: String,
    /// The metadata uses features this editor does not regenerate (vacation, periods...).
    pub locked: bool,
}

pub fn parse(script: &str) -> Parsed {
    let Some(b) = script.find(BEGIN) else {
        let ext = script.trim();
        return Parsed { metadata: json!({"version": 1, "rules": []}), external: strip_requires(ext), locked: false };
    };
    let e = script[b..].find(END).map(|i| b + i).unwrap_or(script.len());
    let json_txt = script[b + BEGIN.len()..e].replace("*\\/", "*/");
    let metadata: Value = serde_json::from_str(json_txt.trim()).unwrap_or(json!({"version": 1, "rules": []}));
    let rest = &script[(e + END.len()).min(script.len())..];
    let external = rest.find(EXTERNAL).map(|i| rest[i + EXTERNAL.len()..].trim().to_string()).unwrap_or_default();
    let locked = metadata.as_object().is_some_and(|o| o.keys().any(|k| k != "version" && k != "rules"))
        || metadata["rules"].as_array().is_some_and(|r| r.iter().any(|x| x.get("activeFrom").is_some() || x.get("activeUntil").is_some()));
    Parsed { metadata, external, locked }
}

fn strip_requires(s: &str) -> String {
    s.lines().filter(|l| !l.trim_start().starts_with("require ")).collect::<Vec<_>>().join("\n").trim().to_string()
}

fn esc(v: &str) -> String {
    v.replace('\\', "\\\\").replace('"', "\\\"")
}

fn values(v: &Value) -> Vec<String> {
    match v {
        Value::Array(a) => a.iter().filter_map(|x| x.as_str()).filter(|x| !x.is_empty()).map(String::from).collect(),
        Value::String(s) if !s.is_empty() => vec![s.clone()],
        _ => vec![],
    }
}

fn arg(vals: &[String], f: impl Fn(&str) -> String) -> String {
    if vals.len() == 1 { format!("\"{}\"", esc(&f(&vals[0]))) }
    else { format!("[{}]", vals.iter().map(|v| format!("\"{}\"", esc(&f(v)))).collect::<Vec<_>>().join(", ")) }
}

fn condition(c: &Value, req: &mut Vec<&'static str>) -> String {
    let field = c["field"].as_str().unwrap_or("from");
    let cmp = c["comparator"].as_str().unwrap_or("contains");
    if field == "all" { return "true".into(); }
    if field == "size" {
        let raw = values(&c["value"]).first().cloned().unwrap_or_default();
        let n = if raw.chars().all(|ch| ch.is_ascii_alphanumeric()) && !raw.is_empty() { raw } else { "0".into() };
        return format!("size {} {n}", if cmp == "greater_than" { ":over" } else { ":under" });
    }
    let vals = values(&c["value"]);
    if field == "body" {
        req.push("body");
        return format!("body {} {}", if cmp == "is" { ":is" } else { ":contains" }, arg(&vals, |s| s.into()));
    }
    if field == "attachment" {
        req.push("mime");
        if cmp == "has_any" { return r#"header :mime :anychild :contains "Content-Disposition" "attachment""#.into(); }
        let exts: Vec<String> = vals.iter().map(|v| v.trim_start_matches(['.', '*']).trim().to_string()).filter(|v| !v.is_empty()).collect();
        return format!(r#"header :mime :anychild :matches ["Content-Disposition", "Content-Type"] {}"#, arg(&exts, |e| format!("*.{e}*")));
    }
    let h = match field { "from" => "From", "to" => "To", "cc" => "Cc", "subject" => "Subject", _ => c["headerName"].as_str().filter(|x| !x.is_empty()).unwrap_or("X-Unknown") };
    let h = esc(h);
    if matches!(cmp, "address_is" | "domain_is") && matches!(field, "from" | "to" | "cc") {
        return format!("address {}:is \"{h}\" {}", if cmp == "domain_is" { ":domain " } else { "" }, arg(&vals, |s| s.into()));
    }
    match cmp {
        "not_contains" => format!("not header :contains \"{h}\" {}", arg(&vals, |s| s.into())),
        "is" => format!("header :is \"{h}\" {}", arg(&vals, |s| s.into())),
        "not_is" => format!("not header :is \"{h}\" {}", arg(&vals, |s| s.into())),
        "starts_with" => format!("header :matches \"{h}\" {}", arg(&vals, |s| format!("{s}*"))),
        "ends_with" => format!("header :matches \"{h}\" {}", arg(&vals, |s| format!("*{s}"))),
        "matches" => format!("header :matches \"{h}\" {}", arg(&vals, |s| s.into())),
        _ => format!("header :contains \"{h}\" {}", arg(&vals, |s| s.into())),
    }
}

fn actions(acts: &[Value], req: &mut Vec<&'static str>) -> Vec<String> {
    let is_flag = |a: &Value| matches!(a["type"].as_str(), Some("mark_read" | "star" | "add_label"));
    let mut ordered: Vec<&Value> = acts.iter().filter(|a| is_flag(a)).collect();
    ordered.extend(acts.iter().filter(|a| !is_flag(a)));
    ordered.iter().filter_map(|a| {
        let v = esc(a["value"].as_str().unwrap_or(""));
        Some(match a["type"].as_str()? {
            "move" => { req.push("fileinto"); format!("fileinto \"{v}\";") }
            "copy" => { req.push("fileinto"); req.push("copy"); format!("fileinto :copy \"{v}\";") }
            "forward" => { if a["keepCopy"] == true { req.push("copy"); } format!("redirect {}\"{v}\";", if a["keepCopy"] == true { ":copy " } else { "" }) }
            "mark_read" => { req.push("imap4flags"); r#"addflag "\\Seen";"#.to_string() }
            "star" => { req.push("imap4flags"); r#"addflag "\\Flagged";"#.to_string() }
            "add_label" => { req.push("imap4flags"); format!("addflag \"$label:{v}\";") }
            "discard" => "discard;".to_string(),
            "reject" => { req.push("reject"); format!("reject \"{v}\";") }
            "keep" => { req.push("fileinto"); "fileinto \"INBOX\";".to_string() }
            "stop" => "stop;".to_string(),
            _ => return None,
        })
    }).collect()
}

/// Build the whole script: metadata block, require line, Bulwark rules, external rules.
pub fn generate(rules: &[Value], external: &str) -> String {
    let mut req: Vec<&'static str> = vec![];
    let mut body = String::new();
    for r in rules.iter().filter(|r| r["enabled"] != false) {
        let conds: Vec<String> = r["conditions"].as_array().cloned().unwrap_or_default().iter().map(|c| condition(c, &mut req)).collect();
        if conds.is_empty() { continue; }
        let test = if conds.len() == 1 { conds[0].clone() }
            else { format!("{}({})", if r["matchType"] == "any" { "anyof" } else { "allof" }, conds.join(", ")) };
        let mut acts = actions(&r["actions"].as_array().cloned().unwrap_or_default(), &mut req);
        if r["stopProcessing"] == true { acts.push("stop;".into()); }
        body += &format!("\n# Rule: {}\nif {test} {{\n{}\n}}\n", r["name"].as_str().unwrap_or("Rule"), acts.iter().map(|a| format!("    {a}")).collect::<Vec<_>>().join("\n"));
    }
    req.sort();
    req.dedup();
    // keep requires of the external part
    let ext_req: Vec<String> = external.lines().filter_map(|l| l.trim().strip_prefix("require ")).flat_map(|x| x.trim_end_matches(';').trim_matches(['[', ']']).split(',').map(|s| s.trim().trim_matches('"').to_string()).collect::<Vec<_>>()).collect();
    let mut all: Vec<String> = req.iter().map(|s| s.to_string()).chain(ext_req).filter(|s| !s.is_empty()).collect();
    all.sort();
    all.dedup();
    let meta = json!({"version": 1, "rules": rules}).to_string().replace("*/", "*\\/");
    let mut out = format!("{BEGIN}\n{meta}\n{END}\n\n");
    if !all.is_empty() { out += &format!("require [{}];\n", all.iter().map(|s| format!("\"{s}\"")).collect::<Vec<_>>().join(", ")); }
    out += &body;
    let ext = strip_requires(external);
    if !ext.is_empty() { out += &format!("\n{EXTERNAL}\n\n{ext}\n"); }
    out
}

/// A Bulwark-shaped rule from the simple form.
pub fn rule(name: &str, field: &str, cmp: &str, value: &str, action: &str, action_value: &str, stop: bool) -> Value {
    let mut b = [0u8; 6];
    let _ = getrandom::getrandom(&mut b);
    let id: String = b.iter().map(|x| format!("{x:02x}")).collect();
    let (cmp, value) = if field == "attachment" { ("has_any", String::new()) } else if field == "all" { ("any", String::new()) } else { (cmp, value.to_string()) };
    let act = if action_value.trim().is_empty() { json!({"type": action}) } else { json!({"type": action, "value": action_value.trim()}) };
    json!({"id": format!("bw-{id}"), "name": if name.trim().is_empty() { "Rule" } else { name.trim() }, "enabled": true, "matchType": "all",
        "conditions": [{"field": field, "comparator": cmp, "value": value}], "actions": [act], "stopProcessing": stop})
}

#[cfg(test)]
mod tests {
    #[test]
    fn roundtrip_matches_bulwark_shape() {
        let r = super::rule("News", "from", "contains", "newsletter@", "move", "Newsletters", false);
        let s = super::generate(&[r], "# rule:[ext]\nif true { keep; }");
        assert!(s.starts_with("/* @metadata:begin\n{") && s.contains("\"version\":1") && s.contains("@metadata:end */"));
        assert!(s.contains("require [\"fileinto\"];"));
        assert!(s.contains("if header :contains \"From\" \"newsletter@\" {\n    fileinto \"Newsletters\";\n}"));
        assert!(s.contains("# --- External rules (managed outside Bulwark) ---"));
        let p = super::parse(&s);
        assert_eq!(p.metadata["rules"][0]["name"], "News");
        assert!(p.external.contains("# rule:[ext]"));
        assert!(!p.locked);
    }
}

//! Port of Bulwark's spam-score plugin: reads spam headers, gives a verdict.
use serde_json::Value;

pub struct Spam {
    pub verdict: &'static str, // "clean" | "suspicious" | "spam"
    pub detail: String,
}

fn get<'a>(h: &'a [Value], name: &str) -> Option<&'a str> {
    h.iter()
        .find(|x| x["name"].as_str().is_some_and(|n| n.eq_ignore_ascii_case(name)))
        .and_then(|x| x["value"].as_str())
        .map(str::trim)
}

fn num_after(s: &str, key: &str) -> Option<f64> {
    let i = s.find(key)? + key.len();
    let n: String = s[i..].chars().take_while(|c| c.is_ascii_digit() || *c == '.' || *c == '-').collect();
    n.parse().ok()
}

fn bracket(s: &str) -> Option<(f64, f64)> {
    let a = s.find('[')?;
    let b = s[a..].find(']')? + a;
    let mut it = s[a + 1..b].split('/');
    Some((it.next()?.trim().parse().ok()?, it.next()?.trim().parse().ok()?))
}

pub fn analyze(h: &[Value]) -> Option<Spam> {
    let status = get(h, "X-Spam-Status");
    let br = get(h, "X-Spamd-Result").and_then(bracket);
    let score = status
        .and_then(|s| num_after(s, "score="))
        .or_else(|| get(h, "X-Spam-Score").and_then(|s| s.parse().ok()))
        .or_else(|| get(h, "X-Rspamd-Score").and_then(|s| s.parse().ok()))
        .or(br.map(|b| b.0));
    let required = status.and_then(|s| num_after(s, "required=")).or(br.map(|b| b.1));
    let flag = get(h, "X-Spam-Flag").is_some_and(|f| f.eq_ignore_ascii_case("yes") || f.eq_ignore_ascii_case("true"));
    let action = get(h, "X-Rspamd-Action").or_else(|| get(h, "X-Spam-Action"));
    let action_spam = action.is_some_and(|a| {
        let a = a.to_lowercase();
        ["reject", "add header", "rewrite subject", "quarantine", "spam"].iter().any(|k| a.contains(k))
    });
    let llm = get(h, "X-Spam-LLM").and_then(|v| {
        let up = v.to_uppercase();
        ["LEGITIMATE", "SPAM", "SUSPICIOUS"].into_iter().find(|k| up.starts_with(k)).map(|k| (k, v))
    });
    if score.is_none() && !flag && action.is_none() && llm.is_none() {
        return None;
    }
    let mut verdict = if flag || action_spam {
        "spam"
    } else {
        match (score, required) {
            (Some(s), Some(r)) if s >= r => "spam",
            (Some(s), Some(r)) if s >= 0.6 * r => "suspicious",
            _ => "clean",
        }
    };
    if verdict == "clean" {
        match llm.map(|l| l.0) {
            Some("SPAM") => verdict = "spam",
            Some("SUSPICIOUS") => verdict = "suspicious",
            _ => {}
        }
    }
    let mut parts = vec![];
    match (score, required) {
        (Some(s), Some(r)) => parts.push(format!("Score {s:.2} / {r:.2} threshold")),
        (Some(s), None) => parts.push(format!("Score {s:.2}")),
        _ => {}
    }
    let stars = get(h, "X-Spam-Level")
        .map(|l| l.matches('*').count())
        .or(score.map(|s| s.max(0.0).floor().min(15.0) as usize));
    if let Some(n) = stars {
        if n > 0 {
            parts.push("★".repeat(n));
        }
    }
    if let Some(a) = action {
        parts.push(format!("Action: {a}"));
    }
    if let Some((_, raw)) = llm {
        parts.push(format!("AI: {raw}"));
    }
    Some(Spam { verdict, detail: parts.join("  ·  ") })
}

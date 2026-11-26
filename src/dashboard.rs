//! Admin dashboard modelled on Stalwart's WebUI: per-tab metric cards and charts built
//! from `x:Metric` samples (Counter: summed per bucket, Gauge: averaged per bucket and
//! carried forward, Histogram: sum/count mean). Charts are SVG path commands in a
//! 1000 x 100 viewbox; the % change compares the first and second half of the window,
//! as the WebUI does.
use crate::admin::Admin;
use crate::{AppWindow, DashBar, DashCol, DashPanel, DashRow, DashTip};
use serde_json::{json, Value};
use slint::{ComponentHandle, ModelRc, VecModel};
use crate::ui_types::{Color, SharedString};
use std::collections::HashMap;

const PINK: u32 = 0xe8486f;
const TEAL: u32 = 0x2a9d8f;
const AMBER: u32 = 0xf59e0b;
const BLUE: u32 = 0x60a5fa;
const VIOLET: u32 = 0xa78bfa;
const PALETTE: [u32; 5] = [PINK, TEAL, AMBER, BLUE, VIOLET];

#[derive(Clone, Copy, PartialEq)]
enum Unit { Count, Bytes, Ms, Pct }

#[derive(Clone, Copy, PartialEq)]
enum Kind { Counter, Gauge, Histogram }

struct Sample { metric: String, kind: Kind, t: i64, count: f64, sum: f64 }

/// Samples bucketed over the window.
struct Data { from: i64, to: i64, buckets: usize, samples: Vec<Sample>, extra: HashMap<String, f64> }

impl Data {
    fn idx(&self, t: i64) -> Option<usize> {
        if t < self.from || t > self.to { return None; }
        let span = (self.to - self.from).max(1) as f64;
        Some((((t - self.from) as f64 / span) * self.buckets as f64).floor().min(self.buckets as f64 - 1.0) as usize)
    }

    fn kind(&self, names: &[&str]) -> Kind {
        self.samples.iter().find(|s| names.contains(&s.metric.as_str())).map(|s| s.kind).unwrap_or(Kind::Counter)
    }

    /// Per-bucket values like the WebUI's bucketValues: counters summed, histograms
    /// sum/count, gauges averaged; None for a bucket without samples.
    fn series_opt(&self, names: &[&str], n: usize) -> Vec<Option<f64>> {
        let sub = Data { from: self.from, to: self.to, buckets: n, samples: vec![], extra: HashMap::new() };
        let mut out: Vec<Option<f64>> = vec![None; n];
        for name in names {
            let mut c = vec![0.0; n];
            let mut s = vec![0.0; n];
            let mut k = vec![0usize; n];
            let mut kind = Kind::Counter;
            for x in self.samples.iter().filter(|x| x.metric == *name) {
                let Some(i) = sub.idx(x.t) else { continue };
                kind = x.kind;
                c[i] += x.count; s[i] += x.sum; k[i] += 1;
            }
            for i in 0..n {
                if k[i] == 0 { continue; }
                let v = match kind {
                    Kind::Counter => Some(c[i]),
                    Kind::Histogram => if c[i] > 0.0 { Some(s[i] / c[i]) } else { None },
                    Kind::Gauge => Some(c[i] / k[i] as f64),
                };
                if let Some(v) = v { out[i] = Some(out[i].unwrap_or(0.0) + v); }
            }
        }
        out
    }

    fn series(&self, names: &[&str], n: usize) -> Vec<f64> {
        self.series_opt(names, n).into_iter().map(|v| v.unwrap_or(0.0)).collect()
    }

    /// The headline figure: counter total, gauge latest value, histogram mean.
    fn value(&self, names: &[&str]) -> f64 {
        names.iter().map(|name| {
            let xs: Vec<&Sample> = self.samples.iter().filter(|x| x.metric == *name).collect();
            match xs.first().map(|x| x.kind) {
                None => self.extra.get(*name).copied().unwrap_or(0.0),
                Some(Kind::Counter) => xs.iter().map(|x| x.count).sum(),
                Some(Kind::Gauge) => xs.iter().max_by_key(|x| x.t).map(|x| x.count).unwrap_or(0.0),
                Some(Kind::Histogram) => {
                    let c: f64 = xs.iter().map(|x| x.count).sum();
                    if c > 0.0 { xs.iter().map(|x| x.sum).sum::<f64>() / c } else { 0.0 }
                }
            }
        }).sum()
    }

    /// Change between the first and second half of the window, like the WebUI.
    fn change(&self, names: &[&str]) -> Option<f64> {
        let h = self.series(names, 2);
        let k = self.kind(names);
        if k == Kind::Gauge || h[0] == 0.0 { return None; }
        Some((h[1] - h[0]) / h[0].abs() * 100.0)
    }
}

fn fmt(v: f64, u: Unit) -> String {
    match u {
        // formatSize(compact): 1024-based, one decimal below 9.95
        Unit::Bytes => {
            let units = ["B", "KB", "MB", "GB", "TB"];
            for i in (0..units.len()).rev() {
                let x = v.abs() / 1024f64.powi(i as i32);
                let digits = if x < 9.95 && i > 0 { 1 } else { 0 };
                let r = (x * 10f64.powi(digits)).round() / 10f64.powi(digits);
                if r >= 1.0 || i == 0 { return format!("{} {}", if digits == 1 { format!("{r:.1}") } else { format!("{r:.0}") }, units[i]); }
            }
            "0 B".into()
        }
        // formatElapsed
        Unit::Ms => {
            if v <= 0.0 { return "0 ms".into(); }
            // sub-10 ms keeps two decimals so fast stores do not read "0 ms" and the
            // y axis of a sub-millisecond chart is not "1 ms, 1 ms, 0 ms, 0 ms, 0 ms"
            if v < 9.995 {
                let t = format!("{v:.2}");
                return format!("{} ms", t.trim_end_matches('0').trim_end_matches('.'));
            }
            if v.round() < 1000.0 { return format!("{:.0} ms", v.round()); }
            let sec = (v / 100.0).round() / 10.0;
            if sec < 60.0 { return format!("{sec:.1} s"); }
            let ts = (v / 1000.0).floor().max(60.0) as i64;
            let (m, s) = (ts / 60, ts % 60);
            if m < 60 { return if s > 0 { format!("{m} m {s} s") } else { format!("{m} m") }; }
            let (h, m) = (m / 60, m % 60);
            if m > 0 { format!("{h} h {m} m") } else { format!("{h} h") }
        }
        Unit::Pct => format!("{v:.1}%"),
        // formatCompactNumber
        Unit::Count => {
            if v.fract() == 0.0 && v.abs() < 1000.0 { return format!("{v:.0}"); }
            let (x, suf) = if v.abs() >= 1e9 { (v / 1e9, "B") } else if v.abs() >= 1e6 { (v / 1e6, "M") } else if v.abs() >= 1e3 { (v / 1e3, "K") } else { (v, "") };
            let t = format!("{x:.1}");
            format!("{}{suf}", t.strip_suffix(".0").unwrap_or(&t))
        }
    }
}

/// Recharts-style nice ticks: 5 ticks from 0 with a "nice" step.
fn nice(m: f64) -> f64 {
    if m <= 0.0 { return 4.0; }
    let raw = m / 4.0;
    let p = 10f64.powf(raw.log10().floor());
    for f in [1.0, 2.0, 2.5, 3.0, 4.0, 5.0, 6.0, 8.0, 10.0] { if f * p >= raw - 1e-9 { return 4.0 * f * p; } }
    40.0 * p
}

fn pt(i: usize, n: usize, v: f64, max: f64) -> (f64, f64) {
    let x = if n <= 1 { 0.0 } else { i as f64 * 1000.0 / (n - 1) as f64 };
    (x, 100.0 - (v / max).clamp(0.0, 1.0) * 100.0)
}

/// d3 curveMonotoneX through the points; `first` is the opening command (M or L).
fn monotone(pts: &[(f64, f64)], first: &str) -> String {
    let n = pts.len();
    if n == 0 { return String::new(); }
    let mut o = format!("{first} {:.1} {:.2}", pts[0].0, pts[0].1);
    if n == 1 { return o; }
    if n == 2 { return o + &format!(" L {:.1} {:.2}", pts[1].0, pts[1].1); }
    let sign = |x: f64| if x < 0.0 { -1.0 } else { 1.0 };
    // slopes at interior points (d3 slope3), ends use slope2
    let mut t = vec![0.0; n];
    for i in 1..n - 1 {
        let (x0, y0) = pts[i - 1]; let (x1, y1) = pts[i]; let (x2, y2) = pts[i + 1];
        let h0 = x1 - x0; let h1 = x2 - x1;
        let s0 = if h0 != 0.0 { (y1 - y0) / h0 } else { 0.0 };
        let s1 = if h1 != 0.0 { (y2 - y1) / h1 } else { 0.0 };
        let pp = (s0 * h1 + s1 * h0) / (h0 + h1);
        t[i] = (sign(s0) + sign(s1)) * s0.abs().min(s1.abs()).min(0.5 * pp.abs());
        if !t[i].is_finite() { t[i] = 0.0; }
    }
    let slope2 = |i: usize, ti: f64, dir: f64| -> f64 {
        let (xa, ya) = pts[i]; let (xb, yb) = pts[(i as isize + dir as isize) as usize];
        let h = xb - xa;
        if h != 0.0 { (3.0 * (yb - ya) / h - ti) / 2.0 } else { ti }
    };
    t[0] = slope2(0, t[1], 1.0);
    t[n - 1] = { let (xa, ya) = pts[n - 2]; let (xb, yb) = pts[n - 1]; let h = xb - xa; if h != 0.0 { (3.0 * (yb - ya) / h - t[n - 2]) / 2.0 } else { t[n - 2] } };
    for i in 1..n {
        let (x0, y0) = pts[i - 1]; let (x1, y1) = pts[i];
        let dx = (x1 - x0) / 3.0;
        o += &format!(" C {:.1} {:.2} {:.1} {:.2} {x1:.1} {y1:.2}", x0 + dx, y0 + dx * t[i - 1], x1 - dx, y1 - dx * t[i]);
    }
    o
}

fn points(vals: &[f64], max: f64) -> Vec<(f64, f64)> {
    let n = vals.len();
    (0..n).map(|i| pt(i, n, vals[i], max)).collect()
}

/// Line through the non-null points (Recharts connectNulls).
fn line_opt(vals: &[Option<f64>], max: f64) -> String {
    let n = vals.len();
    let pts: Vec<(f64, f64)> = (0..n).filter_map(|i| vals[i].map(|v| pt(i, n, v, max))).collect();
    if pts.is_empty() { return "M 0 100 L 1000 100".into(); }
    monotone(&pts, "M")
}

fn line(vals: &[f64], max: f64) -> String { monotone(&points(vals, max), "M") }

/// Area under a connectNulls line down to the baseline.
fn area_opt(vals: &[Option<f64>], max: f64) -> String {
    let n = vals.len();
    let pts: Vec<(f64, f64)> = (0..n).filter_map(|i| vals[i].map(|v| pt(i, n, v, max))).collect();
    if pts.len() < 2 { return "M 0 100 L 0 100".into(); }
    format!("{} L {:.1} 100 L {:.1} 100 Z", monotone(&pts, "M"), pts[pts.len() - 1].0, pts[0].0)
}

/// Band between a series top and its base (the series below it in the stack).
fn band(top: &[f64], base: &[f64], max: f64) -> String {
    let mut b = points(base, max);
    b.reverse();
    format!("{} {} Z", monotone(&points(top, max), "M"), monotone(&b, "L"))
}

fn bars(top: &[f64], base: &[f64], max: f64) -> String {
    let n = top.len().max(1);
    let w = 1000.0 / n as f64;
    let mut o = String::new();
    for i in 0..top.len() {
        if top[i] <= base[i] { continue; }
        let x0 = i as f64 * w + w * 0.18;
        let x1 = (i + 1) as f64 * w - w * 0.18;
        let y0 = 100.0 - (base[i] / max).clamp(0.0, 1.0) * 100.0;
        let y1 = 100.0 - (top[i] / max).clamp(0.0, 1.0) * 100.0;
        o += &format!("M {x0:.1} {y0:.2} L {x1:.1} {y0:.2} L {x1:.1} {y1:.2} L {x0:.1} {y1:.2} Z ");
    }
    if o.is_empty() { "M 0 100 L 0 100".into() } else { o }
}

fn col(c: u32) -> Color { Color::from_rgb_u8((c >> 16) as u8, (c >> 8) as u8, c as u8) }
fn ss(v: Vec<String>) -> ModelRc<SharedString> { ModelRc::new(VecModel::from(v.into_iter().map(Into::into).collect::<Vec<SharedString>>())) }

fn blank(kind: i32, title: &str, icon: &str, height: i32) -> DashPanel {
    DashPanel {
        kind, title: title.into(), icon: icon.into(), height, info: "".into(), value: "".into(), change: "".into(),
        good: true, flat: false, spark: "".into(), tone: 0, names: ss(vec![]), colors: ModelRc::default(), areas: ss(vec![]), lines: ss(vec![]),
        ylabels: ss(vec![]), xlabels: ss(vec![]), bars: ModelRc::default(), note: "".into(),
        tips: ModelRc::default(), tip_names: ss(vec![]), tip_colors: ModelRc::default(), spark_tips: ModelRc::default(), fill_alpha: 0.35, xstep: 1,
    }
}

/// Polarity: whether an increase is good.
#[derive(Clone, Copy)]
enum Pol { Up, Down, Neutral }

struct Ctx<'a> { d: &'a Data, range: usize, h12: bool }

impl Ctx<'_> {
    fn card(&self, title: &str, icon: &str, info: &str, names: &[&str], u: Unit, pol: Pol, tone: i32) -> DashPanel {
        let d = self.d;
        let mut p = blank(0, title, icon, 110);
        p.info = info.into();
        p.tone = tone;
        p.value = fmt(d.value(names), u).into();
        if let Some(c) = d.change(names) {
            let up = c >= 0.0;
            let mag = (c.abs() * 10.0).round() / 10.0;
            if mag == 0.0 {
                // no movement: neutral grey badge without an arrow
                p.change = "0%".into();
                p.flat = true;
            } else {
                p.change = format!("{} {}%", if up { "\u{2191}" } else { "\u{2193}" }, format!("{:.1}", mag).trim_end_matches(".0")).into();
                p.good = match pol { Pol::Up => up, Pol::Down => !up, Pol::Neutral => true };
            }
        }
        let mut s = d.series(names, 20);
        if d.kind(names) == Kind::Gauge {
            // a gauge holds its level between samples: carry it over empty buckets (and
            // back to the start) instead of drawing spikes down to zero
            let raw = d.series_opt(names, 20);
            let first = raw.iter().flatten().next().copied().unwrap_or(0.0);
            let mut last = first;
            s = raw.iter().map(|v| { if let Some(x) = v { last = *x; } last }).collect();
        }
        if s.iter().any(|v| *v != 0.0) {
            let max = s.iter().cloned().fold(0.0, f64::max) * 1.1;
            p.spark = line(&s, max).into();
            let span = (d.to - d.from) as f64 / 20.0;
            p.spark_tips = ModelRc::new(VecModel::from(s.iter().enumerate().map(|(i, v)| DashTip {
                time: self.when(d.from + (span * (i as f64 + 0.5)) as i64).into(),
                vals: ss(vec![fmt(*v, u)]),
                ys: ModelRc::new(VecModel::from(vec![(1.0 - v / max) as f32])),
            }).collect::<Vec<_>>()));
        }
        p
    }

    /// WebUI formatTimeTick: time of day for intraday ranges, else month/day.
    fn when(&self, t: i64) -> String {
        let dt = chrono::DateTime::from_timestamp(t, 0).unwrap_or_default().with_timezone(&chrono::Local);
        let hm = if self.h12 { "%-I:%M %p" } else { "%H:%M" };
        match self.range { 0 | 1 => dt.format(hm).to_string(), _ => dt.format(&format!("%m/%d {hm}")).to_string() }
    }

    fn tick(&self, t: i64) -> String {
        let dt = chrono::DateTime::from_timestamp(t, 0).unwrap_or_default().with_timezone(&chrono::Local);
        match self.range { 0 | 1 => dt.format(if self.h12 { "%-I:%M %p" } else { "%H:%M" }).to_string(), _ => dt.format("%m/%d").to_string() }
    }

    /// Bucket centre timestamps (bucketTimestamps).
    fn centres(&self) -> Vec<i64> {
        let d = self.d;
        let w = (d.to - d.from) as f64 / d.buckets as f64;
        (0..d.buckets).map(|i| d.from + (w * (i as f64 + 0.5)) as i64).collect()
    }

    /// style 0: area (stacked when several series, nulls carried forward like the WebUI),
    /// 1: bars, 2: lines (connectNulls).
    fn chart(&self, title: &str, series: &[(&str, &[&str])], u: Unit, style: u8, height: i32) -> DashPanel {
        let d = self.d;
        let n = d.buckets;
        let stacked = style == 0 && series.len() > 1 || style == 1;
        let raw: Vec<Vec<Option<f64>>> = series.iter().map(|(_, m)| d.series_opt(m, n)).collect();
        // stacked charts carry the last seen value over empty buckets (0 before the first)
        let filled: Vec<Vec<f64>> = raw.iter().map(|r| {
            let mut last = 0.0;
            r.iter().map(|v| match v { Some(x) => { last = *x; *x } None => if style == 0 { last } else { 0.0 } }).collect()
        }).collect();
        let mut tops: Vec<Vec<f64>> = vec![];
        let mut acc = vec![0.0; n];
        for f in &filled { for i in 0..n { acc[i] += f[i]; } tops.push(acc.clone()); }
        let peak = if stacked { tops.last().map(|t| t.iter().cloned().fold(0.0, f64::max)).unwrap_or(0.0) }
            else { raw.iter().flatten().flatten().cloned().fold(0.0, f64::max) };
        let max = nice(peak);
        let mut p = blank(if style == 1 { 2 } else { 1 }, title, "", height);
        let colors: Vec<Color> = (0..series.len()).map(|i| col(PALETTE[i % 5])).collect();
        p.names = ss(series.iter().map(|(l, _)| l.to_string()).collect());
        p.colors = ModelRc::new(VecModel::from(colors.clone()));
        p.tip_names = p.names.clone();
        p.tip_colors = p.colors.clone();
        let ys: Vec<Vec<f32>> = (0..n).map(|i| (0..series.len()).map(|s| {
            let v = if stacked { Some(tops[s][i]) } else { raw[s][i] };
            v.map(|v| (1.0 - (v / max).clamp(0.0, 1.0)) as f32).unwrap_or(-1.0)
        }).collect()).collect();
        let times = self.centres();
        p.tips = ModelRc::new(VecModel::from((0..n).map(|i| DashTip {
            time: self.when(times[i]).into(),
            vals: ss((0..series.len()).map(|s| if stacked { fmt(filled[s][i], u) } else { raw[s][i].map(|v| fmt(v, u)).unwrap_or("—".into()) }).collect()),
            ys: ModelRc::new(VecModel::from(ys[i].clone())),
        }).collect::<Vec<_>>()));
        match style {
            1 => {
                let mut base = vec![0.0; n];
                let mut a = vec![];
                for t in &tops { a.push(bars(t, &base, max)); base = t.clone(); }
                p.areas = ss(a);
            }
            2 => { p.lines = ss(raw.iter().map(|r| line_opt(r, max)).collect()); }
            _ if !stacked => {
                p.fill_alpha = 0.14;
                p.areas = ss(raw.iter().map(|r| area_opt(r, max)).collect());
                p.lines = ss(raw.iter().map(|r| line_opt(r, max)).collect());
            }
            _ => {
                let zero = vec![0.0; n];
                p.fill_alpha = 0.35;
                p.areas = ss((0..tops.len()).map(|i| band(&tops[i], if i == 0 { &zero } else { &tops[i - 1] }, max)).collect());
                p.lines = ss(tops.iter().map(|t| line(t, max)).collect());
            }
        }
        p.ylabels = ss((0..5).map(|i| fmt(max * (4 - i) as f64 / 4.0, u)).collect());
        // about a dozen ticks at bucket centres (minTickGap thinning)
        let step = (n as f64 / 12.0).ceil().max(1.0) as usize;
        p.xlabels = ss((0..n).step_by(step).map(|i| self.tick(times[i])).collect());
        p.xstep = step as i32;
        p
    }

    fn hbars(&self, title: &str, icon: &str, items: &[(&str, &[&str])], u: Unit, height: i32) -> DashPanel {
        // rankingEntries: sorted by value, largest first
        let mut items: Vec<(&str, &[&str])> = items.to_vec();
        items.sort_by(|a, b| self.d.value(b.1).partial_cmp(&self.d.value(a.1)).unwrap_or(std::cmp::Ordering::Equal));
        let items = &items[..];
        let vals: Vec<f64> = items.iter().map(|(_, m)| self.d.value(m)).collect();
        let max = vals.iter().cloned().fold(0.0, f64::max);
        let total: f64 = vals.iter().sum();
        let mut p = blank(3, title, icon, height);
        p.bars = ModelRc::new(VecModel::from(items.iter().zip(&vals).enumerate().map(|(i, ((l, _), v))| DashBar {
            label: (*l).into(), value: fmt(*v, u).into(), frac: if max > 0.0 { (*v / max) as f32 } else { 0.0 },
            color: col([PINK, TEAL, AMBER, BLUE, VIOLET, PINK][i % 6]),
            share: if total > 0.0 { format!("{:.1}%", v / total * 100.0) } else { "0.0%".into() }.into(),
        }).collect::<Vec<_>>()));
        p
    }

    fn ratio(&self, title: &str, icon: &str, good: &[&str], bad: &[&str], note: &str) -> DashPanel {
        let (g, b) = (self.d.value(good), self.d.value(bad));
        let mut p = blank(4, title, icon, 112);
        p.value = if g + b > 0.0 { fmt(g / (g + b) * 100.0, Unit::Pct) } else { "—".into() }.into();
        p.note = note.into();
        p.info = note.into();
        // the meter under the value (BNDashboard "Clean mail")
        if g + b > 0.0 {
            p.bars = ModelRc::new(VecModel::from(vec![DashBar { label: "".into(), value: "".into(), frac: (g / (g + b)) as f32, color: col(0), share: "".into() }]));
        }
        p
    }
}

fn row(cols: Vec<(f32, Vec<DashPanel>)>) -> DashRow {
    DashRow { cols: ModelRc::new(VecModel::from(cols.into_iter().map(|(w, p)| DashCol { weight: w, panels: ModelRc::new(VecModel::from(p)) }).collect::<Vec<_>>())) }
}

fn cards(c: Vec<DashPanel>) -> DashRow { row(c.into_iter().map(|p| (1.0, vec![p])).collect()) }

/// Metric names each tab needs.
fn metrics(tab: usize) -> Vec<&'static str> {
    match tab {
        0 => vec!["queue.message-queued", "queue.authenticated-message-queued", "queue.dsn-queued", "queue.report-queued", "queue.count",
                  "server.memory", "message-ingest.ham", "message-ingest.spam", "security.ip-blocked", "auth.failed",
                  "smtp.mailbox-does-not-exist", "smtp.relay-not-allowed", "smtp.rate-limit-exceeded"],
        1 => vec!["smtp.connection-start", "imap.connection-start", "pop3.connection-start", "http.connection-start", "sieve.connection-start",
                  "smtp.active-connections", "imap.active-connections", "pop3.active-connections", "http.active-connections", "sieve.active-connections"],
        2 => vec!["queue.count", "delivery.active-connections", "smtp.request-time", "delivery.total-time", "queue.message-queued",
                  "queue.authenticated-message-queued", "delivery.delivered", "queue.dsn-queued", "queue.report-queued", "queue.back-pressure"],
        3 => vec!["security.ip-blocked", "security.authentication-ban", "security.abuse-ban", "security.scan-ban", "security.loiter-ban",
                  "security.unauthorized", "auth.failed", "message-ingest.spam"],
        4 => vec!["smtp.spf-from-fail", "smtp.dkim-fail", "smtp.dkim2-fail", "smtp.dmarc-fail", "smtp.dkim2-dsn-rejected",
                  "incoming-report.dmarc-report-with-warnings", "incoming-report.tls-report-with-warnings",
                  "smtp.spf-from-pass", "smtp.dkim-pass", "smtp.dkim2-pass", "smtp.dmarc-pass"],
        5 => vec!["encryption.gateway-encrypted", "encryption.gateway-plaintext", "encryption.gateway-bounced", "encryption.at-rest-encrypted",
                  "encryption.encryption-skipped", "encryption.key-lookup", "encryption.key-harvested", "encryption.key-change",
                  "encryption.crl-fetch-failed", "encryption.dnssec-bogus", "encryption.key-lookup-time"],
        6 => vec!["message-ingest.time", "dns.lookup-time", "limit.memory-budget", "server.memory", "server.buffered-memory-smtp", "server.buffered-memory-read"],
        7 => vec!["store.data-read-time", "store.data-write-time", "store.blob-read-time", "store.blob-write-time"],
        8 => vec!["search-index.index-time", "search-index.batch-indexed", "search-index.document-truncated", "search-index.partition-retry",
                  "search-index.pending-email", "search-index.pending-calendar", "search-index.pending-contacts", "search-index.pending-file", "search-index.pending-tracing"],
        9 => vec!["task-manager.task-failed", "task-manager.task-retry", "task-manager.task-stalled", "task-manager.task-expired", "task-manager.pending", "task-manager.due"],
        _ => vec!["cache.hit", "cache.miss", "cache.entry-too-large", "cache.swap-error", "cache.messages-size", "cache.messages-large-size",
                  "cache.size", "cache.stale", "cache.swap-hit", "cache.swap-miss", "cache.swap-write"],
    }
}

fn layout(tab: usize, x: &Ctx) -> Vec<DashRow> {
    use Pol::*;
    use Unit::*;
    let (ib, is, ic, im) = ("\u{E715}", "\u{E724}", "\u{E823}", "\u{E950}");
    match tab {
        0 => vec![
            row(vec![
                (1.0, vec![x.card("Received Messages", ib, "Messages accepted into the queue", &["queue.message-queued"], Count, Up, 0)]),
                (1.0, vec![x.card("Sent Messages", is, "Authenticated submissions queued", &["queue.authenticated-message-queued"], Count, Up, 1)]),
                (1.0, vec![x.card("Pending Messages", ic, "Messages waiting in the queue", &["queue.count"], Count, Down, 0)]),
                (1.0, vec![x.card("Server Memory", im, "Resident memory of the server", &["server.memory"], Bytes, Neutral, 2)]),
            ]),
            row(vec![
                (2.3, vec![x.chart("Messages", &[("received", &["queue.message-queued"]), ("sent", &["queue.authenticated-message-queued"])], Count, 0, 306)]),
                (1.0, vec![
                    x.ratio("Clean mail", "\u{E83D}", &["message-ingest.ham"], &["message-ingest.spam"], "Share of inbound messages classified as ham during the period."),
                    x.hbars("Rejections", "\u{F140}", &[("Blocked IPs", &["security.ip-blocked"]), ("Failed logins", &["auth.failed"]), ("Spam", &["message-ingest.spam"]),
                        ("Unknown recipient", &["smtp.mailbox-does-not-exist"]), ("Relay denied", &["smtp.relay-not-allowed"]), ("Rate limited", &["smtp.rate-limit-exceeded"])], Count, 182),
                ]),
            ]),
            row(vec![
                (1.0, vec![
                    { let mut p = x.card("Total Users", "\u{E716}", "Accounts in the directory", &["dir.accounts"], Count, Neutral, 1); p.height = 115; p }
                    , { let mut p = x.card("Total Domains", "\u{E774}", "Domains served", &["dir.domains"], Count, Neutral, 1); p.height = 115; p }]),
                (1.5, vec![x.chart("Memory usage", &[("memory", &["server.memory"])], Bytes, 0, 242)]),
            ]),
        ],
        1 => vec![
            cards(vec![
                x.card("SMTP connections", "\u{E715}", "Open SMTP sessions", &["smtp.active-connections"], Count, Neutral, 0),
                x.card("IMAP connections", "\u{E8F1}", "Open IMAP sessions", &["imap.active-connections"], Count, Neutral, 1),
                x.card("HTTP connections", "\u{E774}", "Open HTTP sessions", &["http.active-connections"], Count, Neutral, 0),
                x.card("POP3 connections", "\u{E896}", "Open POP3 sessions", &["pop3.active-connections"], Count, Neutral, 2),
            ]),
            row(vec![(1.0, vec![x.chart("New connections", &[("smtp", &["smtp.connection-start"]), ("imap", &["imap.connection-start"]),
                ("http", &["http.connection-start"]), ("pop3", &["pop3.connection-start"]), ("sieve", &["sieve.connection-start"])], Count, 0, 300)])]),
            row(vec![(1.0, vec![x.chart("Active connections", &[("smtp", &["smtp.active-connections"]), ("imap", &["imap.active-connections"]),
                ("http", &["http.active-connections"]), ("pop3", &["pop3.active-connections"]), ("sieve", &["sieve.active-connections"])], Count, 2, 260)])]),
        ],
        2 => vec![
            cards(vec![
                x.card("Queued Messages", ic, "Messages waiting in the queue", &["queue.count"], Count, Down, 0),
                x.card("Active Sessions", "\u{E8AB}", "Outbound delivery connections", &["delivery.active-connections"], Count, Neutral, 1),
                x.card("Session Time", "\u{E916}", "Average SMTP session duration", &["smtp.request-time"], Ms, Down, 0),
                x.card("Delivery Time", "\u{E916}", "Average time from submission to delivery", &["delivery.total-time"], Ms, Down, 2),
            ]),
            row(vec![
                (2.0, vec![x.chart("Messages", &[("received", &["queue.message-queued"]), ("sent", &["queue.authenticated-message-queued"])], Count, 0, 300)]),
                (1.0, vec![x.hbars("Outgoing", is, &[("Received", &["queue.message-queued"]), ("Submitted", &["queue.authenticated-message-queued"]),
                    ("Delivered", &["delivery.delivered"]), ("DSNs sent", &["queue.dsn-queued"]), ("Reports sent", &["queue.report-queued"])], Count, 300)]),
            ]),
            row(vec![
                (1.0, vec![x.chart("DSNs & reports", &[("dsn", &["queue.dsn-queued"]), ("reports", &["queue.report-queued"])], Count, 1, 240)]),
                (1.0, vec![x.chart("Queue back-pressure", &[("back-pressure", &["queue.back-pressure"])], Count, 0, 240)]),
            ]),
        ],
        3 => vec![
            cards(vec![
                x.card("Threats Blocked", "\u{E83D}", "Connections from blocked IPs", &["security.ip-blocked"], Count, Neutral, 0),
                x.card("IPs Banned", "\u{F140}", "New fail-to-ban bans", &["security.authentication-ban", "security.abuse-ban", "security.scan-ban", "security.loiter-ban"], Count, Down, 1),
                x.card("Auth Failures", "\u{E72E}", "Failed logins", &["auth.failed"], Count, Down, 0),
                x.card("Spam Blocked", "\u{E7BA}", "Messages classified as spam", &["message-ingest.spam"], Count, Neutral, 2),
            ]),
            row(vec![
                (2.0, vec![x.chart("Fail-to-ban", &[("auth", &["security.authentication-ban"]), ("brute", &["security.abuse-ban", "security.scan-ban"]),
                    ("loiter", &["security.loiter-ban"]), ("blocked", &["security.ip-blocked"])], Count, 1, 300)]),
                (1.0, vec![x.hbars("Breakdown", "\u{F140}", &[("Blocked IPs", &["security.ip-blocked"]), ("Auth bans", &["security.authentication-ban"]),
                    ("Abuse bans", &["security.abuse-ban"]), ("Scan bans", &["security.scan-ban"]), ("Loiter bans", &["security.loiter-ban"]),
                    ("Unauthorized", &["security.unauthorized"]), ("Failed logins", &["auth.failed"])], Count, 300)]),
            ]),
        ],
        4 => vec![
            row(vec![
                (1.0, vec![x.ratio("SPF pass rate", "\u{E73E}", &["smtp.spf-from-pass"], &["smtp.spf-from-fail"], "Inbound messages passing SPF.")]),
                (1.0, vec![x.ratio("DKIM pass rate", "\u{E73E}", &["smtp.dkim-pass", "smtp.dkim2-pass"], &["smtp.dkim-fail", "smtp.dkim2-fail"], "Signatures that verified.")]),
                (1.0, vec![x.ratio("DMARC pass rate", "\u{E73E}", &["smtp.dmarc-pass"], &["smtp.dmarc-fail"], "Inbound messages passing DMARC.")]),
            ]),
            row(vec![
                (1.0, vec![x.chart("SPF", &[("fail", &["smtp.spf-from-fail"]), ("pass", &["smtp.spf-from-pass"])], Count, 0, 240)]),
                (1.0, vec![x.chart("DKIM", &[("fail", &["smtp.dkim-fail", "smtp.dkim2-fail"]), ("pass", &["smtp.dkim-pass", "smtp.dkim2-pass"])], Count, 0, 240)]),
            ]),
            row(vec![
                (1.0, vec![x.chart("DMARC", &[("fail", &["smtp.dmarc-fail"]), ("pass", &["smtp.dmarc-pass"])], Count, 0, 240)]),
                (1.0, vec![x.hbars("Failures & warnings", "\u{E7BA}", &[("SPF fail", &["smtp.spf-from-fail"]), ("DKIM fail", &["smtp.dkim-fail"]), ("DKIM2 fail", &["smtp.dkim2-fail"]),
                    ("DMARC fail", &["smtp.dmarc-fail"]), ("DKIM2 DSN rejected", &["smtp.dkim2-dsn-rejected"]),
                    ("DMARC report warn", &["incoming-report.dmarc-report-with-warnings"]), ("TLS report warn", &["incoming-report.tls-report-with-warnings"])], Count, 240)]),
            ]),
        ],
        5 => vec![
            cards(vec![
                x.card("Gateway Encrypted", "\u{E72E}", "Messages encrypted by the gateway", &["encryption.gateway-encrypted"], Count, Up, 0),
                x.card("Gateway Plaintext", "\u{E785}", "Messages sent without encryption", &["encryption.gateway-plaintext"], Count, Down, 1),
                x.card("Encrypted at Rest", "\u{E8B7}", "Messages encrypted at rest", &["encryption.at-rest-encrypted"], Count, Up, 0),
                x.card("Key Lookup Time", "\u{E916}", "Average key lookup time", &["encryption.key-lookup-time"], Ms, Down, 2),
            ]),
            row(vec![
                (2.0, vec![x.chart("Gateway", &[("encrypted", &["encryption.gateway-encrypted"]), ("plaintext", &["encryption.gateway-plaintext"]),
                    ("bounced", &["encryption.gateway-bounced"])], Count, 0, 300)]),
                (1.0, vec![x.hbars("Keys & errors", "\u{E8D7}", &[("Key lookups", &["encryption.key-lookup"]), ("Keys harvested", &["encryption.key-harvested"]),
                    ("Key changes", &["encryption.key-change"]), ("Skipped", &["encryption.encryption-skipped"]), ("Bounced", &["encryption.gateway-bounced"]),
                    ("CRL fetch failed", &["encryption.crl-fetch-failed"]), ("DNSSEC bogus", &["encryption.dnssec-bogus"])], Count, 300)]),
            ]),
        ],
        6 => vec![
            cards(vec![
                x.card("Ingest Time", "\u{E916}", "Average message ingestion time", &["message-ingest.time"], Ms, Down, 0),
                x.card("DNS Lookup Time", "\u{E774}", "Average DNS lookup time", &["dns.lookup-time"], Ms, Down, 1),
                x.card("Server Memory", im, "Resident memory of the server", &["server.memory"], Bytes, Neutral, 0),
                x.card("Memory Budget", im, "Memory budget in use", &["limit.memory-budget"], Bytes, Neutral, 2),
            ]),
            row(vec![(1.0, vec![x.chart("Memory", &[("server", &["server.memory"]), ("smtp buffers", &["server.buffered-memory-smtp"]),
                ("read buffers", &["server.buffered-memory-read"])], Bytes, 2, 280)])]),
            row(vec![
                (1.0, vec![x.chart("Ingest time", &[("ingest", &["message-ingest.time"])], Ms, 0, 240)]),
                (1.0, vec![x.chart("DNS lookup time", &[("dns", &["dns.lookup-time"])], Ms, 0, 240)]),
            ]),
        ],
        7 => vec![
            cards(vec![
                x.card("Data Read", "\u{E8B7}", "Average data store read time", &["store.data-read-time"], Ms, Down, 0),
                x.card("Data Write", "\u{E8B7}", "Average data store write time", &["store.data-write-time"], Ms, Down, 1),
                x.card("Blob Read", "\u{E7B8}", "Average blob store read time", &["store.blob-read-time"], Ms, Down, 0),
                x.card("Blob Write", "\u{E7B8}", "Average blob store write time", &["store.blob-write-time"], Ms, Down, 2),
            ]),
            row(vec![
                (1.0, vec![x.chart("Data store", &[("read", &["store.data-read-time"]), ("write", &["store.data-write-time"])], Ms, 2, 280)]),
                (1.0, vec![x.chart("Blob store", &[("read", &["store.blob-read-time"]), ("write", &["store.blob-write-time"])], Ms, 2, 280)]),
            ]),
        ],
        8 => vec![
            cards(vec![
                x.card("Index Time", "\u{E916}", "Average indexing time", &["search-index.index-time"], Ms, Down, 0),
                x.card("Batch Indexed", "\u{E721}", "Documents indexed", &["search-index.batch-indexed"], Count, Up, 1),
                x.card("Truncated", "\u{E8C6}", "Documents truncated while indexing", &["search-index.document-truncated"], Count, Down, 0),
                x.card("Partition Retries", "\u{E72C}", "Index partition retries", &["search-index.partition-retry"], Count, Down, 2),
            ]),
            row(vec![
                (2.0, vec![x.chart("Pending", &[("email", &["search-index.pending-email"]), ("calendar", &["search-index.pending-calendar"]),
                    ("contacts", &["search-index.pending-contacts"]), ("file", &["search-index.pending-file"]), ("tracing", &["search-index.pending-tracing"])], Count, 2, 300)]),
                (1.0, vec![x.hbars("Pending now", "\u{E823}", &[("Email", &["search-index.pending-email"]), ("Calendar", &["search-index.pending-calendar"]),
                    ("Contacts", &["search-index.pending-contacts"]), ("Files", &["search-index.pending-file"]), ("Tracing", &["search-index.pending-tracing"])], Count, 300)]),
            ]),
        ],
        9 => vec![
            cards(vec![
                x.card("Failed", "\u{E783}", "Tasks that failed", &["task-manager.task-failed"], Count, Down, 0),
                x.card("Retried", "\u{E72C}", "Tasks retried", &["task-manager.task-retry"], Count, Down, 1),
                x.card("Stalled", "\u{E769}", "Tasks that stalled", &["task-manager.task-stalled"], Count, Down, 0),
                x.card("Expired", "\u{E823}", "Tasks that expired", &["task-manager.task-expired"], Count, Down, 2),
            ]),
            row(vec![
                (2.0, vec![x.chart("Queue", &[("pending", &["task-manager.pending"]), ("due", &["task-manager.due"])], Count, 2, 300)]),
                (1.0, vec![x.chart("Problems", &[("failed", &["task-manager.task-failed"]), ("retry", &["task-manager.task-retry"]),
                    ("stalled", &["task-manager.task-stalled"]), ("expired", &["task-manager.task-expired"])], Count, 1, 300)]),
            ]),
        ],
        _ => vec![
            row(vec![
                // same 110px height as the stat cards on every other tab
                (1.0, vec![{ let mut p = x.ratio("Hit rate", "\u{E945}", &["cache.hit"], &["cache.miss"], "Lookups served from the cache."); p.height = 110; p }]),
                (1.0, vec![x.card("Cache Hits", "\u{E73E}", "Cache hits", &["cache.hit"], Count, Up, 0)]),
                (1.0, vec![x.card("Cache Misses", "\u{E711}", "Cache misses", &["cache.miss"], Count, Down, 1)]),
                (1.0, vec![x.card("Cache Size", im, "Memory used by caches", &["cache.size"], Bytes, Neutral, 2)]),
            ]),
            row(vec![
                (2.0, vec![x.chart("Lookups", &[("hit", &["cache.hit"]), ("miss", &["cache.miss"])], Count, 0, 300)]),
                (1.0, vec![x.hbars("Other events", "\u{E945}", &[("Stale", &["cache.stale"]), ("Too large", &["cache.entry-too-large"]), ("Swap hits", &["cache.swap-hit"]),
                    ("Swap misses", &["cache.swap-miss"]), ("Swap writes", &["cache.swap-write"]), ("Swap errors", &["cache.swap-error"])], Count, 300)]),
            ]),
            row(vec![(1.0, vec![x.chart("Message cache size", &[("messages", &["cache.messages-size"]), ("large", &["cache.messages-large-size"])], Bytes, 2, 240)])]),
        ],
    }
}

fn fetch(a: &Admin, names: &[&str], from: i64, to: i64) -> Result<Vec<Sample>, String> {
    let iso = |t: i64| chrono::DateTime::from_timestamp(t, 0).unwrap_or_default().to_rfc3339_opts(chrono::SecondsFormat::Secs, true);
    let filter = json!({"timestampIsGreaterThanOrEqual": iso(from), "timestampIsLessThanOrEqual": iso(to), "metric": names});
    let mut ids: Vec<Value> = vec![];
    loop {
        let r = a.call("x:Metric/query", json!({"filter": filter, "position": ids.len(), "limit": 1000}))?;
        let page = r["ids"].as_array().cloned().unwrap_or_default();
        let done = page.len() < 1000 || ids.len() > 50_000;
        ids.extend(page);
        if done { break; }
    }
    let mut out = vec![];
    for chunk in ids.chunks(500) {
        let g = a.call("x:Metric/get", json!({"ids": chunk}))?;
        for m in g["list"].as_array().into_iter().flatten() {
            let Some(t) = m["timestamp"].as_str().and_then(|t| chrono::DateTime::parse_from_rfc3339(t).ok()) else { continue };
            out.push(Sample {
                metric: m["metric"].as_str().unwrap_or("").to_string(),
                kind: match m["@type"].as_str() { Some("Gauge") => Kind::Gauge, Some("Histogram") => Kind::Histogram, _ => Kind::Counter },
                t: t.timestamp(), count: m["count"].as_f64().unwrap_or(0.0), sum: m["sum"].as_f64().unwrap_or(0.0),
            });
        }
    }
    if std::env::var("BARBICAN_DASH_DUMP").is_ok() {
        let mut seen: HashMap<String, usize> = HashMap::new();
        for s in &out { *seen.entry(s.metric.clone()).or_default() += 1; }
        eprintln!("dashboard: {} samples {:?}", out.len(), seen);
    }
    Ok(out)
}

/// Load the dashboard tab (0..=10) for range 0 hour, 1 day, 2 week, 3 month.
pub fn load(u: &AppWindow, c: std::sync::Arc<crate::jmap::Client>, tab: usize, range: usize) {
    let w = u.as_weak();
    u.set_dash_status("Loading…".into());
    std::thread::spawn(move || {
        let h12 = crate::settings::h12();
        let res = (|| -> Result<(Data, usize), String> {
            let a = Admin::new(&c).ok_or("Add a Stalwart admin API key in Settings → Admin.")?;
            let to = chrono::Utc::now().timestamp();
            let (span, buckets) = match range { 0 => (3600, 60), 2 => (7 * 86400, 56), 3 => (30 * 86400, 60), _ => (86400, 48) };
            let from = to - span;
            let samples = fetch(&a, &metrics(tab), from, to)?;
            let mut extra = HashMap::new();
            if tab == 0 {
                for (k, ty) in [("dir.accounts", "Account"), ("dir.domains", "Domain")] {
                    if let Ok(r) = a.call(&format!("x:{ty}/query"), json!({"limit": 0, "calculateTotal": true})) {
                        extra.insert(k.to_string(), r["total"].as_f64().unwrap_or(0.0));
                    }
                }
            }
            Ok((Data { from, to, buckets, samples, extra }, range))
        })();
        let _ = w.upgrade_in_event_loop(move |u| {
            if u.get_dash_tab() as usize != tab || u.get_dash_range() as usize != range { return; }
            match res {
                Ok((d, range)) => {
                    let rows = layout(tab, &Ctx { d: &d, range, h12 });
                    u.set_dash_rows(ModelRc::new(VecModel::from(rows)));
                    u.set_dash_status(if d.samples.is_empty() { "No metrics recorded for this period.".into() } else { "".into() });
                }
                Err(e) => {
                    let msg = if e.contains("unknownMethod") || e.contains("Unknown method") { "Metrics are not available on this server (Enterprise feature).".to_string() } else { e };
                    let d = Data { from: 0, to: 1, buckets: 2, samples: vec![], extra: HashMap::new() };
                    u.set_dash_rows(ModelRc::new(VecModel::from(layout(tab, &Ctx { d: &d, range, h12 })))) ;
                    u.set_dash_status(msg.into());
                }
            }
        });
    });
}

#[cfg(test)]
mod tests {
    use super::*;

    fn flat_data() -> Data {
        let s = |t| Sample { metric: "m".into(), kind: Kind::Counter, t, count: 5.0, sum: 0.0 };
        Data { from: 0, to: 3600, buckets: 4, samples: vec![s(100), s(2000)], extra: HashMap::new() }
    }

    #[test]
    fn zero_change_is_neutral() {
        let d = flat_data();
        let p = Ctx { d: &d, range: 0, h12: false }.card("t", "", "", &["m"], Unit::Count, Pol::Up, 0);
        assert_eq!(p.change.as_str(), "0%");
        assert!(p.flat);
    }

    #[test]
    fn time_labels_follow_time_format() {
        let d = flat_data();
        let t = 13 * 3600 + 5 * 60;
        let h24 = Ctx { d: &d, range: 0, h12: false }.when(t);
        let h12 = Ctx { d: &d, range: 0, h12: true }.when(t);
        assert!(!h24.contains('M'), "{h24}");
        assert!(h12.ends_with("AM") || h12.ends_with("PM"), "{h12}");
    }

    #[test]
    fn sub_millisecond_ticks_are_distinct() {
        let ticks: Vec<String> = (0..5).map(|i| fmt(1.0 * i as f64 / 4.0, Unit::Ms)).collect();
        assert_eq!(ticks, ["0 ms", "0.25 ms", "0.5 ms", "0.75 ms", "1 ms"]);
        assert_eq!(fmt(12.4, Unit::Ms), "12 ms");
    }
}

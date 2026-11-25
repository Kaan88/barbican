//! A fake HTTP server for the provider tests: a `TcpListener` on `127.0.0.1:0` that answers one
//! scripted reply per connection, in order, and records each request. Replies are sent with
//! chunked transfer encoding, one HTTP chunk per scripted piece with a short pause between them,
//! so the client sees the pieces as separate reads.
use std::io::{Read, Write};
use std::net::{TcpListener, TcpStream};
use std::sync::{Arc, Mutex};
use std::time::Duration;

#[derive(Clone, Debug)]
pub struct Recorded {
    pub method: String,
    pub path: String,
    pub headers: Vec<(String, String)>,
    pub body: String,
}

impl Recorded {
    pub fn header(&self, name: &str) -> Option<String> {
        self.headers.iter().find(|(n, _)| n.eq_ignore_ascii_case(name)).map(|(_, v)| v.clone())
    }
}

pub struct Reply {
    status: u16,
    /// the reason phrase; the standard one when `None`
    reason: Option<&'static str>,
    content_type: &'static str,
    extra: Vec<(String, String)>,
    /// before the status line
    delay_ms: u64,
    parts: Vec<Vec<u8>>,
    /// between two parts
    gap_ms: u64,
    /// after the last part, before the end of the body
    pause_ms: u64,
}

impl Reply {
    pub fn stream(status: u16, parts: Vec<Vec<u8>>) -> Reply {
        Reply { status, reason: None, content_type: "text/event-stream", extra: vec![], delay_ms: 0, parts, gap_ms: 10, pause_ms: 0 }
    }

    pub fn json(status: u16, body: &str) -> Reply {
        let parts = if body.is_empty() { vec![] } else { vec![body.as_bytes().to_vec()] };
        Reply { content_type: "application/json", ..Reply::stream(status, parts) }
    }

    pub fn redirect(status: u16, location: &str) -> Reply {
        Reply { extra: vec![("location".into(), location.into())], ..Reply::json(status, "") }
    }

    /// Keeps the connection open this long after the last part.
    pub fn then_pause(self, ms: u64) -> Reply {
        Reply { pause_ms: ms, ..self }
    }

    /// Waits this long before answering at all.
    pub fn after(self, ms: u64) -> Reply {
        Reply { delay_ms: ms, ..self }
    }

    pub fn reason(self, phrase: &'static str) -> Reply {
        Reply { reason: Some(phrase), ..self }
    }

    pub fn with_gap(self, ms: u64) -> Reply {
        Reply { gap_ms: ms, ..self }
    }
}

pub struct FakeServer {
    /// `http://127.0.0.1:<port>`
    pub base: String,
    requests: Arc<Mutex<Vec<Recorded>>>,
}

fn reason(status: u16) -> &'static str {
    reqwest::StatusCode::from_u16(status).ok().and_then(|s| s.canonical_reason()).unwrap_or("Unknown")
}

fn read_request(s: &mut TcpStream) -> Option<Recorded> {
    let mut buf = Vec::new();
    let mut byte = [0u8; 1024];
    let head_end = loop {
        if let Some(i) = buf.windows(4).position(|w| w == b"\r\n\r\n") {
            break i;
        }
        let n = s.read(&mut byte).ok()?;
        if n == 0 {
            return None;
        }
        buf.extend_from_slice(&byte[..n]);
    };
    let head = String::from_utf8_lossy(&buf[..head_end]).to_string();
    let mut lines = head.split("\r\n");
    let mut first = lines.next()?.split(' ');
    let method = first.next()?.to_string();
    let path = first.next()?.to_string();
    let headers: Vec<(String, String)> = lines
        .filter_map(|l| l.split_once(':'))
        .map(|(n, v)| (n.trim().to_ascii_lowercase(), v.trim().to_string()))
        .collect();
    let len: usize = headers.iter().find(|(n, _)| n == "content-length").and_then(|(_, v)| v.parse().ok()).unwrap_or(0);
    let mut body = buf[head_end + 4..].to_vec();
    while body.len() < len {
        let n = s.read(&mut byte).ok()?;
        if n == 0 {
            break;
        }
        body.extend_from_slice(&byte[..n]);
    }
    Some(Recorded { method, path, headers, body: String::from_utf8_lossy(&body).to_string() })
}

fn answer(s: &mut TcpStream, r: &Reply) -> std::io::Result<()> {
    std::thread::sleep(Duration::from_millis(r.delay_ms));
    let mut head = format!(
        "HTTP/1.1 {} {}\r\ncontent-type: {}\r\ntransfer-encoding: chunked\r\nconnection: close\r\n",
        r.status,
        r.reason.unwrap_or_else(|| reason(r.status)),
        r.content_type
    );
    for (n, v) in &r.extra {
        head.push_str(&format!("{n}: {v}\r\n"));
    }
    head.push_str("\r\n");
    s.write_all(head.as_bytes())?;
    s.flush()?;
    for (i, p) in r.parts.iter().enumerate() {
        if i > 0 {
            std::thread::sleep(Duration::from_millis(r.gap_ms));
        }
        s.write_all(format!("{:x}\r\n", p.len()).as_bytes())?;
        s.write_all(p)?;
        s.write_all(b"\r\n")?;
        s.flush()?;
    }
    std::thread::sleep(Duration::from_millis(r.pause_ms));
    s.write_all(b"0\r\n\r\n")?;
    s.flush()
}

impl FakeServer {
    pub fn start(replies: Vec<Reply>) -> FakeServer {
        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        let base = format!("http://{}", listener.local_addr().unwrap());
        let requests = Arc::new(Mutex::new(Vec::new()));
        let rec = requests.clone();
        std::thread::spawn(move || {
            for reply in replies {
                let Ok((mut s, _)) = listener.accept() else { return };
                let Some(req) = read_request(&mut s) else { return };
                rec.lock().unwrap().push(req);
                let _ = answer(&mut s, &reply);
            }
            // the listener closes here: a request nobody scripted is refused
        });
        FakeServer { base, requests }
    }

    pub fn requests(&self) -> Vec<Recorded> {
        self.requests.lock().unwrap().clone()
    }
}

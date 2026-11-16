//! Single-instance handoff for mailto: links (GAPS P0-8). When the OS starts Barbican for a
//! mailto: link while Barbican already runs, the new process hands the link to the running
//! one over a loopback socket and exits, so the link opens in the window the user has.
//!
//! Every instance listens on 127.0.0.1 (a free port) and writes the port and a random token
//! to `instance.json` in the config folder; a later launch reads them, sends
//! "<token>\n<url>\n" and waits for "ok". The token keeps other local programs from
//! injecting links. Plain launches without a link stay independent instances (as before).
//! With BARBICAN_AUTOMATION the file is per automation port, so test instances never take
//! links meant for the user's own Barbican.
use std::io::{BufRead, BufReader, Read, Write};
use std::net::{SocketAddr, TcpListener, TcpStream};
use std::time::Duration;

fn file() -> std::path::PathBuf {
    let name = match std::env::var("BARBICAN_AUTOMATION_PORT").ok().filter(|_| crate::automation::enabled()) {
        Some(p) => format!("instance-{}.json", p.trim()),
        None => "instance.json".into(),
    };
    crate::config_dir().join(name)
}

/// The first command-line argument that is a mailto: link (as the OS passes it, "%1").
pub fn mailto_arg(args: &[String]) -> Option<String> {
    args.iter().map(|a| a.trim().trim_matches('"').to_string()).find(|a| crate::mailto::parse(a).is_some())
}

/// Where a running instance listens: port, the token a client proves itself with, and the
/// greeting the server proves itself with.
#[derive(Clone, Debug, PartialEq)]
pub struct Endpoint { pub port: u16, pub token: String, pub greet: String, pub pid: u32 }

fn read_endpoint() -> Option<Endpoint> {
    let v: serde_json::Value = serde_json::from_str(&std::fs::read_to_string(file()).ok()?).ok()?;
    Some(Endpoint {
        port: v["port"].as_u64()? as u16,
        token: v["token"].as_str()?.to_string(),
        greet: v["greet"].as_str()?.to_string(),
        pid: v["pid"].as_u64().unwrap_or(0) as u32,
    })
}

/// Send `url` to the instance at `e`; true when it took it. The server speaks first: when the
/// port now belongs to another program (Barbican crashed and left a stale file), its first
/// line is not our greeting and nothing, neither token nor link, is sent to it.
pub fn send(e: &Endpoint, url: &str) -> bool {
    let addr = SocketAddr::from(([127, 0, 0, 1], e.port));
    let Ok(s) = TcpStream::connect_timeout(&addr, Duration::from_millis(800)) else { return false };
    let _ = s.set_read_timeout(Some(Duration::from_secs(2)));
    let Ok(mut w) = s.try_clone() else { return false };
    let mut r = BufReader::new(s);
    let mut hello = String::new();
    if r.read_line(&mut hello).is_err() || hello.trim() != e.greet { return false; }
    let _ = r.get_ref().set_read_timeout(Some(Duration::from_secs(5)));
    if writeln!(w, "{}\n{}", e.token, url.replace(['\r', '\n'], "")).is_err() { return false; }
    let mut line = String::new();
    r.read_line(&mut line).is_ok() && line.trim() == "ok"
}

/// Hand `url` to a running Barbican; true when one took it (this process can exit).
pub fn forward(url: &str) -> bool {
    read_endpoint().is_some_and(|e| send(&e, url))
}

/// 128 random bits as hex; None when the system has no randomness to give (then this
/// instance does not listen: a guessable token would let any local program inject links).
fn random_hex() -> Option<String> {
    let mut b = [0u8; 16];
    getrandom::getrandom(&mut b).ok()?;
    Some(b.iter().map(|x| format!("{x:02x}")).collect())
}

/// Read one handoff from a connection: the URL when the token matches.
pub fn read_request(stream: impl Read, token: &str) -> Option<String> {
    let mut r = BufReader::new(stream.take(256 * 1024));
    let (mut t, mut url) = (String::new(), String::new());
    r.read_line(&mut t).ok()?;
    r.read_line(&mut url).ok()?;
    if t.trim() != token { return None; }
    let url = url.trim().to_string();
    crate::mailto::parse(&url).is_some().then_some(url)
}

/// Connections handled at once; more are closed straight away.
const MAX_CONNS: usize = 8;

/// Serve handoffs from later launches: each link goes to `on_link`. Every connection gets its
/// own thread and a short deadline, so idle connections cannot hold up the next launch.
pub fn serve(listener: TcpListener, token: String, greet: String, on_link: impl Fn(String) + Send + Sync + 'static) {
    use std::sync::atomic::{AtomicUsize, Ordering};
    use std::sync::Arc;
    let on_link = Arc::new(on_link);
    let busy = Arc::new(AtomicUsize::new(0));
    let (token, greet) = (Arc::new(token), Arc::new(greet));
    std::thread::spawn(move || {
        for mut stream in listener.incoming().flatten() {
            if busy.load(Ordering::SeqCst) >= MAX_CONNS { continue; }
            busy.fetch_add(1, Ordering::SeqCst);
            let (on_link, busy, token, greet) = (on_link.clone(), busy.clone(), token.clone(), greet.clone());
            std::thread::spawn(move || {
                let _ = stream.set_read_timeout(Some(Duration::from_secs(2)));
                let _ = stream.set_write_timeout(Some(Duration::from_secs(2)));
                if writeln!(stream, "{greet}").is_ok() {
                    if let Ok(read) = stream.try_clone() {
                        match read_request(read, &token) {
                            // the link is kept until it opens (mailto::dispatch waits for a sign-in)
                            Some(url) => { on_link(url); let _ = writeln!(stream, "ok"); }
                            None => { let _ = writeln!(stream, "no"); }
                        }
                    }
                }
                busy.fetch_sub(1, Ordering::SeqCst);
            });
        }
    });
}

thread_local! { static MINE: std::cell::RefCell<Option<Endpoint>> = const { std::cell::RefCell::new(None) }; }

/// Listen for links from later launches and record where (this instance becomes the target).
pub fn listen() {
    let (Some(token), Some(greet)) = (random_hex(), random_hex()) else { return };
    let greet = format!("barbican {greet}");
    let Ok(listener) = TcpListener::bind(("127.0.0.1", 0)) else { return };
    let Ok(port) = listener.local_addr().map(|a| a.port()) else { return };
    let _ = std::fs::create_dir_all(crate::config_dir());
    let pid = std::process::id();
    let data = serde_json::json!({"port": port, "token": token, "greet": greet, "pid": pid});
    if std::fs::write(file(), data.to_string()).is_err() { return; }
    MINE.with(|m| *m.borrow_mut() = Some(Endpoint { port, token: token.clone(), greet: greet.clone(), pid }));
    serve(listener, token, greet, crate::mailto::request);
}

/// Clean exit: remove the file when it still points at this instance (a later instance may
/// have taken over).
pub fn cleanup() {
    let Some(mine) = MINE.with(|m| m.borrow().clone()) else { return };
    if read_endpoint().as_ref() == Some(&mine) { let _ = std::fs::remove_file(file()); }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn args_and_requests() {
        let a = |v: &[&str]| mailto_arg(&v.iter().map(|s| s.to_string()).collect::<Vec<_>>());
        assert_eq!(a(&["--flag", "\"mailto:a@x.io?subject=Hi\""]), Some("mailto:a@x.io?subject=Hi".into()));
        assert_eq!(a(&["http://x"]), None);
        assert_eq!(read_request(&b"tok\nmailto:a@x.io\n"[..], "tok"), Some("mailto:a@x.io".into()));
        assert_eq!(read_request(&b"bad\nmailto:a@x.io\n"[..], "tok"), None);
        assert_eq!(read_request(&b"tok\nfile:///etc/passwd\n"[..], "tok"), None);
    }

    #[test]
    fn handoff_over_loopback() {
        let l = TcpListener::bind(("127.0.0.1", 0)).unwrap();
        let port = l.local_addr().unwrap().port();
        let (tx, rx) = std::sync::mpsc::channel();
        let tx = std::sync::Mutex::new(tx);
        serve(l, "secret".into(), "barbican hi".into(), move |u| { let _ = tx.lock().unwrap().send(u); });
        let e = |token: &str, greet: &str| Endpoint { port, token: token.into(), greet: greet.into(), pid: 0 };
        assert!(!send(&e("wrong", "barbican hi"), "mailto:a@x.io"));
        // a server that does not greet as expected gets nothing
        assert!(!send(&e("secret", "barbican other"), "mailto:a@x.io"));
        assert!(rx.try_recv().is_err());
        // idle connections do not block a real handoff
        let idle: Vec<_> = (0..3).map(|_| TcpStream::connect(("127.0.0.1", port)).unwrap()).collect();
        assert!(send(&e("secret", "barbican hi"), "mailto:a@x.io?subject=Hand%20off"));
        assert_eq!(rx.recv_timeout(Duration::from_secs(3)).unwrap(), "mailto:a@x.io?subject=Hand%20off");
        drop(idle);
    }

    #[test]
    fn foreign_program_on_a_stale_port_gets_nothing() {
        let l = TcpListener::bind(("127.0.0.1", 0)).unwrap();
        let port = l.local_addr().unwrap().port();
        let got = std::thread::spawn(move || {
            let (mut s, _) = l.accept().unwrap();
            let _ = s.set_read_timeout(Some(Duration::from_millis(1500)));
            let _ = writeln!(s, "HTTP/1.1 400 Bad Request");
            let mut buf = vec![];
            let _ = s.read_to_end(&mut buf);
            buf
        });
        assert!(!send(&Endpoint { port, token: "secret".into(), greet: "barbican hi".into(), pid: 0 }, "mailto:a@x.io?body=private"));
        assert!(got.join().unwrap().is_empty());
    }
}

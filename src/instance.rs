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

fn read_endpoint() -> Option<(u16, String)> {
    let v: serde_json::Value = serde_json::from_str(&std::fs::read_to_string(file()).ok()?).ok()?;
    Some((v["port"].as_u64()? as u16, v["token"].as_str()?.to_string()))
}

/// Send `url` to the instance listening on `port`; true when it took it.
pub fn send(port: u16, token: &str, url: &str) -> bool {
    let addr = SocketAddr::from(([127, 0, 0, 1], port));
    let Ok(mut s) = TcpStream::connect_timeout(&addr, Duration::from_millis(1500)) else { return false };
    let _ = s.set_read_timeout(Some(Duration::from_secs(5)));
    if writeln!(s, "{token}\n{}", url.replace(['\r', '\n'], "")).is_err() { return false; }
    let mut line = String::new();
    BufReader::new(s).read_line(&mut line).is_ok() && line.trim() == "ok"
}

/// Hand `url` to a running Barbican; true when one took it (this process can exit).
pub fn forward(url: &str) -> bool {
    match read_endpoint() { Some((port, token)) => send(port, &token, url), None => false }
}

fn token() -> String {
    let mut b = [0u8; 16];
    let _ = getrandom::getrandom(&mut b);
    b.iter().map(|x| format!("{x:02x}")).collect()
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

/// Serve handoffs from later launches: each link goes to `on_link`.
pub fn serve(listener: TcpListener, token: String, on_link: impl Fn(String) + Send + 'static) {
    std::thread::spawn(move || {
        for mut stream in listener.incoming().flatten() {
            let _ = stream.set_read_timeout(Some(Duration::from_secs(3)));
            let Ok(read) = stream.try_clone() else { continue };
            match read_request(read, &token) {
                Some(url) => { on_link(url); let _ = writeln!(stream, "ok"); }
                None => { let _ = writeln!(stream, "no"); }
            }
        }
    });
}

/// Listen for links from later launches and record where (this instance becomes the target).
pub fn listen() {
    let Ok(listener) = TcpListener::bind(("127.0.0.1", 0)) else { return };
    let Ok(port) = listener.local_addr().map(|a| a.port()) else { return };
    let token = token();
    let _ = std::fs::create_dir_all(crate::config_dir());
    let data = serde_json::json!({"port": port, "token": token, "pid": std::process::id()});
    if std::fs::write(file(), data.to_string()).is_err() { return; }
    serve(listener, token, crate::mailto::request);
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
        serve(l, "secret".into(), move |u| { let _ = tx.send(u); });
        assert!(!send(port, "wrong", "mailto:a@x.io"));
        assert!(send(port, "secret", "mailto:a@x.io?subject=Hand%20off"));
        assert_eq!(rx.recv_timeout(Duration::from_secs(3)).unwrap(), "mailto:a@x.io?subject=Hand%20off");
    }
}

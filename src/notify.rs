//! Native notifications (Bulwark lib/desktop/mail-notifications.ts and
//! reminder-notifications.ts): WinRT toasts on Windows with Barbican's app id, a tag and a
//! group per account, buttons and a click handler; freedesktop notifications on Linux with
//! the same actions. A click or a button comes back as an [`Activation`] through the handler
//! main.rs installs, on the UI thread.
use std::sync::Mutex;

#[derive(Debug, Clone, PartialEq)]
pub struct Note {
    /// Tag: showing the same id again replaces the earlier one.
    pub id: String,
    /// Toasts of one group are collapsed together (one per account, or "reminders").
    pub group: String,
    pub title: String,
    pub body: String,
    /// Handed back with the activation: "mail:<email id>", "inbox", "event:<id>".
    pub context: String,
    /// (action id, label)
    pub actions: Vec<(String, String)>,
    pub sound: bool,
}

/// What the user did with a notification.
#[derive(Debug, Clone, PartialEq)]
pub struct Activation {
    /// "open" for a click on the notification itself, else the button's id.
    pub action: String,
    pub context: String,
}

/// Encoded as one string where the platform hands back a single argument (toasts).
pub fn encode(action: &str, context: &str) -> String {
    format!("{action}|{context}")
}

pub fn decode(arg: &str) -> Activation {
    match arg.split_once('|') {
        Some((a, c)) => Activation { action: a.to_string(), context: c.to_string() },
        None => Activation { action: "open".into(), context: arg.to_string() },
    }
}

/// Mail that arrived together (Bulwark buildMailNotification): one message gets its sender
/// and subject with Archive, Mark as read and Delete; several get a count and open the Inbox.
pub struct NewMail {
    pub id: String,
    pub sender: String,
    pub subject: String,
    pub preview: String,
}

pub fn mail_note(batch: &[NewMail], account: &str, sound: bool, archive: bool) -> Option<Note> {
    let newest = batch.first()?;
    let subject = if newest.subject.trim().is_empty() { "(no subject)" } else { newest.subject.as_str() };
    let base = Note {
        id: format!("mail:{account}/{}", newest.id),
        group: format!("mail:{account}"),
        title: String::new(),
        body: String::new(),
        context: String::new(),
        actions: vec![],
        sound,
    };
    if batch.len() > 1 {
        return Some(Note {
            title: format!("{} new messages", batch.len()),
            body: format!("{}: {subject}", newest.sender),
            context: "inbox".into(),
            ..base
        });
    }
    let mut actions = vec![];
    if archive { actions.push(("archive".to_string(), "Archive".to_string())); }
    actions.push(("mark-read".into(), "Mark as read".into()));
    actions.push(("delete".into(), "Delete".into()));
    let body = if newest.preview.trim().is_empty() { subject.to_string() } else { format!("{subject}\n{}", newest.preview.trim()) };
    Some(Note {
        title: if newest.sender.is_empty() { "New mail".into() } else { newest.sender.clone() },
        body,
        context: format!("mail:{}", newest.id),
        actions,
        ..base
    })
}

/// Calendar reminder hook for the calendar: "Team call" / "In 10 min · Personal".
/// Activations come back with context "event:<id>" and action "open" (or a snooze id
/// "snooze-5|10|15" the calendar handles).
#[allow(dead_code)]
pub fn reminder_note(event_id: &str, fire_ms: i64, title: &str, when: &str, sound: bool) -> Note {
    let mut actions = vec![("open".to_string(), "Open".to_string())];
    for m in [5, 10, 15] { actions.push((format!("snooze-{m}"), format!("{m} min"))); }
    Note {
        id: format!("reminder:{event_id}:{fire_ms}"),
        group: "reminders".into(),
        title: if title.trim().is_empty() { "(untitled event)".into() } else { title.to_string() },
        body: when.to_string(),
        context: format!("event:{event_id}"),
        actions,
        sound,
    }
}

type Handler = Box<dyn Fn(Activation) + Send + Sync>;
static HANDLER: Mutex<Option<Handler>> = Mutex::new(None);
/// Automation: notifications are recorded instead of shown, newest last.
pub static SHOWN: Mutex<Vec<Note>> = Mutex::new(Vec::new());

/// Where clicks and buttons go (main.rs forwards them to the UI thread).
pub fn set_handler(f: impl Fn(Activation) + Send + Sync + 'static) {
    *HANDLER.lock().unwrap() = Some(Box::new(f));
}

pub fn activate(a: Activation) {
    if let Some(h) = &*HANDLER.lock().unwrap() { h(a); }
}

/// Show a notification (from any thread).
pub fn show(n: Note) {
    if crate::automation::enabled() {
        let mut s = SHOWN.lock().unwrap();
        s.push(n);
        let len = s.len();
        if len > 50 { s.drain(..len - 50); }
        return;
    }
    sys::show(n);
}

#[cfg(windows)]
mod sys {
    use super::*;
    use windows::core::HSTRING;
    use windows::Data::Xml::Dom::XmlDocument;
    use windows::Foundation::TypedEventHandler;
    use windows::UI::Notifications::{ToastActivatedEventArgs, ToastNotification, ToastNotificationManager};
    use windows::core::Interface;

    /// PowerShell's id: Windows shows toasts for it on every machine (what notify-rust uses).
    const POWERSHELL_APP_ID: &str = "{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\\WindowsPowerShell\\v1.0\\powershell.exe";

    thread_local! {
        // the toast objects stay alive while they can still be clicked
        static LIVE: std::cell::RefCell<Vec<ToastNotification>> = const { std::cell::RefCell::new(Vec::new()) };
    }

    pub fn app_id() -> &'static str {
        if crate::platform::win::app_id_registered() { crate::platform::win::APP_ID } else { POWERSHELL_APP_ID }
    }

    pub fn show(n: Note) {
        let xml = toast_xml(&n);
        let res = (|| -> windows::core::Result<()> {
            let doc = XmlDocument::new()?;
            doc.LoadXml(&HSTRING::from(xml))?;
            let toast = ToastNotification::CreateToastNotification(&doc)?;
            // a tag is at most 64 characters
            let tag: String = n.id.chars().rev().take(64).collect::<Vec<_>>().into_iter().rev().collect();
            toast.SetTag(&HSTRING::from(tag))?;
            toast.SetGroup(&HSTRING::from(n.group.chars().take(64).collect::<String>()))?;
            toast.Activated(&TypedEventHandler::new(move |_, args: windows::core::Ref<windows::core::IInspectable>| {
                if let Some(a) = args.as_ref().and_then(|i| i.cast::<ToastActivatedEventArgs>().ok()).and_then(|a| a.Arguments().ok()) {
                    activate(decode(&a.to_string()));
                }
                Ok(())
            }))?;
            ToastNotificationManager::CreateToastNotifierWithId(&HSTRING::from(app_id()))?.Show(&toast)?;
            LIVE.with(|l| { let mut l = l.borrow_mut(); l.push(toast); let n = l.len(); if n > 30 { l.drain(..n - 30); } });
            Ok(())
        })();
        if res.is_err() {
            // a Windows without the toast platform: the plain notification from before
            let _ = notify_rust::Notification::new().appname("Barbican").summary(&n.title).body(&n.body).show();
        }
    }
}

#[cfg(not(windows))]
mod sys {
    use super::*;

    pub fn show(n: Note) {
        std::thread::spawn(move || {
            let mut x = notify_rust::Notification::new();
            x.appname("Barbican").summary(&n.title).body(&n.body).icon("barbican");
            if n.sound { x.sound_name("message-new-email"); }
            if n.group.starts_with("mail:") { x.hint(notify_rust::Hint::Category("email.arrived".into())); }
            // "default" is a click on the notification itself
            x.action("default", "Open");
            for (id, label) in &n.actions { x.action(id, label); }
            let Ok(handle) = x.show() else { return };
            let context = n.context.clone();
            handle.wait_for_action(move |action| {
                if action == "__closed" { return; }
                let action = if action == "default" { "open" } else { action };
                activate(Activation { action: action.to_string(), context: context.clone() });
            });
        });
    }
}

/// The toast's XML (Windows): the click hands back "open|context", each button "id|context".
#[cfg_attr(not(windows), allow(dead_code))]
pub fn toast_xml(n: &Note) -> String {
    let x = |s: &str| s.replace('&', "&amp;").replace('<', "&lt;").replace('>', "&gt;").replace('"', "&quot;");
    let mut body = String::new();
    for line in n.body.lines().filter(|l| !l.trim().is_empty()).take(2) { body.push_str(&format!("<text>{}</text>", x(line))); }
    let actions: String = n.actions.iter().map(|(id, label)| format!("<action content=\"{}\" arguments=\"{}\" activationType=\"foreground\"/>", x(label), x(&encode(id, &n.context)))).collect();
    let audio = if !n.sound { "<audio silent=\"true\"/>".to_string() }
        else if n.group.starts_with("mail:") { "<audio src=\"ms-winsoundevent:Notification.Mail\"/>".to_string() }
        else if n.group == "reminders" { "<audio src=\"ms-winsoundevent:Notification.Reminder\"/>".to_string() }
        else { String::new() };
    let scenario = if n.group == "reminders" { " scenario=\"reminder\"" } else { "" };
    format!("<toast launch=\"{}\" activationType=\"foreground\"{scenario}><visual><binding template=\"ToastGeneric\"><text>{}</text>{body}</binding></visual>{}{audio}</toast>",
        x(&encode("open", &n.context)), x(&n.title), if actions.is_empty() { String::new() } else { format!("<actions>{actions}</actions>") })
}

#[cfg(test)]
mod tests {
    use super::*;

    fn m(id: &str, sender: &str, subject: &str) -> NewMail {
        NewMail { id: id.into(), sender: sender.into(), subject: subject.into(), preview: "Hello there".into() }
    }

    #[test]
    fn one_message() {
        let n = mail_note(&[m("e1", "Ada", "Plans")], "acc", true, true).unwrap();
        assert_eq!(n.title, "Ada");
        assert_eq!(n.body, "Plans\nHello there");
        assert_eq!(n.context, "mail:e1");
        assert_eq!(n.group, "mail:acc");
        assert_eq!(n.id, "mail:acc/e1");
        let ids: Vec<&str> = n.actions.iter().map(|a| a.0.as_str()).collect();
        assert_eq!(ids, ["archive", "mark-read", "delete"]);
        let n = mail_note(&[m("e1", "", "")], "acc", false, false).unwrap();
        assert_eq!(n.title, "New mail");
        assert!(n.body.starts_with("(no subject)"));
        assert_eq!(n.actions[0].0, "mark-read");
    }

    #[test]
    fn toast_xml_escapes() {
        let n = mail_note(&[m("e1", "Tom & <Jerry>", "a < b & \"c\"")], "acc", true, true).unwrap();
        let x = toast_xml(&n);
        assert!(x.contains("<text>Tom &amp; &lt;Jerry&gt;</text>"));
        assert!(x.contains("a &lt; b &amp; &quot;c&quot;"));
        assert!(!x.contains("<Jerry>") && !x.contains("& <"));
    }

    #[test]
    fn batch_opens_inbox() {
        let n = mail_note(&[m("e2", "Bo", "Second"), m("e1", "Ada", "First")], "acc", true, true).unwrap();
        assert_eq!(n.title, "2 new messages");
        assert_eq!(n.body, "Bo: Second");
        assert_eq!(n.context, "inbox");
        assert!(n.actions.is_empty());
        assert!(mail_note(&[], "acc", true, true).is_none());
    }

    #[test]
    fn activation_round_trip() {
        assert_eq!(decode(&encode("archive", "mail:e1")), Activation { action: "archive".into(), context: "mail:e1".into() });
        assert_eq!(decode("mail:e9"), Activation { action: "open".into(), context: "mail:e9".into() });
        // ids with a bar keep it in the context
        assert_eq!(decode("open|event:a|b").context, "event:a|b");
    }

    #[test]
    fn toast_markup() {
        let n = mail_note(&[m("e1", "A & B <x>", "Q\"s")], "acc", true, true).unwrap();
        let x = toast_xml(&n);
        assert!(x.starts_with("<toast launch=\"open|mail:e1\""), "{x}");
        assert!(x.contains("<text>A &amp; B &lt;x&gt;</text><text>Q&quot;s</text><text>Hello there</text>"), "{x}");
        assert!(x.contains("<action content=\"Archive\" arguments=\"archive|mail:e1\""));
        assert!(x.contains("ms-winsoundevent:Notification.Mail"));
        let silent = toast_xml(&Note { sound: false, ..n });
        assert!(silent.contains("<audio silent=\"true\"/>"));
        let r = reminder_note("ev1", 1000, "Call", "In 10 min", true);
        let x = toast_xml(&r);
        assert!(x.contains("scenario=\"reminder\"") && x.contains("snooze-5|event:ev1"), "{x}");
    }
}

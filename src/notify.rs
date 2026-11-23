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
    /// Handed back with the activation: "mail:<email id>@<folder id>@<account>" (the folder and
    /// account parts are optional: older toasts carry "mail:<email id>"), "inbox", "event:<id>".
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

/// The context of a one-message toast: "mail:<id>@<folder id>@<account>". JMAP ids use only
/// letters, digits, '-' and '_', so '@' separates them; the account (a login, which may contain
/// '@' itself) goes last and takes the rest.
pub fn mail_context(id: &str, mailbox: &str, account: &str) -> String {
    if mailbox.is_empty() && account.is_empty() { return format!("mail:{id}"); }
    format!("mail:{id}@{mailbox}@{account}")
}

/// A message a toast points at.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct MailRef {
    pub id: String,
    /// The folder it was in when the toast was made (None on a toast from before folders were carried).
    pub mailbox: Option<String>,
    /// The login whose mail it is (None on an older toast).
    pub account: Option<String>,
}

/// "mail:..." back into its parts; None for other contexts. Reads both the old "mail:<id>" and
/// the current form.
pub fn parse_mail_context(ctx: &str) -> Option<MailRef> {
    let rest = ctx.strip_prefix("mail:")?;
    let mut parts = rest.splitn(3, '@');
    let id = parts.next().unwrap_or("");
    if id.is_empty() { return None; }
    let some = |s: Option<&str>| s.filter(|s| !s.is_empty()).map(String::from);
    Some(MailRef { id: id.to_string(), mailbox: some(parts.next()), account: some(parts.next()) })
}

/// What the window shows when a toast is activated.
#[derive(Debug, Clone, Default)]
pub struct View {
    /// Login of the signed-in session.
    pub account: String,
    /// The selected folder id ("" for a view that is not a folder, "__unified" for All inboxes).
    pub folder: String,
    /// A search or a tag narrows the list, so the message might not be in it.
    pub narrowed: bool,
    /// The message is in the list on screen.
    pub listed: bool,
    /// Ids of the session's folders (empty while they are not loaded yet).
    pub folders: Vec<String>,
    pub inbox: Option<String>,
}

/// Where the server says a message is.
#[derive(Debug, Clone, PartialEq)]
pub enum Location {
    /// In these folders.
    In(Vec<String>),
    /// No longer exists (deleted for good).
    Gone,
    /// Not asked, or the server did not answer: the toast's own hint decides.
    Unknown,
}

#[derive(Debug, Clone, PartialEq, Default)]
pub struct OpenPlan {
    /// Folder to select (clearing search and tag) before anything else; None keeps the view.
    pub folder: Option<String>,
    /// Open the message after that.
    pub open: bool,
    /// Said to the user when the message could not be opened.
    pub notice: Option<&'static str>,
}

fn known(v: &View, id: &str) -> bool { v.folders.is_empty() || v.folders.iter().any(|f| f == id) }

/// The folder to select to show `target`: None when it is already shown as it is.
pub fn folder_to_select(target: &str, v: &View) -> Option<String> {
    (target != v.folder || v.narrowed).then(|| target.to_string())
}

/// Which folder, and whether to open the message, when a toast for `r` is activated
/// (Bulwark's showInOwningFolder: the message's own folder is selected, its account is checked
/// first, a message that is gone only says so). The All inboxes view is left for the folder:
/// its rows are keyed per source, so the message could not be highlighted there.
pub fn plan_mail_open(r: &MailRef, v: &View, at: &Location) -> OpenPlan {
    // another login's message id means nothing here (Bulwark opens the Inbox instead)
    if r.account.as_deref().is_some_and(|a| !v.account.is_empty() && a != v.account) {
        return OpenPlan { folder: v.inbox.as_deref().and_then(|i| folder_to_select(i, v)), open: false, notice: None };
    }
    let hint = r.mailbox.as_deref().filter(|m| known(v, m));
    let target = match at {
        Location::Gone => return OpenPlan { folder: None, open: false, notice: Some("That message is no longer there.") },
        Location::In(boxes) => {
            let mut have: Vec<&String> = boxes.iter().filter(|b| v.folders.iter().any(|f| f == *b)).collect();
            have.sort();
            if have.iter().any(|b| **b == v.folder) { Some(v.folder.as_str()) }
            else {
                hint.filter(|h| have.iter().any(|b| *b == h))
                    .or(v.inbox.as_deref().filter(|i| have.iter().any(|b| *b == i)))
                    .or(have.first().map(|b| b.as_str()))
                    .or(hint)
            }
        }
        Location::Unknown => hint,
    };
    let target = match target {
        Some(t) => Some(t),
        // nothing says where it is: where it is listed, else the Inbox it most likely arrived in
        None if v.listed => None,
        None => v.inbox.as_deref(),
    };
    let folder = target.and_then(|t| folder_to_select(t, v));
    OpenPlan { folder, open: true, notice: None }
}

/// Mail that arrived together (Bulwark buildMailNotification): one message gets its sender
/// and subject with Archive, Mark as read and Delete; several get a count and open the Inbox.
pub struct NewMail {
    pub id: String,
    /// The folder the message arrived in (its Inbox, or the folder a rule filed it to).
    pub mailbox: String,
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
        context: mail_context(&newest.id, &newest.mailbox, account),
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
        NewMail { id: id.into(), mailbox: "mb1".into(), sender: sender.into(), subject: subject.into(), preview: "Hello there".into() }
    }

    #[test]
    fn one_message() {
        let n = mail_note(&[m("e1", "Ada", "Plans")], "acc", true, true).unwrap();
        assert_eq!(n.title, "Ada");
        assert_eq!(n.body, "Plans\nHello there");
        assert_eq!(n.context, "mail:e1@mb1@acc");
        assert_eq!(n.group, "mail:acc");
        assert_eq!(n.id, "mail:acc/e1");
        let ids: Vec<&str> = n.actions.iter().map(|a| a.0.as_str()).collect();
        assert_eq!(ids, ["archive", "mark-read", "delete"]);
        let n = mail_note(&[m("e1", "", "")], "acc", false, false).unwrap();
        assert_eq!(n.title, "New mail");
        assert!(n.body.starts_with("(no subject)"));
        assert_eq!(n.actions[0].0, "mark-read");
    }

    fn view(folder: &str) -> View {
        View { account: "me@x.test".into(), folder: folder.into(), narrowed: false, listed: false,
            folders: vec!["inbox".into(), "arch".into(), "sub".into(), "sent".into()], inbox: Some("inbox".into()) }
    }
    fn r(mailbox: Option<&str>, account: Option<&str>) -> MailRef {
        MailRef { id: "e1".into(), mailbox: mailbox.map(String::from), account: account.map(String::from) }
    }
    fn boxes(b: &[&str]) -> Location { Location::In(b.iter().map(|s| s.to_string()).collect()) }

    #[test]
    fn mail_context_round_trip() {
        assert_eq!(mail_context("e1", "mb", "me@x.test"), "mail:e1@mb@me@x.test");
        assert_eq!(parse_mail_context("mail:e1@mb@me@x.test"), Some(r(Some("mb"), Some("me@x.test"))));
        // a toast from before the folder was carried
        assert_eq!(parse_mail_context("mail:e1"), Some(r(None, None)));
        assert_eq!(mail_context("e1", "", ""), "mail:e1");
        assert_eq!(parse_mail_context("mail:e1@@acc"), Some(r(None, Some("acc"))));
        assert_eq!(parse_mail_context("mail:"), None);
        assert_eq!(parse_mail_context("inbox"), None);
        assert_eq!(parse_mail_context("event:a"), None);
    }

    #[test]
    fn opens_the_mails_own_folder() {
        // viewing another folder: switch to the one the mail is in, then open it
        let p = plan_mail_open(&r(Some("arch"), None), &view("sent"), &boxes(&["arch"]));
        assert_eq!(p, OpenPlan { folder: Some("arch".into()), open: true, notice: None });
        // already there: keep the list, just open
        let p = plan_mail_open(&r(Some("arch"), None), &view("arch"), &boxes(&["arch"]));
        assert_eq!(p, OpenPlan { folder: None, open: true, notice: None });
    }

    #[test]
    fn moved_mail_follows_the_server() {
        // the toast said Inbox, the mail has since been filed to Archive
        let p = plan_mail_open(&r(Some("inbox"), None), &view("inbox"), &boxes(&["arch"]));
        assert_eq!(p.folder.as_deref(), Some("arch"));
        assert!(p.open);
        // in several folders: the one on screen, else the toast's, else the Inbox
        assert_eq!(plan_mail_open(&r(Some("sub"), None), &view("arch"), &boxes(&["sub", "arch"])).folder, None);
        assert_eq!(plan_mail_open(&r(Some("sub"), None), &view("sent"), &boxes(&["arch", "sub"])).folder.as_deref(), Some("sub"));
        assert_eq!(plan_mail_open(&r(None, None), &view("sent"), &boxes(&["arch", "inbox"])).folder.as_deref(), Some("inbox"));
        // only in a folder the tree does not have (a shared account's): the Inbox, as for an old toast
        assert_eq!(plan_mail_open(&r(None, None), &view("sent"), &boxes(&["elsewhere"])).folder.as_deref(), Some("inbox"));
    }

    #[test]
    fn deleted_mail_only_says_so() {
        let p = plan_mail_open(&r(Some("inbox"), None), &view("sent"), &Location::Gone);
        assert_eq!(p, OpenPlan { folder: None, open: false, notice: Some("That message is no longer there.") });
    }

    #[test]
    fn old_toast_and_unknown_server() {
        // no folder in the context, not asked: where it is listed, else the Inbox
        let mut v = view("sent");
        assert_eq!(plan_mail_open(&r(None, None), &v, &Location::Unknown).folder.as_deref(), Some("inbox"));
        v.listed = true;
        assert_eq!(plan_mail_open(&r(None, None), &v, &Location::Unknown).folder, None);
        // the server did not answer: the toast's folder
        assert_eq!(plan_mail_open(&r(Some("arch"), None), &view("sent"), &Location::Unknown).folder.as_deref(), Some("arch"));
        // a folder the tree does not know is not selected
        assert_eq!(plan_mail_open(&r(Some("zzz"), None), &view("inbox"), &Location::Unknown).folder, None);
        // folders not loaded yet: trust the toast
        let mut v = view("");
        v.folders.clear();
        v.inbox = None;
        assert_eq!(plan_mail_open(&r(Some("arch"), None), &v, &Location::Unknown).folder.as_deref(), Some("arch"));
    }

    #[test]
    fn all_inboxes_view_is_left() {
        // the merged view keys rows per source: the mail's own Inbox is selected instead
        let p = plan_mail_open(&r(Some("inbox"), Some("me@x.test")), &view("__unified"), &boxes(&["inbox"]));
        assert_eq!(p, OpenPlan { folder: Some("inbox".into()), open: true, notice: None });
        // the views that are not folders (Starred, All mail)
        assert_eq!(plan_mail_open(&r(Some("inbox"), None), &view(""), &boxes(&["inbox"])).folder.as_deref(), Some("inbox"));
    }

    #[test]
    fn search_or_tag_is_cleared() {
        // right folder, but a search narrows the list: reload it so the mail is in it
        let mut v = view("inbox");
        v.narrowed = true;
        assert_eq!(plan_mail_open(&r(Some("inbox"), None), &v, &boxes(&["inbox"])).folder.as_deref(), Some("inbox"));
        // narrowed but the mail is on screen already: leave the results
        v.listed = true;
        assert_eq!(plan_mail_open(&r(None, None), &v, &Location::Unknown).folder, None);
    }

    #[test]
    fn another_login_opens_the_inbox_only() {
        let p = plan_mail_open(&r(Some("arch"), Some("other@x.test")), &view("sent"), &boxes(&["arch"]));
        assert_eq!(p, OpenPlan { folder: Some("inbox".into()), open: false, notice: None });
        let p = plan_mail_open(&r(Some("arch"), Some("other@x.test")), &view("inbox"), &Location::Unknown);
        assert_eq!(p, OpenPlan { folder: None, open: false, notice: None });
        // the same login: opens
        assert!(plan_mail_open(&r(Some("arch"), Some("me@x.test")), &view("inbox"), &boxes(&["arch"])).open);
    }

    #[test]
    fn inbox_toast_selects_the_inbox() {
        assert_eq!(folder_to_select("inbox", &view("sent")).as_deref(), Some("inbox"));
        assert_eq!(folder_to_select("inbox", &view("inbox")), None);
        let mut v = view("inbox");
        v.narrowed = true;
        assert_eq!(folder_to_select("inbox", &v).as_deref(), Some("inbox"));
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
        assert!(x.starts_with("<toast launch=\"open|mail:e1@mb1@acc\""), "{x}");
        assert!(x.contains("<text>A &amp; B &lt;x&gt;</text><text>Q&quot;s</text><text>Hello there</text>"), "{x}");
        assert!(x.contains("<action content=\"Archive\" arguments=\"archive|mail:e1@mb1@acc\""));
        assert!(x.contains("ms-winsoundevent:Notification.Mail"));
        let silent = toast_xml(&Note { sound: false, ..n });
        assert!(silent.contains("<audio silent=\"true\"/>"));
        let r = reminder_note("ev1", 1000, "Call", "In 10 min", true);
        let x = toast_xml(&r);
        assert!(x.contains("scenario=\"reminder\"") && x.contains("snooze-5|event:ev1"), "{x}");
    }
}

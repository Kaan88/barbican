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
    /// account parts are optional: older toasts carry "mail:<email id>"), "inbox:<account>" (plain
    /// "inbox" on older toasts), "event:<id>". The account is the remembered login (what the
    /// account switcher lists), not the session's own user name.
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

/// The context of a toast for several new messages: "inbox:<account>".
pub fn inbox_context(account: &str) -> String {
    if account.is_empty() { "inbox".into() } else { format!("inbox:{account}") }
}

/// A message a toast points at (or, with an empty `id`, only the Inbox of `account`).
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

/// "inbox" or "inbox:<account>" back into a reference without a message (empty `id`); None for
/// other contexts.
pub fn parse_inbox_context(ctx: &str) -> Option<MailRef> {
    let account = if ctx == "inbox" { None } else { Some(ctx.strip_prefix("inbox:")?).filter(|a| !a.is_empty()).map(String::from) };
    Some(MailRef { id: String::new(), mailbox: None, account })
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
    /// The message open in the reader ("" for none).
    pub current_mail: String,
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

/// Whether the user changed the account, the folder or the open message since `then` (Bulwark
/// applyMailDeepLink: "The user moved on while the login reconnected: don't pull them away").
pub fn moved_on(then: &View, now: &View) -> bool {
    then.account != now.account || then.folder != now.folder || then.current_mail != now.current_mail
}

/// What to do about the login a toast belongs to.
#[derive(Debug, Clone, PartialEq)]
pub enum AccountPlan {
    /// The window is on that login (or the toast names none): open it here.
    Here,
    /// Another signed-in login: switch to it first (Bulwark applyMailDeepLink: switchAccount).
    Switch(String),
    /// A login that is not signed in any more.
    Missing,
}

/// `current` is the login on screen, `signed_in` the logins the switcher offers.
pub fn plan_account(r: &MailRef, current: &str, signed_in: &[String]) -> AccountPlan {
    match r.account.as_deref() {
        None => AccountPlan::Here,
        Some(a) if current.is_empty() || a == current => AccountPlan::Here,
        Some(a) if signed_in.iter().any(|s| s == a) => AccountPlan::Switch(a.to_string()),
        Some(_) => AccountPlan::Missing,
    }
}

/// How long an activation waits for its login's folders after the switch.
pub const PENDING_FOR: std::time::Duration = std::time::Duration::from_secs(90);

/// Whether an activation queued at `queued` for a login switch still applies once `login`'s
/// folders are loaded: only for the login it named, and not long after.
pub fn pending_applies(r: &MailRef, queued: std::time::Instant, now: std::time::Instant, login: &str) -> bool {
    r.account.as_deref() == Some(login) && now.saturating_duration_since(queued) <= PENDING_FOR
}

fn known(v: &View, id: &str) -> bool { v.folders.is_empty() || v.folders.iter().any(|f| f == id) }

/// The folder to select to show `target`: None when it is already shown as it is.
pub fn folder_to_select(target: &str, v: &View) -> Option<String> {
    (target != v.folder || v.narrowed).then(|| target.to_string())
}

/// Which folder, and whether to open the message, when a toast for `r` is activated
/// (Bulwark's showInOwningFolder: the message's own folder is selected, a message that is gone
/// only says so). The login is settled before this (see [`plan_account`]). The All inboxes view
/// is left for the folder: its rows are keyed per source, so the message could not be
/// highlighted there. A search or tag that already lists the message is left alone.
pub fn plan_mail_open(r: &MailRef, v: &View, at: &Location) -> OpenPlan {
    // another login's message id means nothing here: callers switch first, so never open it
    if r.account.as_deref().is_some_and(|a| !v.account.is_empty() && a != v.account) {
        return OpenPlan::default();
    }
    // a toast for several messages: just that login's Inbox
    if r.id.is_empty() {
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
    // search results that list the message already show and highlight it: keep them
    let folder = target.filter(|_| !(v.listed && v.narrowed)).and_then(|t| folder_to_select(t, v));
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
            context: inbox_context(account),
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
        View { account: "me@x.test".into(), folder: folder.into(), narrowed: false, listed: false, current_mail: String::new(),
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
        // also when the server's answer is known: the mail is in the results and highlighted
        let p = plan_mail_open(&r(Some("inbox"), None), &v, &boxes(&["inbox"]));
        assert_eq!(p, OpenPlan { folder: None, open: true, notice: None });
        let p = plan_mail_open(&r(Some("arch"), None), &v, &boxes(&["arch"]));
        assert_eq!(p, OpenPlan { folder: None, open: true, notice: None });
    }

    #[test]
    fn another_login_is_switched_to() {
        let me = "me@x.test";
        let signed = vec![me.to_string(), "other@x.test".to_string()];
        // no login in the toast, or the one on screen: here
        assert_eq!(plan_account(&r(Some("arch"), None), me, &signed), AccountPlan::Here);
        assert_eq!(plan_account(&r(Some("arch"), Some(me)), me, &signed), AccountPlan::Here);
        // nothing on screen to compare with: here
        assert_eq!(plan_account(&r(None, Some(me)), "", &signed), AccountPlan::Here);
        // another signed-in login: switch to it
        assert_eq!(plan_account(&r(Some("arch"), Some("other@x.test")), me, &signed), AccountPlan::Switch("other@x.test".into()));
        // one that was signed out since
        assert_eq!(plan_account(&r(Some("arch"), Some("gone@x.test")), me, &signed), AccountPlan::Missing);
        // never opened through the wrong login
        let p = plan_mail_open(&r(Some("arch"), Some("other@x.test")), &view("sent"), &boxes(&["arch"]));
        assert_eq!(p, OpenPlan::default());
        // the same login: opens
        assert!(plan_mail_open(&r(Some("arch"), Some(me)), &view("inbox"), &boxes(&["arch"])).open);
    }

    #[test]
    fn queued_activation_waits_for_its_login_only() {
        use std::time::{Duration, Instant};
        let t0 = Instant::now();
        let m = r(Some("arch"), Some("other@x.test"));
        assert!(pending_applies(&m, t0, t0 + Duration::from_secs(3), "other@x.test"));
        // another login came up (the switch failed or the user chose another)
        assert!(!pending_applies(&m, t0, t0 + Duration::from_secs(3), "me@x.test"));
        // too late: the user has long moved on
        assert!(!pending_applies(&m, t0, t0 + PENDING_FOR + Duration::from_secs(1), "other@x.test"));
        // a toast without a login never waits
        assert!(!pending_applies(&r(None, None), t0, t0, "other@x.test"));
    }

    #[test]
    fn moved_on_means_account_folder_or_mail() {
        let a = view("inbox");
        assert!(!moved_on(&a, &a.clone()));
        assert!(moved_on(&a, &view("arch")));
        let mut b = a.clone();
        b.current_mail = "e7".into();
        assert!(moved_on(&a, &b));
        let mut c = a.clone();
        c.account = "other@x.test".into();
        assert!(moved_on(&a, &c));
        // a search typed meanwhile or rows loaded do not count
        let mut d = a.clone();
        d.narrowed = true;
        d.listed = true;
        assert!(!moved_on(&a, &d));
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
    fn inbox_context_round_trip() {
        assert_eq!(inbox_context("me@x.test"), "inbox:me@x.test");
        assert_eq!(inbox_context(""), "inbox");
        let a = |s: &str| MailRef { id: String::new(), mailbox: None, account: Some(s.into()) };
        assert_eq!(parse_inbox_context("inbox:me@x.test"), Some(a("me@x.test")));
        // an older toast names no login
        assert_eq!(parse_inbox_context("inbox"), Some(MailRef::default()));
        assert_eq!(parse_inbox_context("inbox:"), Some(MailRef::default()));
        assert_eq!(parse_inbox_context("mail:e1"), None);
        assert_eq!(parse_inbox_context("inboxes"), None);
        assert_eq!(parse_inbox_context("event:a"), None);
        // and a message context is not an Inbox one
        assert_eq!(parse_mail_context("inbox:me@x.test"), None);
    }

    #[test]
    fn inbox_toast_follows_its_login() {
        let me = "me@x.test";
        let signed = vec![me.to_string(), "other@x.test".to_string()];
        let toast = parse_inbox_context(&inbox_context("other@x.test")).unwrap();
        // another signed-in login: switch to it, as for a message
        assert_eq!(plan_account(&toast, me, &signed), AccountPlan::Switch("other@x.test".into()));
        assert_eq!(plan_account(&parse_inbox_context(&inbox_context("gone@x.test")).unwrap(), me, &signed), AccountPlan::Missing);
        assert_eq!(plan_account(&parse_inbox_context("inbox").unwrap(), me, &signed), AccountPlan::Here);
        // a queued one waits for its own login only
        let t0 = std::time::Instant::now();
        assert!(pending_applies(&toast, t0, t0, "other@x.test"));
        assert!(!pending_applies(&toast, t0, t0, me));
        // on that login: the Inbox is selected, nothing is opened
        let mut v = view("sent");
        v.account = "other@x.test".into();
        assert_eq!(plan_mail_open(&toast, &v, &Location::Unknown), OpenPlan { folder: Some("inbox".into()), open: false, notice: None });
        // the Inbox is shown already: nothing to do
        assert_eq!(plan_mail_open(&toast, &{ let mut w = v.clone(); w.folder = "inbox".into(); w }, &Location::Unknown), OpenPlan::default());
        // All inboxes gives way to the login's own Inbox
        let mut u = v.clone();
        u.folder = "__unified".into();
        assert_eq!(plan_mail_open(&toast, &u, &Location::Unknown).folder.as_deref(), Some("inbox"));
        // never through the wrong login
        assert_eq!(plan_mail_open(&toast, &view("sent"), &Location::Unknown), OpenPlan::default());
    }

    #[test]
    fn login_not_user_name() {
        // the toast carries the remembered login even when the principal name differs: it matches
        // what the account switcher offers (the session's own user name would not)
        let n = mail_note(&[m("e1", "Ada", "Plans")], "ann@x.test", true, true).unwrap();
        let signed = vec!["ann@x.test".to_string(), "bo@x.test".to_string()];
        let r = parse_mail_context(&n.context).unwrap();
        assert_eq!(plan_account(&r, "bo@x.test", &signed), AccountPlan::Switch("ann@x.test".into()));
        let principal = mail_note(&[m("e1", "Ada", "Plans")], "ann", true, true).unwrap();
        let r = parse_mail_context(&principal.context).unwrap();
        assert_eq!(plan_account(&r, "bo@x.test", &signed), AccountPlan::Missing);
    }

    #[test]
    fn batch_opens_inbox() {
        let n = mail_note(&[m("e2", "Bo", "Second"), m("e1", "Ada", "First")], "acc", true, true).unwrap();
        assert_eq!(n.title, "2 new messages");
        assert_eq!(n.body, "Bo: Second");
        assert_eq!(n.context, "inbox:acc");
        assert_eq!(parse_inbox_context(&n.context).and_then(|r| r.account).as_deref(), Some("acc"));
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

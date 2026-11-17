# Behaviours: reader, read receipts, authentication results, codes, sender pictures, quick rules

Area: `READER`. Owner milestones: M1 (`auth.rs`, the body view and its CSP, `main.rs` remote-content policy, the mailview header timings), M2 (`reader.rs`, `mdn.rs`, `quick_rules.rs`, `otp.rs`, `avatars.rs`). Lite mode is M1 in Appendix B and is marked for a user decision.

Sources: `src/reader.rs`, `src/mdn.rs`, `src/auth.rs`, `src/otp.rs`, `src/avatars.rs`, `src/quick_rules.rs`, `src/mailview.rs`, `src/main.rs`, `src/lite.rs`. Pins as in `jmap.md`. Bulwark references are to version 1.21.2.

## Reader actions and banners (`reader.rs`) (M2)

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-READ-001 | The reader offers: view source, the List-Unsubscribe banner (RFC 2369 and RFC 8058 one-click), read receipts, forward as attachment, rules from a message, save an attachment to Files, download all attachments as a zip, calendar invitations, attached messages (Email/parse), and the notice for a cut body (`isTruncated`). | reader.rs:1-6 | Bulwark `email-viewer.tsx` | none |
| B-READ-002 | Banners are cleared when a message starts loading and set once it is shown. Menus come from `ctxmenu.rs` (kind `reader`) and the list's More menu (`rx:*`). | reader.rs:8-10 | | none |
| B-READ-003 | Folded header lines are unfolded. Header names match case-insensitively. Repeated headers are all returned. | reader.rs:1221 `headers_unfold` | RFC 5322 | `headers_unfold` |
| B-READ-004 | `List-Unsubscribe` gives an HTTP target and a mailto target. | reader.rs:1229 `unsubscribe_parsing` | RFC 2369 | `unsubscribe_parsing` |
| B-READ-005 | One-click unsubscribe is trusted only when DKIM passes for the list's domain. A pass for another domain does not vouch for the list. | reader.rs:1265 `dkim_pass` | RFC 8058; B-READ-022 | `dkim_gate` |
| B-READ-006 | One-click unsubscribe refuses private and loopback addresses (no server-side request forgery). Public addresses are allowed. | reader.rs:1277 `one_click_post`, `public_ip` | security | `one_click_refuses_private_networks` |
| B-READ-007 | One-click unsubscribe does not follow redirects. A redirect is an error. | reader.rs:1288 `post_form` | security | `one_click_does_not_follow_redirects` |
| B-READ-008 | One-click unsubscribe POSTs the RFC 8058 form body with `application/x-www-form-urlencoded`. | reader.rs:1313 | RFC 8058 | `one_click_posts_the_form_body` |
| B-READ-009 | Subject prefixes (`Re:`, `Fwd:`, `AW:`, `RE[2]:` and chains of them) are stripped for display. | reader.rs:1339 `strip_subject_prefixes` | Bulwark | `names` |
| B-READ-010 | A body that the server cut is reported as truncated (`isTruncated`). The source view is cut at `SOURCE_DISPLAY_LIMIT` (2 MiB). | reader.rs:20, :1354 `displayed_truncated` | RFC 8621 4.1.4 (J-053) | `truncation_and_source_cut` |
| B-READ-011 | Files shown in the reader are built as a folder tree with FileNode shape. | reader.rs:1367 `folder_tree`, `file_node` | Stalwart FileNode (J-075) | `files_tree_and_nodes` |
| B-READ-012 | A `message/rfc822` part, or an attachment named `.eml`, is a message attachment and is parsed (Email/parse). | reader.rs:1382 `is_message_attachment` | RFC 8621 4.9 (J-058) | `message_attachments` |
| B-READ-013 | A Blob/copy request is built only when source and target accounts differ. | reader.rs:1254 `blob_copy_request` | RFC 9404 | `blob_copy_between_accounts` |

## Read receipts (`mdn.rs`) (M2)

Bulwark mapping: `lib/mdn.ts` and the `readReceiptResponse` setting. RFC 8098.

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-READ-015 | Stalwart has no `urn:ietf:params:jmap:mdn` capability, so the client builds the `multipart/report` itself, uploads it, imports it into Sent (`Email/import`), submits it (`EmailSubmission/set` with an explicit envelope), then sets `$MDNSent` on the original. | mdn.rs:1-4 | Stalwart (no MDN capability, J-068) | `builds_two_part_report` |
| B-READ-016 | A receipt is sent automatically only when the requester is the return path (test `auto_send_only_to_the_return_path`). A third party's request is asked about. | mdn.rs:261 `auto_send_ok` | Bulwark `readReceiptResponse` | `auto_send_only_to_the_return_path` |
| B-READ-017 | Receipt headers are built from fixed fields (Date, From, Message-ID, In-Reply-To). Content of the message cannot inject Bcc or X- headers. | mdn.rs:276-298 | security (header injection) | `builds_two_part_report`, `header_injection_is_neutralised` |
| B-READ-018 | Base64 bodies in the receipt wrap at 76 characters per line. | mdn.rs:306 | RFC 2045 | `base64_wraps_at_76` |
| B-READ-019 | The requester is the first address in `Disposition-Notification-To`. | mdn.rs:251 `dnt_addresses`, :315 `requested_by` | RFC 8098 | `dnt_addresses`, `requester_and_offer_rules` |
| B-READ-020 | The identity used for a receipt follows the recipient: the identity whose address is in To or Cc. | mdn.rs:328 `pick_identity` | Bulwark | `identity_follows_the_recipient` |
| B-READ-021 | Whether a receipt is offered follows the `readReceiptResponse` setting (ask, always, never). | mdn.rs:313 | Bulwark | `requester_and_offer_rules` |

## Authentication results (`auth.rs`) (M1)

Bulwark mapping: `lib/email-headers.ts`. RFC 8601 (Authentication-Results). DMARC relaxed alignment, with no public suffix list.

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-READ-022 | Only a pass that aligns with the From domain says anything about the visible sender. A pass for someone else's domain says nothing. | auth.rs:1-3, :93 `aligned_pass` | RFC 7489 (relaxed) | `parses_and_aligns` |
| B-READ-023 | A DKIM pass aligns when `header.d` is the From domain or a child of it. | auth.rs:122 `aligned` | RFC 7489 | `parses_and_aligns`, `domains` |
| B-READ-024 | An SPF pass counts only when `smtp.mailfrom` aligns with the From domain. | auth.rs:104 `spf_needs_aligned_mailfrom` | RFC 7489 | `spf_needs_aligned_mailfrom` |
| B-READ-025 | DMARC fail, and SPF softfail with no aligned DKIM pass, are failures. `dkim=none` is not a failure. | auth.rs:111 `failure` | | `failures` |
| B-READ-026 | `domain_of` lower-cases and strips the trailing dot. A sender without `@` has no domain. | auth.rs:119 | | `domains` |

## One-time codes (`otp.rs`) (M2)

Bulwark mapping: `lib/verification-code.ts`. A dependency-free port.

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-READ-027 | A token is a code only when it looks like one and sits near a word that names a code (code, Bestätigungscode, PIN, OTP and similar). The search looks 60 characters before and 120 after (`MAX_BEFORE`, `MAX_AFTER`). | otp.rs:1-4, :6-7 | Bulwark | `finds_codes` |
| B-READ-028 | Doubtful tokens are left out: a wrong chip is worse than none. | otp.rs:1-4 | | `skips_non_codes` |
| B-READ-029 | Promo codes (words such as off, save, discount, coupon), order and tracking numbers, and labelled numbers (nr, tel, fax, phone, Zeile) are not codes. | otp.rs:9, :29, :31 | | `skips_non_codes` |
| B-READ-030 | Code words in German, English, CJK and the other listed languages are recognised. OTP phrases (one-time password) count. | otp.rs:18-26 | | `finds_codes` |

## Sender pictures (`avatars.rs`) (M2)

Bulwark mapping: `components/ui/avatar.tsx`, `app/api/favicon`, `app/api/bimi`. Off by default: these reach outside hosts.

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-READ-031 | Order: a BIMI logo (only for mail that passed DMARC for its From domain), then a Gravatar photo, then the company favicon, then initials. | avatars.rs:1-3, :392 `candidate_order` | Bulwark | `candidate_order_follows_bulwark` |
| B-READ-032 | Off by default. The settings are `senderFavicons` (favicons and BIMI) and `gravatarAvatars`. Barbican asks first; Bulwark has them on. | avatars.rs:4-6 | Bulwark defaults differ (deviation D- to be recorded) | `junk_and_off_show_nothing` |
| B-READ-033 | In Junk no picture is shown unless `showAvatarsInJunk` is on. A logo lends a phishing mail credibility. | avatars.rs:6-7 | | `junk_and_off_show_nothing` |
| B-READ-034 | Answers are kept on disk in `avatars/` in the config folder: positive answers for 14 days, negative for 1 day. They are also kept in memory for the session. | avatars.rs:9-10, :19-20 | Bulwark favicon cache TTLs | none |
| B-READ-035 | The Gravatar hash is the SHA-256 of the trimmed, lower-cased address (64 hex characters). | avatars.rs:415 `gravatar_hash` | Gravatar | `gravatar_hash_is_sha256_of_the_normalised_address` |
| B-READ-036 | The root domain is the registrable domain, including multi-part suffixes (`example.co.uk`). A host is reduced to it before a favicon lookup. | avatars.rs:383 `root_domain`, :59 `MULTI_PART_TLDS`, :76 `PERSONAL_DOMAINS` | | `root_domains_and_validity` |
| B-READ-037 | A BIMI logo URL must be `https:`. A DMARC pass domain is read from Authentication-Results. | avatars.rs:421 `bimi_logo_url`, `dmarc_pass_domain` | BIMI | `bimi_records_and_dmarc` |
| B-READ-038 | Pictures are rasterised to a 64 px square (`EDGE`); bytes over 256 KiB are refused (`MAX_BYTES`); four workers run (`WORKERS`). A one-pixel image is not a raster. | avatars.rs:22-24, :456 | | `rasters_fit_a_square_and_reject_pixels` |
| B-READ-039 | An idle worker counts itself out under the queue lock, so the count is exact when the queue is empty. | avatars.rs:446 | | `idle_worker_is_counted_out_under_the_queue_lock` |

## Body view, CSP and remote content (`mailview.rs`, `main.rs`) (M1)

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-READ-040 | Each message is a sandboxed srcdoc frame with its own CSP (the per-message image policy). | mailview.rs:1-3 | | none |
| B-READ-041 | When remote content is blocked, the CSP allows inline styles (`style-src 'unsafe-inline'`), `data:` fonts, and no scripts (`script-src 'none'`). Stylesheets and fonts of the message are included. | main.rs:106 (test name `blocked_remote_content_includes_stylesheets_and_fonts`); mailview.rs CSP | | `blocked_remote_content_includes_stylesheets_and_fonts` |
| B-READ-042 | While a native overlay (menu, dialog, settings) covers the body, the body's child window is hidden and a capture of it is shown in its place. | mailview.rs:7-9 | feel (WebView airspace, PORT-PLAN S2) | none |
| B-READ-043 | Keyboard shortcuts pressed inside the body are forwarded to the app as key strings, such as `c|k` (Control+k) and `s|?` (Shift+?). | mailview.rs:312 `forwarded_keys_parse` | | `forwarded_keys_parse` |
| B-READ-044 | The body's CSP contains `script-src 'none'` in the blocked-remote case (test assertion). | mailview.rs CSP tests (108-109) | | `blocked_remote_content_includes_stylesheets_and_fonts` |

## Lite mode (`lite.rs`) (decision pending)

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-READ-045 | Lite (plain-text reading) renders the email HTML at 600 px wide, with height above 900 px for a long message. | lite.rs:64 | | `renders_email_html` |
| B-READ-046 | PORT-PLAN Appendix A says lite (`lite.rs`) is dropped, with "plain-text mode kept". The user must confirm this. Recorded as a D- item when decided. | PORT-PLAN Appendix A, B (M1 "lite 1 (marked dropped)") | | `renders_email_html` (status: dropped? pending decision) |

## Quick rules (`quick_rules.rs`) (M2)

Bulwark mapping: `lib/filters/quick-rules.ts`, `rules-menu.tsx`.

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-READ-047 | Rules from a message: "Always move messages from X to …" (sender, domain, or the list), "Always mark as read", "Always tag with", and "Block sender". | quick_rules.rs:1-3 | Bulwark | `presets_build_bulwark_rules` |
| B-READ-048 | A new rule goes at the top of the Sieve metadata, or is merged into an existing one-condition rule that already does the same. One-click rules do not pile up as near duplicates. | quick_rules.rs:3-5, :323 `apply` | | `apply_creates_merges_or_covers` |
| B-READ-049 | A quick rule is refused when the Sieve script was edited by hand. It is never overwritten silently. | quick_rules.rs:254 | | `hand_edited_script_is_refused` |
| B-READ-050 | Fileinto paths for quick-rule folders are `INBOX/News` for a folder under Inbox and `Archive` for the archive role. | quick_rules.rs:289 `sieve_path` | | `sieve_paths` |
| B-READ-051 | A preset builds a Bulwark-shaped rule: name `Move from X to Y`, a `domain_is` condition on `from`. | quick_rules.rs:298 | Bulwark | `presets_build_bulwark_rules` |
| B-READ-052 | Candidate senders (addresses, and the domain) are read from the message. | quick_rules.rs:275 `subject` | | `subject_from_a_message` |

## Open questions

- B-READ-010 and gap J-053: `displayed_truncated` exists and is tested, but the compliance doc says a truncated body is shown with no notice. Check which is current before porting.
- B-READ-032: Bulwark has sender pictures on, Barbican asks first. This is a deliberate deviation and needs an entry in `spec/decisions.md`.
- B-READ-046: lite mode is dropped or kept, by user decision.
- B-READ-044 is a restatement of the CSP test. The port should add its own test of the frame's CSP header.
- `reader.rs:1168` holds a hard-coded test-account address for the live checks. It must not be copied into the port or the public repository. The port takes the test account from `.local/accounts.md` through configuration.

# Behaviours: OAuth / OIDC sign-in and remembered sessions (`src/oauth.rs`)

Area: `OAUTH`. Owner milestone: M1 (sign-in, refresh, remembered accounts), M8 (revoke on sign-out
polish). Added at the M0 review (stream m0-review): the catalogue streams had no row for this module.

Pins (per `spec/README.md`): Rust source at `rust-final` (`bba9613`); Bulwark 1.21.2; Stalwart 0.16.27.
RFCs: 6749 (OAuth 2.0), 7636 (PKCE), 8252 (native apps, loopback redirect), 8414 (AS metadata),
7591 (dynamic registration), 7009 (revocation). Rust tests: T-192..T-198.

The credential storage format these rows rely on is described in `docs/adr/0008-credential-migration.md`
(spike S5). The C# app stores under its own names; it reads the Rust entries only through the explicit
one-time import.

| ID | Rule | Source | Reference | Tests |
|---|---|---|---|---|
| B-OAUTH-001 | Flow: authorization code with PKCE (S256), loopback redirect `http://127.0.0.1:<port>/callback`, refresh tokens. | oauth.rs:1-4, :164 `sign_in` | RFC 6749, 7636, 8252 | none (live) |
| B-OAUTH-002 | Discovery order: the server's own `/.well-known/oauth-authorization-server`, then `/.well-known/openid-configuration`; when an account name is given, Stalwart's `/api/discover/<account>` (0.16+) wins if it names an authorization endpoint. Neither found: "This server does not offer OAuth sign-in." | oauth.rs:87 `discover` | RFC 8414; Stalwart `/api/discover` | none |
| B-OAUTH-003 | An endpoint counts as the server's own when it is relative, has the base URL's origin, or equals the own metadata's endpoint; otherwise it is an external SSO provider (`external = true`) and its endpoints are used as given. | oauth.rs:101-105 | | none |
| B-OAUTH-004 | Own-server endpoints are rebased onto the origin the user typed (Stalwart may advertise its configured hostname); path and query are kept. | oauth.rs:50 `rebase` | | none |
| B-OAUTH-005 | Scope: `openid email profile offline_access`, filtered to what `scopes_supported` lists, plus every `urn:ietf:params:oauth:scope:*` data scope offered. Without `scopes_supported`, the four defaults are sent. | oauth.rs:13-14, :61 `pick_scope` | | none |
| B-OAUTH-006 | HTTP for OAuth: 15 s timeout, redirects not followed. | oauth.rs:38 `http` | | none |
| B-OAUTH-007 | Client id: dynamic registration (RFC 7591) when the metadata offers a registration endpoint, as a public native client (`token_endpoint_auth_method: none`, `application_type: native`, redirect URIs incl. port 8791 and portless loopback); the id is cached per registration endpoint in `oauth-clients.json`. Registration failure falls back to the configured client id. | oauth.rs:127-157 `client_id` | RFC 7591, RFC 8252 7.3 | none |
| B-OAUTH-008 | The loopback listener binds 127.0.0.1:8791 first (so a pre-registered client can list an exact redirect URI), else an ephemeral port. | oauth.rs:166-169 | RFC 8252 7.3 | none |
| B-OAUTH-009 | Authorization request carries `state` (16 random bytes, base64url), the PKCE challenge of a 48-byte verifier, the scope when non-empty and `login_hint` when an account was typed. The URL opens in the system browser. | oauth.rs:171-185 | RFC 7636 4 | none |
| B-OAUTH-010 | The callback wait is bounded (5 minutes: "Sign-in timed out."). Requests to other paths get 404 and the wait continues. The browser tab gets a small "Signed in" / "Sign-in failed" page. | oauth.rs:187-211 | | none |
| B-OAUTH-011 | An `error` on the callback ends the sign-in with the provider's error and description. A `state` mismatch ends it ("Sign-in state mismatch; try again.") before the code is redeemed. | oauth.rs:212-224 | RFC 6749 4.1.2.1, 10.12 | none |
| B-OAUTH-012 | Token responses without `access_token` or with a non-2xx status are errors that start "Token request failed (<status>)". A refresh response without a new refresh token keeps the old one. Expiry is `expires_in` minus 30 s. | oauth.rs:235-249 `parse_tokens` | RFC 6749 5.1, 6 | T-198 (prefix used by rejection detection) |
| B-OAUTH-013 | Refresh decision: no rejection (network, 5xx) is transient and keeps everything; a rejection with a different non-empty stored token retries once with it (another instance rotated it); otherwise the account needs signing in again. | oauth.rs:262 `decide` | | T-192..T-197 |
| B-OAUTH-014 | A rejection is a 4xx token error naming `invalid_grant`, `invalid_client` or `unauthorized_client`; timeouts and 5xx are not rejections. | oauth.rs:273 `is_rejection` | RFC 6749 5.2 | T-198 |
| B-OAUTH-015 | Refresh runs under a cross-process lock file (`oauth-refresh.lock` in the config dir; waits up to 15 s, a lock older than 60 s is stale). It adopts the stored refresh token first, writes a rotated token back to both the session entry and the accounts list, and never deletes credentials. | oauth.rs:277-359 | Stalwart rotates refresh tokens | T-192..T-197 (decision part only) |
| B-OAUTH-016 | "Session expired" is the error prefix for an account that needs signing in again; the account is kept, never deleted. | oauth.rs:270-271, :341, :354 | | none |
| B-OAUTH-017 | Sign-out revokes refresh and access tokens best-effort (RFC 7009) when the server's metadata has a revocation endpoint; failures are ignored. | oauth.rs:362 `revoke` | RFC 7009 | none |
| B-OAUTH-018 | Remembered session: one `session` entry (the active one) and an `accounts` list (most recent first, one per user+server) in the OS credential store. Kinds: `oauth` (refresh token, token endpoint, client id) and `password`. Forgetting an account removes it from the list only. | oauth.rs:373-411 | ADR 0008 | none |

## Open questions
- B-OAUTH-011: an `error` callback aborts before the `state` check, so any local process can cancel a
  pending sign-in (denial of service only, no token leak). Bulwark's web flow does not have this
  shape. The C# port should check `state` first. Logged in `docs/FABLE-REVIEW.md` (M0 review).
- B-OAUTH-007: the dynamic-registration cache is a plain JSON file keyed by registration endpoint;
  the C# port may keep it in settings storage. No behaviour change intended.

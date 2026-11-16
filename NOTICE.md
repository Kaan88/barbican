# Notice

Barbican is distributed under the GNU Affero General Public License, version 3 only
(SPDX: `AGPL-3.0-only`). The full text is in [LICENSE](LICENSE).

## Material derived from Bulwark Webmail
Barbican reimplements the behaviour of Bulwark Webmail (version 1.21.2) and stays compatible with
its data. Parts are derived from Bulwark's source, among them:

- `src/Barbican.Core/Settings/settings-schema.json`: extracted from Bulwark's settings pages and
  settings store (Bulwark's settings keys, labels and descriptions);
- `src/Barbican.App/Assets/mail/email_dark.js`: a port of Bulwark `lib/email-dark-mode.ts`;
- behaviours, formats and rules ported from Bulwark files cited by file and line in the code and in
  `spec/`.

Bulwark Webmail is Copyright (C) 2025-2026 Bulwark Project Authors and licensed under the GNU
Affero General Public License, version 3 only (with an additional app-store permission that
Barbican does not use). Its fork notice, preserved here as Bulwark asks:

> This repository is distributed under the GNU Affero General Public License,
> version 3 only. It also includes material derived from earlier MIT-licensed
> work in the fork lineage.
>
> Fork lineage:
>
> - https://github.com/root-fr/jmap-webmail
> - https://github.com/bulwarkmail/webmail
>
> Original MIT attribution notice preserved from the earlier forked codebase:
>
> MIT License
>
> Copyright (c) 2025 Matthieu MALVACHE
>
> Permission is hereby granted, free of charge, to any person obtaining a copy
> of this software and associated documentation files (the "Software"), to deal
> in the Software without restriction, including without limitation the rights
> to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all
> copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
> SOFTWARE.

## Stalwart
Barbican talks to Stalwart Mail Server and follows its source for server behaviour; no Stalwart
code is included.

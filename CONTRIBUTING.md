<!-- SPDX-License-Identifier: GPL-3.0-or-later -->
# Contributing to CollectiveWiki

Thanks for your interest in improving CollectiveWiki.

## Getting started

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download).
2. Fork and clone the repository.
3. Build and test: `dotnet test CollectiveWiki.slnx -c Release`.

All `Collective.*` dependencies are vendored in `build/packages/`; no private feed is required (only nuget.org, for Markdig and the test SDKs).

## Project layout

- `src/Wiki.Core` — parsing, indexing, search, vault, editor model, sync core. **No UI framework.**
- `src/Wiki.Storage` — the SQLite/FTS5 persistent index.
- `src/Wiki.Editor` — the Avalonia live-preview editor control.
- `src/Wiki.Desktop` — the Avalonia desktop application.
- `src/Wiki.Sync`, `src/Wiki.Sync.Transport`, `src/Wiki.Sync.Host` — peer-to-peer sync core, transport, and host.
- `tests/` — one test project per source project.

## Conventions

- **License header:** every source file starts with `SPDX-License-Identifier: GPL-3.0-or-later` (line comment for `.cs`, XML comment for `.axaml`/`.csproj`).
- **Tests come with changes.** `Wiki.Core` logic is unit-tested against the `corpus/` fixture vault — keep the existing suites green.
- **`Wiki.Core` stays UI-free** — it must not reference Avalonia or any UI framework.
- **One commit per logical change**, with a clear message.

## Pull requests

- Keep each PR focused on a single concern.
- Ensure `dotnet test CollectiveWiki.slnx -c Release` passes before opening.
- Describe what changed and why; link any related issue.

## Security

Please do **not** open a public issue for security vulnerabilities — see [SECURITY.md](SECURITY.md).

## Licensing of contributions

CollectiveWiki is licensed under the GNU General Public License, version 3 or later
(`GPL-3.0-or-later`). By opening a pull request you agree that your contribution is
licensed under those same terms (inbound = outbound).

If the project later ships a separate commercial build (for example, an ad-supported
mobile edition), that build is a distinct proprietary product and will **not**
incorporate externally-contributed GPL code unless the contributor has signed a
Contributor License Agreement granting the necessary rights. No CLA is required today —
contributions are accepted purely under `GPL-3.0-or-later`. If and when a CLA becomes
necessary, it will be requested explicitly on the pull request.

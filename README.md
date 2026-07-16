<!-- SPDX-License-Identifier: GPL-3.0-or-later -->
# CollectiveWiki

**A local-first, no-account Markdown knowledge base** — linked, searchable notes over a folder of plain `.md` files you own.

CollectiveWiki turns a directory of Markdown into a second brain: `[[wikilinks]]` with automatic backlinks, tags, full-text search, a link graph, daily notes, templates, and image/note embeds — all edited in a live-preview WYSIWYG editor. Your notes stay portable, git-friendly plain text on your own disk. No account, no cloud, no lock-in. Optionally, sync a vault directly between your own devices peer-to-peer, with no server in the middle. Your vaults stay compatible with Obsidian — open a folder you already use and its `[[wikilinks]]`, `![[embeds]]`, `#tags`, and callouts work as-is.

Part of the [Collective Software](https://github.com/CollectiveSoftware-Public) suite. Free software under the GNU GPL v3 (or later).

## Features

- **Live-preview editor** — a single editable surface where Markdown renders in place; formatting markers reveal only on the line your caret is on.
- **Wikilinks & backlinks** — `[[note]]` links with an automatic backlinks panel and unlinked-mention discovery.
- **Tags** — `#nested/tags`, a tag tree, tag autocomplete, and vault-wide tag rename.
- **Full-text search** — a persistent SQLite/FTS5 index with `tag:`, `path:`, and `"exact phrase"` operators.
- **Link graph** — an interactive, pannable/zoomable graph of the whole vault.
- **Daily notes, templates, transclusion** — a journal workflow, template insertion, and `![[note#section]]` embeds.
- **Image embeds** — `![[image.png]]` renders inline; click to open it full size.
- **Properties** — YAML frontmatter shown as a typed, editable Properties card.
- **Callouts, footnotes, code highlighting, slash commands** — `> [!note]` callouts, `[^footnotes]`, fenced-code syntax highlighting, and a `/` command menu.
- **Tabs, split view, outline, bookmarks** — document tabs, a two-pane split, a live outline rail, and bookmarks.
- **HTML export** — export a single note or the whole vault as a self-contained static site.
- **Peer-to-peer sync (optional)** — end-to-end-authenticated vault-to-vault sync across your own devices over your LAN or a self-hosted relay. No third-party server, ever.

## Download

Grab the latest build from [Releases](https://github.com/CollectiveSoftware-Public/CollectiveWiki/releases/latest):

| Platform | File |
|---|---|
| Windows (x64) | `CollectiveWiki-<version>-win-x64.exe` |
| Linux (x64) | `CollectiveWiki-<version>-linux-x64` |

Both are self-contained single files — no .NET runtime required. On Linux, mark it executable:
`chmod +x CollectiveWiki-<version>-linux-x64`.

### A note on unsigned builds

CollectiveWiki is **not code-signed**, so:

- **Windows SmartScreen will warn you** on first run ("Windows protected your PC" → *More info* → *Run anyway*).
- **Some antivirus may flag it.** Code-signing certificates require a verified legal identity, which
  this project does not have. We would rather ship unsigned and tell you plainly than not ship.

Because we can't rely on the OS to vouch for the binary, every release is **cryptographically signed by
the project instead** — and you can check it yourself.

### Verify your download

Every release ships `manifest.json` (the version plus a SHA-256 for each binary) and `manifest.json.sig`
(an ECDSA P-256 signature over that manifest). Verifying proves the binary matches what the keyholder
signed and wasn't altered in transit.

This project's public signing key:

```
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAETQrOGlaKuD6QzGtUzI4E/56mEn3Dd98qXYFgRBKds+DuDYFIsbNywlsFJdzylJ7a0Ef0sXPk2srXp08A7HNqag==
```

```sh
# 1. Get the verifier. It lives in this repo — a shallow clone is the simplest way to get it
#    and its two library files in the layout it expects.
git clone --depth 1 https://github.com/CollectiveSoftware-Public/CollectiveWiki.git
# 2. Download the release you want to check.
gh release download v1.0.0 -R CollectiveSoftware-Public/CollectiveWiki --dir cw
# 3. Verify.
pwsh ./CollectiveWiki/build/verify-release.ps1 -Dir cw -PublicKeyBase64 "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAETQrOGlaKuD6QzGtUzI4E/56mEn3Dd98qXYFgRBKds+DuDYFIsbNywlsFJdzylJ7a0Ef0sXPk2srXp08A7HNqag=="
```

A genuine release prints `Release verified`. Anything else — **don't run the binary**.

The verifier checks three things: the manifest carries a valid signature from the key above; every
binary you downloaded hashes to exactly what that signed manifest says; and **no extra file is present
that the manifest doesn't declare** — the signature vouches only for the files it lists.

Note the verifier and the public key both come from this repo, so verification protects your *download*
(a swapped asset, a hostile mirror, a corrupted transfer) rather than protecting you from this repo
itself. That's the honest boundary.

The signing key is held offline and never touches CI. So as long as you have the project's genuine key,
a hostile mirror or a swapped release asset cannot produce binaries that verify against it — forging those
would need the key itself, which never leaves the maintainer's machine. The catch is *getting* the genuine
key: the copy above lives in this repo, so an attacker who could rewrite this repo could also swap the key.
Verification protects your download against tampering in transit; it is not a defense against this repo
itself being compromised (see the boundary above).

To be precise about what this does *not* cover: the binaries are built by GitHub Actions, so a
compromise of this repository or of the build pipeline could produce a *genuinely signed* release.
Signing withholds release authority from CI; it does not prove the source that CI compiled. What you can
check for yourself is that each release is built from its tag by the workflow committed at that tag
(`.github/workflows/release.yml`), and that the source it built is the source you can read here. We don't
yet offer reproducible builds, which is what it would take to prove the binary matches that source.

## Build from source

CollectiveWiki targets **.NET 10** and builds entirely offline — every `Collective.*` dependency is vendored in `build/packages/`, so no private package feed is required (only nuget.org, for Markdig and the test SDKs).

Prerequisites: the [.NET 10 SDK](https://dotnet.microsoft.com/download).

    git clone https://github.com/CollectiveSoftware-Public/CollectiveWiki.git
    cd CollectiveWiki

    # Build and run the full test suite
    dotnet test CollectiveWiki.slnx -c Release

    # Run the desktop app
    dotnet run --project src/Wiki.Desktop -c Release

To produce a single-file desktop build, see `build/publish-desktop.ps1`.

## Platform support

The desktop app runs on **Windows, Linux, and macOS** (Avalonia). Optional peer-to-peer sync currently seals its device keys with the OS keystore on **Windows** (DPAPI); at-rest key storage for Linux/macOS and transit encryption on those platforms are planned successors.

## License

CollectiveWiki is licensed under the **GNU General Public License, version 3 or later** (`GPL-3.0-or-later`). See [LICENSE](LICENSE).

This repository vendors several `Collective.*` binary packages (in `build/packages/`) that are part of the same Collective Software suite and are themselves licensed under GPL-3.0-or-later: `Collective.Platform` (and its `.Abstractions`, `.Controls`, `.Secrets`, `.Testing` companions), `Collective.Code.Core`, `Collective.Code.Syntax`, `Collective.Diff.Core`, and `Collective.Docs.Controls`. Their complete corresponding source is being published under the [CollectiveSoftware](https://github.com/CollectiveSoftware-Public) organization, with `Collective.Platform` prioritized. Until each package's source lands there, the corresponding source is available on request through the contact in [SECURITY.md](SECURITY.md).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Bug reports and feature requests are welcome via GitHub Issues; please report security vulnerabilities privately per [SECURITY.md](SECURITY.md).

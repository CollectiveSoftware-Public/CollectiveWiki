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

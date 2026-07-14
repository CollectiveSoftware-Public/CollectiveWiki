<!-- SPDX-License-Identifier: GPL-3.0-or-later -->
# Security Policy

## Reporting a vulnerability

Please report security vulnerabilities **privately** — do not open a public issue.

Use GitHub's private vulnerability reporting: on this repository, open the **Security** tab and choose **Report a vulnerability**. This creates a private advisory visible only to the maintainers.

We aim to acknowledge reports within a few days and will keep you updated as we investigate and prepare a fix. Please allow a reasonable window for a fix to ship before any public disclosure.

## Scope

CollectiveWiki is a local-first application: your notes live on your own disk, and optional peer-to-peer sync is authenticated end-to-end with no third-party server. Reports of particular interest:

- weaknesses in the peer-to-peer sync authentication, pairing, or content-key handling;
- at-rest handling of device keys and secrets;
- parsing or import paths that a malicious vault or note could abuse.

## Corresponding source

To request the corresponding source of any vendored `Collective.*` GPL package not yet published under the [CollectiveSoftware](https://github.com/CollectiveSoftware-Public) organization, use the same private advisory channel.

## Supported versions

CollectiveWiki is in active development; security fixes target the latest `main`.

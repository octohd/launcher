# Security policy

## Supported versions

Security fixes are provided for the latest stable OctoHD release. Update to the newest release before reporting an issue that may already be resolved.

## Reporting a vulnerability

Do not open a public issue for a suspected vulnerability. Email **octohd@proton.me** with:

- the affected OctoHD version and operating system;
- a clear description of the issue and its impact;
- minimal reproduction steps or a proof of concept;
- any suggested mitigation, if known.

Do not send private keys, access tokens, copyrighted game files, full patch archives, or unrelated personal data. Redact usernames and local paths from screenshots and logs.

Reports will be reviewed privately. Once a fix is available, the issue may be disclosed in release notes with credit if the reporter requests it.

## Security scope

High-impact areas include patch-source validation, download integrity, filesystem path handling, self-update verification, and process launching. Problems in third-party patch files, game clients, or external hosting services should be reported to their respective operators unless OctoHD itself creates the vulnerability.

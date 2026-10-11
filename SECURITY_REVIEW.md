# Pre-publication security review

Date: 11 October 2026. Scope: the source and documentation prepared for the first public Glasscape repository and version 2.1.0 release.

## Checks

- Gitleaks 8.30.1 scans of the exact staged publication snapshot, using the official release binary verified against its published SHA-256 checksums. Reports redact potential secret values. No API key, access token or private credential match was found in the reviewed source.
- Manual review of media process arguments, startup registration, package identity, installer/archive paths, release file selection and data-handling statements.
- .NET package vulnerability query; no vulnerable package reference was reported for the application project.
- Native payload provenance and architecture/import checks. ExplorerTAP and mpv runtime files are pinned to published hashes.
- License/provenance review with explicit MIT/GPL scope and retained third-party notices.

## Changes made before publication

The two archive producers previously had inconsistent private-file filters, and persistent release staging could retain unrelated files. Both now use one shared policy for private filenames and excluded media. Release traversal rejects links/junctions and validates every staged descendant before copies, cleanup, archive creation and packaging. Tests cover environment files, credentials/secrets JSON, signing keys, logs, local settings, excluded media in nested paths and reparse points.

The portable installer also checks source and destination trees/ancestors before copying and repeats safety checks before writes. Public Git exclusions keep credentials, internal work records, binary build results and non-distributed media out of the repository.

Media filenames are passed as literal process arguments after `--`; mpv user configuration and scripts are disabled. Packaged startup uses the asynchronous Windows startup-task API and keeps Windows as the authority for enabled/disabled/policy states.

## Limits

Secret scanning and code review do not prove the absence of every vulnerability. This review is not a binary audit of upstream libraries. The documented uncertainty about exact sources for some upstream mpv DLL dependencies remains in [the mpv notice](third_party/mpv/NOTICE.md).

The unsigned Store upload bundle keeps the actual Store publisher identity. No certificate trust, security setting or administrator-installed testing identity was changed. Installed Store-package native integration and startup policy behavior require testing an installable signed package. Visible desktop test runs are opt-in; routine localization checks and Store image rendering do not show app windows.

Report new concerns privately using [SECURITY.md](SECURITY.md).

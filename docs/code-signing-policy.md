# Code signing policy

## Status

Sentory is applying to the SignPath Foundation open-source code-signing program.
Release 2.0.4 and earlier releases are unsigned. This page will be updated when the
application has been approved and the signing pipeline has been activated.

**Free code signing provided by SignPath.io, certificate by SignPath Foundation.**

Only release files that carry a valid Authenticode signature issued in the name of
SignPath Foundation are covered by this policy. An unsigned file must not be presented
as an official signed Sentory release.

## Project and team roles

- Source repository: [NudeNyang/Sentory](https://github.com/NudeNyang/Sentory)
- Committers and reviewers: [NudeNyang](https://github.com/NudeNyang)
- Signing request approvers: [NudeNyang](https://github.com/NudeNyang)

All project members with source or signing access must use multi-factor authentication
for GitHub and SignPath. Changes from contributors who do not have direct commit access
must be reviewed before they are merged.

## Release and signing requirements

Official signed releases must meet all of the following requirements:

1. The source is a commit in the public `NudeNyang/Sentory` repository.
2. Release binaries are built on GitHub-hosted GitHub Actions runners.
3. SignPath verifies the GitHub build origin before accepting the signing request.
4. A designated approver manually approves every release signing request.
5. Product and version metadata match the release tag and the public project name.
6. Sentory-owned executables and installers are signed with Authenticode. Third-party
   components are not re-signed with the Sentory signing policy.
7. SHA-256 files and the release manifest are generated from the final signed files.
8. Signed files are not modified after signing.

The signing key is generated and stored in SignPath's hardware security module. It is
not exported to the repository or to GitHub Actions.

## Privacy and network behavior

Sentory does not upload saved messenger content to a Sentory-operated server and does
not include analytics or advertising trackers. Link previews can contact the website
behind a saved link. Update checks contact GitHub Releases. Sync is performed only when
the user configures a cloud folder or WebDAV destination. OCR runs locally on Windows.

See [Privacy and local data](./privacy.md) for the complete description, including data
locations, network requests, retention, deletion, and user controls.

## Verification

On Windows, users can inspect a signed file through **Properties → Digital Signatures**
or verify it with the Windows SDK `signtool`:

```powershell
signtool verify /pa /all /v .\Sentory.exe
```

The signature must be valid and identify SignPath Foundation as the signer. Users
should download files only from the project's official
[GitHub Releases](https://github.com/NudeNyang/Sentory/releases) page.

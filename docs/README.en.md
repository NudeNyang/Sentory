# Sentory

[한국어](../README.md) | [English](./README.en.md)

> Keep the links and images you paste in one place

Sentory is a desktop app for keeping links and images from your messenger chats in a
separate, searchable library. Instead of scrolling through an old conversation, you
can find an item, open the original, or copy it back to the clipboard.

Discord, Slack, WhatsApp, Telegram, KakaoTalk, LINE, and WeChat are currently supported. Sentory is not a system-wide clipboard
logger. It checks the chat input area of a supported messenger and saves only the
content that meets that messenger's detection rules. Support for more messengers is
planned.

## Supported messengers

| Messenger | When Sentory saves | What Sentory ignores |
| --- | --- | --- |
| Discord | After a pasted link or image is actually sent | Paste without sending, `Shift+Enter`, canceled attachments |
| Slack | After a pasted or Explorer-dropped link or image is actually sent from the desktop app | Paste or drop without sending, canceled attachments, other input fields |
| WhatsApp | After a pasted or Explorer-dropped link or image is actually sent from the desktop app | Paste or drop without sending, canceled drafts, other apps |
| Telegram | After a pasted or Explorer-dropped link or image is actually sent from the desktop app | Paste or drop without sending, canceled drafts, other apps |
| KakaoTalk | When a link or image is pasted into an individual chat, or local images are dropped into that chat | Manually typed URLs, search input, anything outside a chat input |
| LINE | After a pasted or Explorer-dropped link or image is actually sent from the desktop app | Paste or drop without sending, other input fields, sends in another chat |
| WeChat | After a pasted or Explorer-dropped link or image is actually sent from the desktop app | Paste or drop without sending, removed links, non-image files, other input fields, sends in another chat |

Clipboard activity in other apps does not create library items. Detection for each
supported messenger can be enabled or disabled separately in settings.

## Features

- Keep links and images as visual cards
- Store several links or images from one message as a single collection and copy the set again
- Browse each item in a collection, copy it separately, or open the original
- Search by title, URL, domain, or text recognized inside an image, and filter by
  messenger, content type, or date
- Generate useful local image titles with multilingual PP-OCRv5 mobile models
- Open images with a file name that matches the title shown in the library
- Sort by newest, oldest, save count, or copy count
- Mark favorites, select several cards, delete in bulk, and clean up old items automatically
- Show page titles, site icons, preview images, and descriptions for saved links
- Use light or dark mode with Korean, English, Japanese, and Chinese interfaces
- Start with Windows by default on new installations and manage the setting from the tray menu
- Check the latest GitHub Release from settings and open its official download page
- Sync the library between Windows computers through OneDrive, Google Drive, Dropbox,
  MEGA, or a folder selected manually
- Sync photos and links directly to a NAS through a WebDAV shared folder
- Auto-scroll at the library edges during range selection and use the mouse wheel while dragging

## Data storage

Sentory stores links, images, settings, and usage history in this local folder:

```text
%LOCALAPPDATA%\Sentory
```

Your library is not uploaded to a Sentory-operated server, and the app contains no
analytics or advertising trackers. Fetching a link preview may send a normal network
request to the website behind that link. Image OCR runs locally on Windows, and the
recognized text is stored in the Sentory database for search. See the
[privacy and local data notice](./privacy.md) for details.

When sync between computers is enabled, photos are written as regular image files and
links as readable TXT files inside the cloud folder or NAS WebDAV folder you select.
Sentory does not relay this data through a Sentory-operated server. Adds, deletes,
favorites, and copy counts are reconciled between Windows computers connected to the
same storage.

## Download

The current stable version is **2.0.9**. It runs on x64 and ARM64 Windows 10 and 11.
Download the installer or portable package from
[Releases](https://github.com/NudeNyang/Sentory/releases). There is no release schedule
yet for macOS or Linux.

The GitHub edition will remain free. A paid
[Microsoft Store edition](https://apps.microsoft.com/detail/9N6S69D3667D) is also
available for anyone who prefers Store-managed installation and updates or wants to
support development.

See the [Sentory 2.0.9 release notes](./releases/2.0.9.md) for details.

| System | Installer | Portable |
| --- | --- | --- |
| Most Intel or AMD Windows PCs | `Sentory-win-x64-setup.exe` | `Sentory-win-x64-portable.zip` |
| Windows on ARM PCs | `Sentory-win-arm64-setup.exe` | `Sentory-win-arm64-portable.zip` |

The x64 installer is the right choice for most PCs. To use Sentory without installing
it, fully extract the portable ZIP and run `Sentory.exe`. On Windows on ARM, choose a
package with `arm64` in its name. All packages are self-contained and do not require a
separate .NET installation.

The current binaries are not code-signed. Windows may show an Unknown Publisher or
SmartScreen warning. Confirm that the file came from this repository's official
Release, and compare its SHA-256 value with the accompanying `.sha256` file if needed.

## Code signing policy

Sentory is applying to the SignPath Foundation open-source code-signing program. Once
the application is approved, official Windows releases will be built on GitHub-hosted
Actions runners and signed through SignPath.io only after manual approval.

**Free code signing provided by SignPath.io, certificate by SignPath Foundation.**

- Committers and reviewers: [NudeNyang](https://github.com/NudeNyang)
- Approvers: [NudeNyang](https://github.com/NudeNyang)
- Privacy policy: [Privacy and local data](./privacy.md)
- Full policy and current rollout status: [Code signing policy](./code-signing-policy.md)

This statement applies only to files with a valid Authenticode signature issued in
the name of SignPath Foundation. Releases published before approval and pipeline
activation remain unsigned and can be checked with their accompanying SHA-256 files.

## Getting started

1. Run Sentory. The library window and taskbar icon appear together.
2. Paste a link or image into a supported chat, or drop an image from Explorer. In
   Discord, Slack, WhatsApp, Telegram, LINE, and WeChat, send the content before
   Sentory saves it.
3. Open a card in the library to review, open, or copy its contents.

New installations enable Start with Windows by default. You can turn it off from
Sentory settings or the tray menu, and later updates preserve your choice.

Sentory checks the latest stable GitHub Release after startup and every six hours. It
downloads the package matching the current architecture and installation type, verifies
the SHA-256 value, and offers an install action in settings. Installation closes Sentory,
replaces the application files, and starts it again. `Check now` skips the six-hour wait.
Microsoft Store builds continue to receive updates through the Store.

All messenger detection switches start off on a new installation. Choose the messengers
you use on the first screen; existing installations keep their saved choices.

The first time you enable Discord detection, Sentory asks for consent before allowing
automatic restarts and warns that a draft message may be cancelled or a call may end.
The choice is remembered only after Discord detection is actually enabled. People who
already used Discord detection are not asked again. A Discord process missing the
required accessibility argument receives a 15-second restart notice. Ordinary
connection and worker recovery states do not restart Discord.

## Good to know

Detection may temporarily stop if a supported messenger changes its interface. Uploads
opened through a messenger's `+` file picker are not guaranteed to be detected; paste
an image into the chat input or drop it from Explorer instead. The ARM64 packages pass
their build and installation self-checks on an ARM64 Windows virtual machine. Messenger
integration testing on a physical Windows on ARM device is still pending.

When reporting a problem, do not attach private conversations or original images.
The Sentory version, Windows version, messenger name, and reproduction steps are
usually enough. See the [support policy](./support.md) for details.

## Source code and builds

Sentory's source code is published in this repository. The exact source corresponding
to each binary release is available as `Sentory-<version>-source.zip` on the Release
page and from the matching Git tag.

```powershell
git clone https://github.com/NudeNyang/Sentory.git
cd Sentory
dotnet build .\Sentory.sln --configuration Release
dotnet test .\Sentory.sln --configuration Release
.\scripts\Publish-TauriRelease.ps1 -Version 2.0.9 -Architecture x64
.\scripts\Publish-TauriRelease.ps1 -Version 2.0.9 -Architecture arm64
```

The release script creates the installer and portable package for the selected Windows
architecture, SHA-256 checksum files, the corresponding source archive, and
`release-manifest.json` in the `artifacts` directory. Official ARM64 packages are also
built with the same script on a native ARM64 Windows GitHub Actions runner.
Implementation and release details are documented in
[development.md](./development.md) and the
[release guide](./05-release-and-distribution.md).

## License

Sentory is licensed under the **GNU General Public License v3.0 only**. You may use,
study, modify, and redistribute it, including for commercial purposes, provided that
you follow the license terms. If you distribute a modified version or binaries, you
must also provide the corresponding source code and license notices as required by
the GPL. Third-party components remain under their respective licenses.

Copyright © 2026 NudeNyang

- [GNU GPL v3 license text](../LICENSE.txt)
- [Privacy and local data](./privacy.md)
- [Code signing policy](./code-signing-policy.md)
- [Third-party notices](../distribution/THIRD-PARTY-NOTICES.txt)
- [Changelog](../CHANGELOG.md)
- [Support policy](./support.md)

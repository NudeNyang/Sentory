# Sentory

[한국어](../README.md) | [English](./README.en.md)

> Keep the links and images you paste in one place

Sentory is a desktop app for keeping links and images from your messenger chats in a
separate, searchable library. Instead of scrolling through an old conversation, you
can find an item, open the original, or copy it back to the clipboard.

Discord and KakaoTalk are currently supported. Sentory is not a system-wide clipboard
logger. It checks the chat input area of a supported messenger and saves only the
content that meets that messenger's detection rules. Support for more messengers is
planned.

## Supported messengers

| Messenger | When Sentory saves | What Sentory ignores |
| --- | --- | --- |
| Discord | After a pasted link or image is actually sent | Paste without sending, `Shift+Enter`, canceled attachments |
| KakaoTalk | When a link or image is pasted into an individual chat, or local images are dropped into that chat | Manually typed URLs, search input, anything outside a chat input |

Clipboard activity in other apps does not create library items. Detection for Discord
and KakaoTalk can be enabled or disabled separately in settings.

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
- Download verified updates in the background and install them from inside the app

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

## Download

The current stable version is **1.4.0**. It runs on 64-bit Windows 10 and 11. Download
the package for your PC from
[Releases](https://github.com/NudeNyang/Sentory/releases). macOS and Linux versions
are planned, but there is no release schedule yet.

| System | Installer | Portable |
| --- | --- | --- |
| Most Intel or AMD Windows PCs | `Sentory-win-x64-setup.exe` | `Sentory-win-x64-portable.zip` |
| Windows on ARM PCs | `Sentory-win-arm64-setup.exe` | `Sentory-win-arm64-portable.zip` |

The x64 installer is the right choice for most PCs. To use Sentory without installing
it, fully extract the portable ZIP and run `Sentory.exe`. Both packages are
self-contained, so they do not require a separate .NET installation.

The current binaries are not code-signed. Windows may show an Unknown Publisher or
SmartScreen warning. Confirm that the file came from this repository's official
Release, and compare its SHA-256 value with the accompanying `.sha256` file if needed.

## Getting started

1. Run Sentory. The library window and taskbar icon appear together.
2. Paste a link or image into an individual KakaoTalk chat. In Discord, send the pasted
   content before Sentory saves it.
3. Open a card in the library to review, open, or copy its contents.

New installations enable Start with Windows by default. You can turn it off from
Sentory settings or the tray menu, and later updates preserve your choice.

Sentory downloads and verifies an available update before showing the install prompt.
Installed copies update without reopening the setup wizard and start Sentory again when
the update is complete. The manual install button remains available in the library if
you close the first prompt.

The first Discord connection may restart Discord to apply accessibility mode. The
current connection state and separate messenger detection controls are available in
Sentory settings.

## Good to know

Detection may temporarily stop if Discord or KakaoTalk changes its interface. The
Windows on ARM packages have passed cross-build and executable architecture checks,
but final testing on a physical ARM64 device is still pending.

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
.\scripts\Publish-Release.ps1 -Version 1.4.0
```

The release script creates Windows x64 and ARM64 installers, portable packages,
SHA-256 checksum files, the corresponding source archive, and
`release-manifest.json` in the `artifacts` directory.
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
- [Third-party notices](../distribution/THIRD-PARTY-NOTICES.txt)
- [Changelog](../CHANGELOG.md)
- [Support policy](./support.md)

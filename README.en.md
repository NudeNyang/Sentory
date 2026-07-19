# Sentory

[한국어](./README.md) | [English](./README.en.md)

> Keep the links and images you paste in one place

Finding an old link or image should not mean scrolling through days of chat history.
Sentory keeps the links and images handled in supported messengers in a quiet,
searchable library, ready to open or copy again when you need them.

Sentory is not a system-wide clipboard logger. It checks the chat input area of a
supported messenger and saves content only when a defined set of conditions is met.

## Captured only where it belongs

| Messenger | When Sentory saves | What Sentory ignores |
| --- | --- | --- |
| Discord | After a pasted link or image is actually sent | Paste without sending, `Shift+Enter`, canceled attachments |
| KakaoTalk | As soon as a link or image is pasted into an individual chat input | Manually typed URLs, search input, anything outside a chat input |

Copying or pasting in another application does not create an item. Sentory validates
the messenger window and its input area first, so ordinary clipboard activity does
not fill your library.

## Keep it now, find it quickly later

- Browse links and images as visual cards
- Search by title, URL, or domain
- Filter by messenger, content type, and date, then choose the sort order you prefer
- Mark favorites and keep them safe from automatic cleanup
- Open the original item or copy it directly from a card
- Drag to select multiple cards and delete them together
- See page titles, site icons, preview images, and short descriptions for links
- Switch between light and dark themes
- Use the interface in Korean, English, Japanese, or Chinese
- Start with Windows and manage detection from the tray

## Local by design

Images, links, settings, and usage history stay in this local folder:

```text
%LOCALAPPDATA%\Sentory
```

Sentory does not upload your library to a Sentory-operated server and does not include
analytics or tracking. Creating a link preview may send a normal network request to
the website behind that link. See the [privacy and local data notice](./PRIVACY.md)
for details.

## Download

The current release candidate is **0.9.0-beta**. Download the package that matches
your PC from [Releases](../../releases).

| System | Installer | Portable |
| --- | --- | --- |
| Most Intel or AMD Windows PCs | `Sentory-win-x64-setup.exe` | `Sentory-win-x64-portable.zip` |
| Windows on ARM PCs | `Sentory-win-arm64-setup.exe` | `Sentory-win-arm64-portable.zip` |

The x64 installer is the right choice for most PCs. If you prefer not to install the
app, extract the portable ZIP and run `Sentory.exe`. Both packages are self-contained,
so a separate .NET installation is not required.

The current packages are not code-signed. Windows may show an Unknown Publisher or
SmartScreen warning. Make sure the file came from this repository's official Release,
and compare its SHA-256 hash with the included `.sha256` file when needed.

## Getting started

1. Run Sentory. The library opens immediately and the app appears on the taskbar.
2. Paste a link or image into an individual KakaoTalk chat, or paste and send it in Discord.
3. Open Sentory to view the saved card, open the original, or copy it again.

The first Discord setup may restart Discord in accessibility mode. Detection status
and reconnection controls are available from the Sentory library and settings.

## Beta notes

Sentory is currently a public beta. Detection may temporarily stop working if Discord
or KakaoTalk changes its interface structure. The ARM64 package has been cross-built
and its executable architecture has been verified, but final testing on a physical
Windows on ARM device is still pending. This version does not include in-app updates;
download a new package manually when a new Release is published.

When reporting a problem, avoid attaching private chat content or original images.
The Sentory version, Windows version, messenger name, and reproduction steps are
usually enough. See the [support policy](./SUPPORT.md) for details.

## Development and builds

```powershell
dotnet build .\Sentory.sln --configuration Release
dotnet test .\Sentory.sln --configuration Release
.\scripts\Publish-Release.ps1 -Version 0.9.0-beta
```

The release script creates Windows x64 and ARM64 installers, portable packages,
SHA-256 checksum files, and `release-manifest.json` in the `artifacts` directory.
Implementation and release details are documented in [PROJECT.md](./PROJECT.md) and
the [release guide](./docs/05-release-and-distribution.md).

## License

Sentory may be used only for personal, non-commercial purposes. Modification, reverse
engineering, redistribution, or commercial use requires prior written permission
from NudeNyang.

Copyright © 2026 NudeNyang. All rights reserved.

- [Full license terms](./LICENSE.txt)
- [Privacy and local data](./PRIVACY.md)
- [Third-party notices](./THIRD-PARTY-NOTICES.txt)
- [Changelog](./CHANGELOG.md)
- [Support policy](./SUPPORT.md)

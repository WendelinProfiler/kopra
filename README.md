# Kopra

Kopra opens apps, finds files and keeps a local clipboard history. Copied files and folders have their own saved copies, so they remain available after the originals move.

Use Kopra on Windows or macOS by itself. Pair a Mac with a Windows PC when you want new copies to appear on both.

## Getting started

On Windows, the Windows key opens search. The first result opens your clipboard history. On Mac, the default search shortcut is Command + Space. If another app has reserved it, Kopra uses Control + Option + Space and tells you.

While Kopra is visible, it keeps keyboard focus. Its file pickers and other dialogs keep their own input focus. Press Escape to hide Kopra; opening a result or pasting an entry also releases focus.

Change the search and history shortcuts in Settings → General. Press the combination you want. Kopra checks whether it can register it before saving the change.

The current interface is in German. Windows 11 and macOS 13 or later are supported. The Mac package includes Apple silicon and Intel builds.

## Pairing a Mac and Windows PC

1. On Windows, open Settings → Devices → Connect Mac. Kopra shows a six-digit code.
2. On Mac, open Settings → Devices, enter that code and choose Connect.
3. Kopra finds the Windows PC on your local network. Approve the connection on both computers. There is no second code to enter or compare.

The entry code expires after five minutes and can be used once. After five attempts, start a new pairing on Windows. The code is not broadcast on the network. QR scanning and QR-image import remain available as alternatives.

Both computers must be on the same local network. Kopra does not use a cloud service or a login account. SSH is not part of the connection.

Fresh incoming copies can become the current clipboard, ready for normal paste. Offline copies appear in history without replacing a newer local copy. Turn direct clipboard replacement off in Settings → Devices if you prefer to choose each item yourself.

## Your history

Kopra saves text, images, files and folders. Pin entries you want to keep. Retention and storage limits are available as preset choices in Settings → History.

On Mac, Enter opens a saved file or folder; Command + Enter prepares it for pasting. Text entries use Enter to paste. Windows uses Enter to restore and paste the selected clipboard entry. Automatic pasting into another app can depend on operating-system permissions; normal Command + V or Control + V remains available.

Deleting an entry removes Kopra's saved copy. It does not delete the original or the other computer's independent copy. Pins are local to each computer.

Shared copies are limited to 32 MB, with up to 2,000 file entries. Shared text is limited to 1 MB. Larger Windows local captures can remain local. This Mac version applies the 32 MB capture limit locally too.

## Privacy

History stays in your user profile. Kopra has no telemetry or analytics. Pairing uses operating-system cryptography and requires consent on both devices. Keys are protected by Windows DPAPI or the macOS login keychain.

History contents are not additionally encrypted at rest. Kopra honors supported confidential-clipboard markers and excluded apps, but it cannot recognize every password. See [privacy](docs/privacy.md) and [security](SECURITY.md).

## Build

Windows requires the .NET 10 SDK:

```powershell
./support/Build.ps1
```

Mac requires Xcode command-line tools:

```sh
cd mac
zsh Build.sh
```

Builds and tests run in GitHub Actions. See [release builds](docs/releases.md) for packaging, signing and notarization. Unsigned preview packages are labeled as previews; they are not presented as notarized releases.

## License

MIT. Third-party notices are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

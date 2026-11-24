# Changelog

## Unreleased

- Resolve Windows search results from their actual file URLs instead of translated display paths.
- Preserve Mac search filenames containing line breaks and skip stale entries before limiting results.
- Restore native text editing shortcuts in Mac panels and keep modified arrow navigation in text fields.
- Support Control+A and Alt+A in Windows text fields without intercepting AltGr or word deletion.

## 1.0.0 beta 4

- Choose English or German in Settings. New installs use English.
- Language changes apply immediately and stay on each device.
- Fix the clipped search icon and hint when search returns no results.
- Add native Windows and Mac screenshots with example content.
- Keep the search placeholder clear of the caret and bundle Inter for consistent Windows typography.

## 1.0.0 beta 3

- Keep keyboard focus while the app is visible on Windows and Mac.
- Preserve focus in owned dialogs and restore it when those dialogs close.
- Release focus when hiding the app or opening a result.
- Isolate native UI tests from installed preferences and history.

## 1.0.0 beta 2

- Pair a Mac by entering a six-digit code. Kopra finds the Windows PC on the local network.
- Codes expire after five minutes, work once and allow at most five attempts.
- A password-authenticated exchange protects the pairing link. Both devices still require confirmation.
- QR scanning and QR-image import remain available as alternatives.
## 1.0.0 beta 1

- Local app and file search with clipboard history on Windows and Mac.
- Optional device sharing with expiring QR codes and confirmation on both computers.
- Search and history shortcuts can be changed in Settings.
- Saved file and folder copies, previews, pins and storage limits.
- Windows portable package and universal Mac package with checksums.

This is a preview release. Public stable downloads still need publisher signing and Mac notarization.

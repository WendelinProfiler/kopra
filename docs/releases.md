# Release builds

The repository builds a self-contained Windows app and a universal Mac app in GitHub Actions. The default packages are unsigned previews. They contain no device keys, history or local build configuration.

## Preview packages

Run `support/Build.ps1` and `support/Package-Windows.ps1` on Windows. On Mac, run `zsh mac/Build.sh` and `zsh mac/Package.sh`.

Packages and SHA-256 checksum files appear under `artifacts`. The test fixtures stay outside the app packages.

## Trusted distribution

A Windows release should be Authenticode-signed with a certificate held by the publisher. A Mac release should be signed with a Developer ID Application identity and notarized by Apple. These credentials belong in CI secrets or a local signing keychain, never in this repository.

The Mac build accepts `CODE_SIGN_IDENTITY`. Without it, the build uses an ad-hoc signature for local testing. With a Developer ID identity, it enables the hardened runtime and the camera entitlement. Submit the resulting archive with `xcrun notarytool`, wait for acceptance and staple the ticket before publishing a stable download.

Keep unsigned packages marked as previews. Do not tell users to disable operating-system security features globally. They should verify the checksum and use the normal per-app approval path for a preview build.

## Before publishing

Run the Windows and Mac tests. Verify camera denial and QR-image import, expired codes, cancellation, confirmation on both devices and duplicate shortcut rejection. Test one fresh standalone install on each platform before pairing them.

A stable release also needs the signing and notarization checks above. The protocol has automated tests, not an independent security audit. Keep that distinction in the release notes.

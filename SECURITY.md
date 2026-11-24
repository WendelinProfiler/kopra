# Security

Pair devices you own and trust. The QR code is temporary, and both devices must confirm the same six-digit code before Kopra stores a connection key.

Pairing uses ephemeral P-256 key agreement, HKDF-SHA256 and an authenticated QR token. Clipboard messages use AES-256-GCM with fresh nonces. The Windows receiver validates the paired identity, request timestamps, payload bounds and relative file paths. Confirmed entry IDs prevent ordinary transfer retries from creating another copy.

The protocol uses .NET and Apple CryptoKit primitives. It has automated tests and has not had an independent security audit. Do not describe a preview build as independently audited.

Windows networking is optional. An unpaired installation does not listen for clipboard transfers. Showing a pairing code opens the pairing listener; closing or expiring that flow stops it when no peer is configured. The optional firewall helper permits only Kopra on TCP 53328 and UDP 53329 from the local subnet. It does not change the network profile.

Local history is not additionally encrypted at rest. Removing history does not guarantee that the operating system, filesystem snapshots or backups have erased every previous byte.

Use GitHub's private vulnerability reporting to report security issues. Include the app version and the steps needed to reproduce the issue. Do not include clipboard contents, pairing links or device keys.

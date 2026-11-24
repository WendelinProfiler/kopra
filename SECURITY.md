# Security

Pair devices you own and trust. Entry codes and QR codes are temporary. Numeric pairing requires the entry code and approval on both devices before Kopra stores a connection key. The QR alternative also shows a comparison code.

Numeric entry first uses SRP-6a with the fixed 2048-bit group from [RFC 5054](https://www.rfc-editor.org/rfc/rfc5054), SHA-256, a fresh salt, transcript-bound HKDF keys and mutual HMAC key confirmation. This authenticates the temporary pairing link without transmitting the code. Five attempts are allowed per five-minute session, across all clients. It is a Kopra protocol profile, not the RFC TLS handshake.

The authenticated link then uses ephemeral P-256 key agreement, HKDF-SHA256 and an authenticated QR token. Clipboard messages use AES-256-GCM with fresh nonces. The Windows receiver validates the paired identity, request timestamps, payload bounds and relative file paths. Confirmed entry IDs prevent ordinary transfer retries from creating another copy.

The protocol uses .NET and Apple CryptoKit primitives. It has automated tests and has not had an independent security audit. Do not describe a preview build as independently audited.

Windows networking is optional. An unpaired installation does not listen for clipboard transfers. Showing a pairing code opens the pairing listener; closing or expiring that flow stops it when no peer is configured. The optional firewall helper permits only Kopra on TCP 53328 and UDP 53329 from the local subnet. It does not change the network profile.

Local history is not additionally encrypted at rest. Removing history does not guarantee that the operating system, filesystem snapshots or backups have erased every previous byte.

Report suspected vulnerabilities privately to the repository maintainer. Include the app version and the steps needed to reproduce the issue. Do not post clipboard contents, pairing links or device keys in a public issue.

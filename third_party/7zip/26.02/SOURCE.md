# 7-Zip 26.02 x64

This directory contains the exact 7-Zip x64 command-line engine used by V2.

## Source

- Product: 7-Zip
- Version: 26.02
- Architecture: x64
- Official release: https://github.com/ip7z/7zip/releases/tag/26.02
- Official download page: https://www.7-zip.org/download.html
- Source tree: https://github.com/ip7z/7zip/tree/26.02
- Installer asset: `7z2602-x64.exe`
- Installer SHA-256: `6745fa76dc2ea031596d8678f6f6b99c3c1b435b4164a63485adbbc7b8d82ef0`

The application vendors only the extracted `7z.exe` and `7z.dll` files. The
installer itself is not committed. The binaries were extracted from the
official x64 installer and their hashes are recorded in `SHA256SUMS`.

## Update procedure

1. Choose an official stable x64 release.
2. Record the release URL and installer SHA-256 here.
3. Extract only `7z.exe` and `7z.dll` from the official installer.
4. Update `SHA256SUMS` and `THIRD-PARTY-NOTICES.md`.
5. Run `scripts/verify-third-party.ps1`.
6. Run the complete Release and four-format integration test suite.
7. Do not update the version as part of an unrelated code change.
